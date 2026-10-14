using System.Security.Cryptography;
using System.Text;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers.Mt5;

/// <summary>
/// Executes Forex trades on a MetaTrader 5 demo account, through MetaApi's cloud or the Expert Advisor bridge on this
/// computer (<see cref="IMt5Gateway"/>): market orders with broker-side stop loss and take profit, sized in lots from
/// the risk engine's units. Same rules as the Deriv broker: every submission is recorded
/// first (a signal is never sent twice), a timeout means "outcome unknown" (the engine turns on the kill switch until
/// it is reconciled), the broker is authoritative for open positions, and real accounts are refused.
/// </summary>
public sealed class Mt5Broker(
    IMt5Gateway gateway,
    Mt5SettingsStore settings,
    IDbContextFactory<TradingDbContext> dbFactory,
    TradingEngineOptions engineOptions,
    IClock clock,
    ILogger<Mt5Broker> logger) : IExecutionBroker
{
    public const string BrokerName = Mt5SettingsStore.BrokerName;
    private static readonly TimeSpan UnknownMatchWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<Instrument, Quote> _quotes = new();
    private (DateTime BarClose, IReadOnlyList<Mt5Position> Positions)? _positionsCache;
    private string? _suffix;
    private string? _accountId;
    private bool _isDemo = true;
    private string? _lastStatus;

    public BrokerDescriptor Descriptor => new(BrokerName, _accountId, _isDemo);

    public void UpdateQuotes(IEnumerable<Quote> quotes)
    {
        foreach (var quote in quotes)
        {
            _quotes[quote.Instrument] = quote;
        }
    }

    /// <summary>Account balance; also checks the account (demo only, currency) and reports the result on the Settings page.</summary>
    public async Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        _suffix ??= await settings.GetSymbolSuffixAsync(cancellationToken);
        Mt5AccountInfo info;
        try
        {
            info = await gateway.GetAccountAsync(cancellationToken);
        }
        catch (Mt5RefusedException ex)
        {
            var message = ex.NotFound ? "MetaApi does not know this account id." : ex.Message;
            await ReportAsync("Failed", message, null, cancellationToken);
            throw new BrokerUnavailableException($"MT5: {message}", ex);
        }
        catch (BrokerUnavailableException ex)
        {
            await ReportAsync("Failed", ex.Message, null, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested)
        {
            await ReportAsync("Failed", $"MT5 not reachable: {ex.Message}", null, cancellationToken);
            throw new BrokerUnavailableException($"MT5 not reachable: {ex.Message}", ex);
        }

        // ADR-008: demo accounts only. MetaApi reports the trade mode; older answers without it fall back to the server name.
        var isDemo = info.TradeMode is { } mode
            ? mode is "ACCOUNT_TRADE_MODE_DEMO" or "ACCOUNT_TRADE_MODE_CONTEST"
            : info.Server.Contains("demo", StringComparison.OrdinalIgnoreCase);
        if (!isDemo)
        {
            await ReportAsync("Failed", $"{info.Server} {info.Login} is a real-money account; only demo accounts are allowed.", info, cancellationToken);
            throw new BrokerUnavailableException("MT5: real-money accounts are not allowed; use an MT5 demo account.");
        }

        if (!string.Equals(info.Currency, engineOptions.AccountCurrency, StringComparison.OrdinalIgnoreCase))
        {
            await ReportAsync("Failed", $"The account currency {info.Currency} does not match the app's {engineOptions.AccountCurrency}.", info,
                cancellationToken);
            throw new BrokerUnavailableException($"MT5: account currency {info.Currency} does not match {engineOptions.AccountCurrency}.");
        }

        _isDemo = true;
        _accountId = $"MT5-{info.Login}";
        await ReportAsync("Connected", null, info, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerAccounts.SingleOrDefaultAsync(a => a.AccountKey == _accountId, cancellationToken);
        if (row is null)
        {
            row = new BrokerAccountEntity { AccountKey = _accountId, Broker = BrokerName, Currency = info.Currency, StartingBalance = info.Balance };
            db.BrokerAccounts.Add(row);
        }

        row.IsDemo = true;
        row.LastBalance = info.Balance;
        row.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new BrokerAccount(info.Currency, info.Balance, row.StartingBalance);
    }

    public async Task<IReadOnlyCollection<OpenPosition>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        var accountId = await AccountIdAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var local = await db.Positions.AsNoTracking()
            .Where(p => p.IsOpen && p.Broker == BrokerName && p.BrokerAccountId == accountId)
            .ToListAsync(cancellationToken);
        var result = local.Select(ToModel).ToList();

        // Broker state is authoritative: positions opened outside this app still count toward exposure limits.
        var tracked = local.Select(p => p.BrokerContractId).ToHashSet();
        foreach (var position in await LivePositionsAsync(null, cancellationToken))
        {
            if (!tracked.Contains(position.Id) && Mt5Symbols.ToInstrument(position.Symbol, _suffix ?? "") is { } instrument)
            {
                result.Add(new OpenPosition(Guid.Empty, $"external-{position.Id}", instrument, position.IsBuy ? Direction.Long : Direction.Short, 0, 0, 0,
                    0, 0, position.OpenedAtUtc, "External", 0, 0, 0));
            }
        }

        return result;
    }

    public async Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().Where(o => o.Broker == BrokerName)
            .OrderByDescending(o => o.RecordedAtUtc).Take(500)
            .Select(o => new BrokerOrder(o.Id, o.ClientOrderId, o.Instrument, o.Direction, o.Units, o.FillPrice,
                Enum.Parse<OrderStatus>(o.Status), o.RejectReason, o.MarketTimeUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<Candle>> GetCandlesAsync(Instrument instrument, TimeFrame timeFrame, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Candle>>([]);

    public async Task<OrderResult> PlaceOrderAsync(TradeOrder order, CancellationToken cancellationToken)
    {
        var accountId = await AccountIdAsync(cancellationToken);

        // 1. Idempotency: the unique index on client_order_id admits exactly one submission per signal.
        var entity = NewOrder(order, accountId);
        if (await OrderLedger.TryInsertAsync(dbFactory, entity, logger, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        if (Mt5Symbols.For(order.Instrument, _suffix ?? "") is not { } symbol)
        {
            return await RejectAsync(entity, $"{order.Instrument.DisplayName} is not Forex: MT5 trades Forex here. Turn MT5 off to trade other markets on Deriv.",
                cancellationToken);
        }

        // 2. Send (the gateway turns units into lots, rounding down). A clear refusal means "not traded"; a lost answer
        //    means "unknown".
        var clientId = ClientId(order.ClientOrderId);
        Mt5OpenResult result;
        var submittedAt = clock.UtcNow;
        try
        {
            result = await gateway.OpenAsync(new Mt5OpenRequest(symbol, order.Direction == Direction.Long, order.Units, order.StopLoss, order.TakeProfit,
                clientId), cancellationToken);
        }
        catch (Mt5RefusedException ex)
        {
            return await RejectAsync(entity, $"MT5 refused the order: {ex.Message}", cancellationToken);
        }
        catch (BrokerUnavailableException ex)
        {
            return await RejectAsync(entity, ex.Message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Execution state unknown for {ClientOrderId}; reconciling with MT5 positions", order.ClientOrderId);
            await OrderLedger.UpdateAsync(dbFactory, entity.Id, o => { o.Status = nameof(OrderStatus.Unknown); o.RejectReason = ex.Message; }, cancellationToken);
            _positionsCache = null;
            var recovered = await TryResolveUnknownAsync(entity.Id, cancellationToken);
            return recovered ?? new OrderResult(order.ClientOrderId, OrderStatus.Unknown, entity.Id, null, null,
                "MT5 order outcome unknown after a connection failure; the order will not be retried automatically.");
        }

        if (!result.Done || result.PositionId is null)
        {
            var reason = result.Code is "VOLUME" or "SYMBOL" ? result.Message : $"MT5 refused the order: {result.Code} {result.Message}".Trim();
            return await RejectAsync(entity, reason, cancellationToken);
        }

        _positionsCache = null;
        var entry = result.Price ?? (_quotes.TryGetValue(order.Instrument, out var q) ? (order.Direction == Direction.Long ? q.Ask : q.Bid) : order.RequestedPrice);
        var position = await RecordFillAsync(entity.Id, order, accountId, result.PositionId, entry, order.StopLoss, order.TakeProfit, result.Units,
            result.Commission, submittedAt, cancellationToken);
        logger.LogInformation("MT5 position {PositionId} opened: {Symbol} {Side} {Units} units at {Entry} SL {StopLoss} TP {TakeProfit}",
            result.PositionId, symbol, order.Direction, result.Units, entry, order.StopLoss, order.TakeProfit);
        return new OrderResult(order.ClientOrderId, OrderStatus.Filled, entity.Id, position.Id, entry, null);
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OrderResult.Rejected(orderId, "Market orders open immediately; there is nothing pending to cancel."));

    public async Task<OrderResult> ClosePositionAsync(string positionId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(positionId, out var id))
        {
            return OrderResult.Rejected(positionId, "Invalid position id.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Broker == BrokerName, cancellationToken);
        if (position?.BrokerContractId is not { } mt5Id)
        {
            return OrderResult.Rejected(positionId, "Unknown MT5 position.");
        }

        try
        {
            var result = await gateway.CloseAsync(mt5Id, cancellationToken);
            if (!result.Done && !result.Code.Contains("POSITION_CLOSED", StringComparison.Ordinal))
            {
                return OrderResult.Rejected(position.ClientOrderId, $"MT5 refused the close: {result.Code} {result.Message}".Trim());
            }
        }
        catch (Mt5RefusedException ex) when (ex.NotFound)
        {
            // Already closed (stop, target, or closed elsewhere): reconciliation reads the result from the deal history.
            logger.LogInformation("MT5 position {PositionId} is no longer open: {Message}", mt5Id, ex.Message);
        }
        catch (Mt5RefusedException ex)
        {
            return OrderResult.Rejected(position.ClientOrderId, $"MT5 refused the close: {ex.Message}");
        }
        catch (BrokerUnavailableException ex)
        {
            return OrderResult.Rejected(position.ClientOrderId, ex.Message);
        }

        _positionsCache = null; // the next reconciliation must see the close
        return new OrderResult(position.ClientOrderId, OrderStatus.Filled, position.OrderId, position.Id, null, null);
    }

    public async Task<IReadOnlyList<ClosedPosition>> ProcessBarAsync(Instrument instrument, Candle bar, CurrencyConverter converter,
        CancellationToken cancellationToken)
    {
        _quotes[instrument] = new Quote(instrument, bar.CloseTimeUtc, instrument.RoundPrice(bar.CloseBid), instrument.RoundPrice(bar.CloseAsk));
        var accountId = await AccountIdAsync(cancellationToken);
        await ResolveUnknownOrdersAsync(instrument, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var symbol = instrument.Symbol;
        var open = await db.Positions
            .Where(p => p.IsOpen && p.Broker == BrokerName && p.BrokerAccountId == accountId && p.Instrument == symbol)
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        HashSet<string> live;
        try
        {
            live = (await LivePositionsAsync(bar.CloseTimeUtc, cancellationToken)).Select(p => p.Id).ToHashSet();
        }
        catch (BrokerUnavailableException ex)
        {
            // Without a current view of MT5 nothing may be treated as closed: keep tracking until it is reachable again.
            logger.LogWarning("MT5 positions not available; reconciliation skipped: {Message}", ex.Message);
            await db.SaveChangesAsync(cancellationToken);
            return [];
        }

        var closed = new List<ClosedPosition>();
        foreach (var entity in open)
        {
            var tracked = PaperExecutionModel.TrackExcursion(ToModel(entity), bar);
            entity.MaxFavorableExcursion = tracked.MaxFavorableExcursion;
            entity.MaxAdverseExcursion = tracked.MaxAdverseExcursion;
            if (entity.BrokerContractId is not { } mt5Id || live.Contains(mt5Id))
            {
                continue;
            }

            var deals = await gateway.GetDealsAsync(mt5Id, cancellationToken);
            var exit = deals.LastOrDefault(d => d.EntryType is "DEAL_ENTRY_OUT" or "DEAL_ENTRY_OUT_BY" or "DEAL_ENTRY_INOUT");
            if (exit is null)
            {
                continue; // Not in the history yet; check again next bar.
            }

            // Profit net of every cost on the position: commission on opening and closing, and swap.
            var profit = deals.Sum(d => d.Profit + d.Commission + d.Swap);
            var reason = exit.Reason switch
            {
                "DEAL_REASON_SL" => ExitReason.StopLoss,
                "DEAL_REASON_TP" => ExitReason.TakeProfit,
                _ => DerivContractMath.ClassifyExit(tracked.Direction, exit.Price, entity.StopLoss, entity.TakeProfit) switch
                {
                    ExitClassification.TakeProfit => ExitReason.TakeProfit,
                    ExitClassification.StopLoss => ExitReason.StopLoss,
                    _ => ExitReason.Manual
                }
            };

            var r = PaperExecutionModel.RMultiple(tracked, profit);
            entity.IsOpen = false;
            entity.ClosedAtUtc = exit.TimeUtc;
            entity.ExitPrice = exit.Price;
            entity.ExitReason = reason.ToString();
            entity.RealizedPnl = profit;
            entity.Commission = -deals.Sum(d => d.Commission);
            entity.RMultiple = r;
            entity.MaePips = Math.Round(instrument.ToPips(tracked.MaxAdverseExcursion), 1);
            entity.MfePips = Math.Round(instrument.ToPips(tracked.MaxFavorableExcursion), 1);
            closed.Add(new ClosedPosition(tracked, exit.TimeUtc, exit.Price, reason, profit, r));
        }

        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    /// <summary>
    /// A short, stable id for the order at MT5 (MetaApi allows 26 characters for comment and client id together), so a
    /// retry or reconciliation finds the position this app opened for a signal.
    /// </summary>
    public static string ClientId(string clientOrderId) =>
        "HV" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientOrderId)))[..12];

    private async Task<string> AccountIdAsync(CancellationToken cancellationToken)
    {
        if (_accountId is null)
        {
            await GetAccountAsync(cancellationToken);
        }

        return _accountId!;
    }

    /// <summary>Open positions on the MT5 account, cached per bar so one cycle makes one call.</summary>
    private async Task<IReadOnlyList<Mt5Position>> LivePositionsAsync(DateTime? barClose, CancellationToken cancellationToken)
    {
        if (barClose is not null && _positionsCache is { } cache && cache.BarClose == barClose)
        {
            return cache.Positions;
        }

        var positions = await gateway.GetPositionsAsync(cancellationToken);
        if (barClose is not null)
        {
            _positionsCache = (barClose.Value, positions);
        }

        return positions;
    }

    private async Task ResolveUnknownOrdersAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var symbol = instrument.Symbol;
        var unknown = await db.Orders.AsNoTracking()
            .Where(o => o.Broker == BrokerName && o.Status == nameof(OrderStatus.Unknown) && o.Instrument == symbol)
            .Select(o => o.Id).ToListAsync(cancellationToken);
        foreach (var id in unknown)
        {
            await TryResolveUnknownAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Looks for the MT5 position carrying this order's client id. If found the order is marked filled and tracked;
    /// otherwise it stays Unknown (and the kill switch stays on) for a human to review.
    /// </summary>
    private async Task<OrderResult?> TryResolveUnknownAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        var clientId = ClientId(order.ClientOrderId);
        IReadOnlyList<Mt5Position> positions;
        try
        {
            positions = await LivePositionsAsync(null, cancellationToken);
        }
        catch (Exception ex) when (ex is Mt5RefusedException or BrokerUnavailableException or HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var match = positions.FirstOrDefault(p => p.ClientId == clientId
                                                 && p.OpenedAtUtc >= order.RecordedAtUtc - UnknownMatchWindow
                                                 && p.OpenedAtUtc <= order.RecordedAtUtc + UnknownMatchWindow);
        if (match is null)
        {
            return null;
        }

        logger.LogWarning("Resolved unknown order {ClientOrderId} to MT5 position {PositionId}", order.ClientOrderId, match.Id);
        var instrument = Instruments.Get(order.Instrument);
        var direction = Enum.Parse<Direction>(order.Direction);
        var tradeOrder = new TradeOrder(order.ClientOrderId, instrument, direction, order.Units, order.RequestedPrice, order.StopLoss, order.TakeProfit,
            0, "Recovered", 0, order.DecisionId, order.CorrelationId);
        var position = await RecordFillAsync(order.Id, tradeOrder, order.BrokerAccountId ?? _accountId!, match.Id, match.OpenPrice,
            match.StopLoss ?? order.StopLoss, match.TakeProfit ?? order.TakeProfit, match.Volume * match.ContractSize, match.Commission,
            match.OpenedAtUtc, cancellationToken);
        return new OrderResult(order.ClientOrderId, OrderStatus.Filled, order.Id, position.Id, match.OpenPrice, null);
    }

    private async Task<PositionEntity> RecordFillAsync(Guid orderId, TradeOrder order, string accountId, string mt5PositionId, decimal entry,
        decimal stop, decimal target, decimal units, decimal? commission, DateTime openedAt, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        entity.Status = nameof(OrderStatus.Filled);
        entity.FillPrice = entry;
        entity.BrokerContractId = mt5PositionId;
        entity.RejectReason = null;

        var position = new PositionEntity
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ClientOrderId = order.ClientOrderId,
            Broker = BrokerName,
            BrokerAccountId = accountId,
            BrokerContractId = mt5PositionId,
            Instrument = order.Instrument.Symbol,
            Direction = order.Direction.ToString(),
            Units = units,
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = target,
            // Units may round down to the lot step: the risk scales with them.
            InitialRiskAmount = order.Units == 0 ? order.RiskAmount : Math.Round(order.RiskAmount * units / order.Units, 2),
            OpenedAtUtc = _quotes.TryGetValue(order.Instrument, out var q) ? q.TimestampUtc : openedAt,
            Strategy = order.Strategy,
            Score = order.Score,
            Commission = commission is { } c ? -c : null,
            IsOpen = true
        };
        db.Positions.Add(position);
        await db.SaveChangesAsync(cancellationToken);
        return position;
    }

    private async Task<OrderResult> RejectAsync(OrderEntity entity, string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("MT5 order {ClientOrderId} rejected: {Reason}", entity.ClientOrderId, reason);
        await OrderLedger.UpdateAsync(dbFactory, entity.Id, o => { o.Status = nameof(OrderStatus.Rejected); o.RejectReason = reason; }, cancellationToken);
        return OrderResult.Rejected(entity.ClientOrderId, reason);
    }

    /// <summary>Writes the connection result for the Settings page when it changes.</summary>
    private async Task ReportAsync(string state, string? message, Mt5AccountInfo? info, CancellationToken cancellationToken)
    {
        var key = $"{state}|{message}|{info?.Balance}";
        if (key == _lastStatus)
        {
            return;
        }

        _lastStatus = key;
        try
        {
            await settings.WriteStatusAsync(new Mt5Status(state, message, await settings.GetVersionAsync(cancellationToken), info?.Login, info?.Server, info?.Broker,
                info is null ? null : state == "Connected", info?.Balance, info?.Currency, clock.UtcNow), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record the MT5 connection status");
        }
    }

    private OrderEntity NewOrder(TradeOrder order, string accountId) => new()
    {
        Id = Guid.NewGuid(),
        ClientOrderId = order.ClientOrderId,
        Broker = BrokerName,
        BrokerAccountId = accountId,
        DecisionId = order.DecisionId,
        CorrelationId = order.CorrelationId,
        Instrument = order.Instrument.Symbol,
        Direction = order.Direction.ToString(),
        Units = order.Units,
        RequestedPrice = order.RequestedPrice,
        StopLoss = order.StopLoss,
        TakeProfit = order.TakeProfit,
        Status = "Submitting",
        MarketTimeUtc = _quotes.TryGetValue(order.Instrument, out var q) ? q.TimestampUtc : clock.UtcNow,
        RecordedAtUtc = clock.UtcNow
    };

    private static OpenPosition ToModel(PositionEntity p) => new(
        p.Id, p.ClientOrderId, Instruments.Get(p.Instrument), Enum.Parse<Direction>(p.Direction), p.Units, p.EntryPrice, p.StopLoss,
        p.TakeProfit, p.InitialRiskAmount, DateTime.SpecifyKind(p.OpenedAtUtc, DateTimeKind.Utc), p.Strategy, p.Score,
        p.MaxFavorableExcursion, p.MaxAdverseExcursion);
}

/// <summary>Our market symbols as MT5 symbols: "EUR/USD" is "EURUSD" plus the account's suffix (e.g. ".r"), Forex only.</summary>
public static class Mt5Symbols
{
    public static string? For(Instrument instrument, string suffix) =>
        instrument.IsCurrencyPair ? instrument.BaseCurrency + instrument.QuoteCurrency + suffix : null;

    public static Instrument? ToInstrument(string mt5Symbol, string suffix)
    {
        var name = suffix.Length > 0 && mt5Symbol.EndsWith(suffix, StringComparison.Ordinal) ? mt5Symbol[..^suffix.Length] : mt5Symbol;
        return name.Length == 6 && Instruments.TryGet($"{name[..3]}/{name[3..]}", out var instrument) ? instrument : null;
    }
}
