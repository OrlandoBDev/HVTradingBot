using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Bridge;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Infrastructure.Brokers.Mt5;

/// <summary>A market order: the risk engine's units (the gateway turns them into lots, never rounding up).</summary>
public sealed record Mt5OpenRequest(string Symbol, bool Buy, decimal Units, decimal StopLoss, decimal TakeProfit, string ClientId);

/// <summary>
/// The answer to an order. <see cref="Done"/>: executed as position <see cref="PositionId"/> with <see cref="Units"/>
/// (lots × contract size); otherwise refused with <see cref="Message"/> and nothing was traded. <see cref="Commission"/>
/// is as MT5 reports it (negative: a cost).
/// </summary>
public sealed record Mt5OpenResult(bool Done, string Code, string Message, string? PositionId, decimal? Price, decimal Units, decimal? Commission);

/// <summary>MT5 answered clearly that it will not do this (bad token, unknown account, invalid request): nothing happened.</summary>
public sealed class Mt5RefusedException(string message, bool authentication = false, bool notFound = false) : Exception(message)
{
    public bool Authentication { get; } = authentication;

    public bool NotFound { get; } = notFound;
}

/// <summary>
/// How <see cref="Mt5Broker"/> reaches the MT5 account: MetaApi's cloud, or the Expert Advisor bridge on this computer.
/// A lost answer (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>, <see cref="TimeoutException"/>)
/// means the outcome is unknown; <see cref="Mt5RefusedException"/> and not-done results mean nothing was traded;
/// <see cref="BrokerUnavailableException"/> means it cannot be reached at all right now.
/// </summary>
public interface IMt5Gateway
{
    Task<Mt5AccountInfo> GetAccountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Mt5Position>> GetPositionsAsync(CancellationToken cancellationToken);

    /// <summary>Deals of one position (opening and closing); empty until MT5 has recorded them.</summary>
    Task<IReadOnlyList<Mt5Deal>> GetDealsAsync(string positionId, CancellationToken cancellationToken);

    Task<Mt5OpenResult> OpenAsync(Mt5OpenRequest request, CancellationToken cancellationToken);

    Task<Mt5TradeResult> CloseAsync(string positionId, CancellationToken cancellationToken);
}

/// <summary>MT5 through MetaApi's cloud REST API (see <see cref="MetaApiClient"/>).</summary>
public sealed class MetaApiGateway(MetaApiClient api, Mt5SettingsStore settings) : IMt5Gateway
{
    private readonly Dictionary<string, Mt5SymbolSpec> _specs = new(StringComparer.Ordinal);
    private Mt5Credentials? _credentials;
    private bool _connected;

    public async Task<Mt5AccountInfo> GetAccountAsync(CancellationToken cancellationToken)
    {
        var credentials = await CredentialsAsync(cancellationToken);
        try
        {
            var info = await api.GetAccountAsync(credentials, cancellationToken);
            _connected = true;
            return info;
        }
        catch (MetaApiException ex)
        {
            throw Refused(ex);
        }
    }

    public async Task<IReadOnlyList<Mt5Position>> GetPositionsAsync(CancellationToken cancellationToken) =>
        await Call(c => api.GetPositionsAsync(c, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<Mt5Deal>> GetDealsAsync(string positionId, CancellationToken cancellationToken) =>
        await Call(c => api.GetDealsAsync(c, positionId, cancellationToken), cancellationToken);

    public async Task<Mt5OpenResult> OpenAsync(Mt5OpenRequest request, CancellationToken cancellationToken)
    {
        var credentials = await CredentialsAsync(cancellationToken);
        Mt5SymbolSpec spec;
        try
        {
            if (!_specs.TryGetValue(request.Symbol, out spec!))
            {
                spec = await api.GetSpecificationAsync(credentials, request.Symbol, cancellationToken);
                _specs[request.Symbol] = spec;
            }
        }
        catch (Exception ex) when (ex is MetaApiException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new Mt5OpenResult(false, "SYMBOL", $"Symbol {request.Symbol} is not available on the MT5 account: {ex.Message}", null, null, 0, null);
        }

        // Units to lots, rounded down to the broker's step (never more risk than the engine sized).
        var lots = Math.Min(Math.Floor(request.Units / spec.ContractSize / spec.VolumeStep) * spec.VolumeStep, spec.MaxVolume);
        if (lots < spec.MinVolume)
        {
            return new Mt5OpenResult(false, "VOLUME",
                $"{request.Units:N0} units is below the broker minimum of {spec.MinVolume} lot ({spec.MinVolume * spec.ContractSize:N0} units); raise the risk per trade or the trading capital.",
                null, null, 0, null);
        }

        Mt5TradeResult result;
        try
        {
            result = await api.OpenAsync(credentials, request.Symbol, request.Buy, lots, Math.Round(request.StopLoss, spec.Digits),
                Math.Round(request.TakeProfit, spec.Digits), request.ClientId, cancellationToken);
        }
        catch (MetaApiException ex)
        {
            throw Refused(ex);
        }

        if (!result.Done || result.PositionId is null)
        {
            return new Mt5OpenResult(false, result.Code, result.Message, null, null, 0, null);
        }

        var opened = (await api.GetPositionsAsync(credentials, cancellationToken)).FirstOrDefault(p => p.Id == result.PositionId);
        return new Mt5OpenResult(true, result.Code, result.Message, result.PositionId, opened?.OpenPrice, lots * spec.ContractSize,
            opened?.Commission);
    }

    public async Task<Mt5TradeResult> CloseAsync(string positionId, CancellationToken cancellationToken) =>
        await Call(c => api.CloseAsync(c, positionId, cancellationToken), cancellationToken);

    private async Task<T> Call<T>(Func<Mt5Credentials, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call(await CredentialsAsync(cancellationToken));
        }
        catch (MetaApiException ex)
        {
            throw Refused(ex);
        }
    }

    /// <summary>Read again until the account connects, so corrected settings are picked up while waiting.</summary>
    private async Task<Mt5Credentials> CredentialsAsync(CancellationToken cancellationToken)
    {
        if (_credentials is null || !_connected)
        {
            _credentials = await settings.GetActiveAsync(cancellationToken)
                           ?? throw new BrokerUnavailableException("MT5 is not set up: enter the MetaApi token and account id in Settings › Broker account.");
        }

        return _credentials;
    }

    private static Mt5RefusedException Refused(MetaApiException ex) => new(
        ex.IsAuthentication ? "MetaApi rejected the token." : ex.IsNotFound ? $"MetaApi: not found ({ex.Message})." : ex.Message,
        ex.IsAuthentication, ex.IsNotFound);
}

/// <summary>
/// MT5 through the HVTradingBot Expert Advisor running in MetaTrader 5 on the same computer (free). Commands go through
/// <see cref="Mt5BridgeStore"/>; the Expert Advisor picks them up within a second and reports the results.
/// </summary>
public sealed class BridgeGateway(Mt5BridgeStore bridge, IClock clock, TimeSpan? resultTimeout = null, TimeSpan? poll = null) : IMt5Gateway
{
    /// <summary>The Expert Advisor reports every second; after this long without a report it is treated as offline.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _resultTimeout = resultTimeout ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan _poll = poll ?? TimeSpan.FromMilliseconds(250);

    public async Task<Mt5AccountInfo> GetAccountAsync(CancellationToken cancellationToken)
    {
        var a = (await OnlineStateAsync(cancellationToken)).Account;
        return new Mt5AccountInfo(a.Login.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Server, a.Company, a.Currency, a.Balance,
            a.Equity, a.TradeMode);
    }

    public async Task<IReadOnlyList<Mt5Position>> GetPositionsAsync(CancellationToken cancellationToken) =>
        (await OnlineStateAsync(cancellationToken)).Positions.Select(p => new Mt5Position(Id(p.Ticket), p.Symbol, p.Type == "BUY", p.Volume, p.PriceOpen,
            p.Sl == 0 ? null : p.Sl, p.Tp == 0 ? null : p.Tp, p.Profit, 0, p.Swap, DateTimeOffset.FromUnixTimeSeconds(p.Time).UtcDateTime, p.Comment,
            p.ContractSize)).ToList();

    public async Task<IReadOnlyList<Mt5Deal>> GetDealsAsync(string positionId, CancellationToken cancellationToken) =>
        (await OnlineStateAsync(cancellationToken)).Deals.Where(d => Id(d.PositionId) == positionId)
        .OrderBy(d => d.Time)
        .Select(d => new Mt5Deal(Id(d.Ticket), $"DEAL_ENTRY_{d.Entry}", d.Price, d.Profit, d.Commission, d.Swap,
            DateTimeOffset.FromUnixTimeSeconds(d.Time).UtcDateTime, $"DEAL_REASON_{d.Reason}"))
        .ToList();

    public async Task<Mt5OpenResult> OpenAsync(Mt5OpenRequest request, CancellationToken cancellationToken)
    {
        await OnlineStateAsync(cancellationToken); // offline: refused before anything is queued
        var id = await bridge.EnqueueOpenAsync(new BridgeOpen(request.Symbol, request.Buy, request.Units, request.StopLoss, request.TakeProfit,
            request.ClientId), cancellationToken);
        var result = await ResultAsync(id, cancellationToken);
        return result.Ok && result.Ticket is { } ticket
            ? new Mt5OpenResult(true, Code(result), result.Message, Id(ticket), result.Price, result.Units ?? 0, result.Commission)
            : new Mt5OpenResult(false, Code(result), result.Message, null, null, 0, null);
    }

    public async Task<Mt5TradeResult> CloseAsync(string positionId, CancellationToken cancellationToken)
    {
        await OnlineStateAsync(cancellationToken);
        var id = await bridge.EnqueueCloseAsync(new BridgeClose(long.Parse(positionId, System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        var result = await ResultAsync(id, cancellationToken);
        return new Mt5TradeResult(result.Ok ? "TRADE_RETCODE_DONE" : Code(result), result.Message, null, positionId);
    }

    /// <summary>
    /// Waits for the Expert Advisor. Not picked up in time: withdrawn, so it is a plain refusal. Picked up but no result:
    /// it may or may not have executed, which is the "unknown" case the broker reconciles by client id.
    /// </summary>
    private async Task<BridgeResult> ResultAsync(long id, CancellationToken cancellationToken)
    {
        if (await bridge.WaitForResultAsync(id, _resultTimeout, _poll, cancellationToken) is { } result)
        {
            return result;
        }

        if (await bridge.ExpireIfPendingAsync(id, cancellationToken))
        {
            throw new Mt5RefusedException("The MT5 Expert Advisor did not pick up the order in time; nothing was sent.");
        }

        throw new TimeoutException("The MT5 Expert Advisor took the order but did not report the result in time.");
    }

    private async Task<BridgeState> OnlineStateAsync(CancellationToken cancellationToken)
    {
        var state = await bridge.GetStateAsync(cancellationToken)
                    ?? throw new BrokerUnavailableException("The MT5 bridge has not connected yet: start MetaTrader 5 with the HVTradingBot Expert Advisor.");
        if (clock.UtcNow - state.LastSeenUtc > OfflineAfter)
        {
            throw new BrokerUnavailableException(
                $"The MT5 Expert Advisor last reported {(clock.UtcNow - state.LastSeenUtc).TotalMinutes:0} min ago: is MetaTrader 5 running with the HVTradingBot Expert Advisor?");
        }

        return state;
    }

    private static string Code(BridgeResult r) => r.Ok ? "TRADE_RETCODE_DONE" : $"RETCODE_{r.Retcode}";

    private static string Id(long ticket) => ticket.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
