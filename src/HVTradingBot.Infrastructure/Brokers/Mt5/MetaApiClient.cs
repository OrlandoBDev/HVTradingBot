using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Infrastructure.Brokers.Mt5;

public sealed record Mt5AccountInfo(string Login, string Server, string Broker, string Currency, decimal Balance, decimal Equity, string? TradeMode);

public sealed record Mt5Position(string Id, string Symbol, bool IsBuy, decimal Volume, decimal OpenPrice, decimal? StopLoss, decimal? TakeProfit,
    decimal Profit, decimal Commission, decimal Swap, DateTime OpenedAtUtc, string? ClientId, decimal ContractSize = 100_000m);

public sealed record Mt5Deal(string Id, string? EntryType, decimal Price, decimal Profit, decimal Commission, decimal Swap, DateTime TimeUtc,
    string? Reason);

public sealed record Mt5SymbolSpec(string Symbol, decimal ContractSize, decimal MinVolume, decimal MaxVolume, decimal VolumeStep, int Digits);

/// <summary>A trade request's answer. <see cref="Done"/>: the broker executed it (TRADE_RETCODE_DONE).</summary>
public sealed record Mt5TradeResult(string Code, string Message, string? OrderId, string? PositionId)
{
    public bool Done => Code is "TRADE_RETCODE_DONE" or "TRADE_RETCODE_DONE_PARTIAL" or "TRADE_RETCODE_PLACED";
}

/// <summary>MetaApi refused a request with a clear answer (bad token, unknown account, invalid request): nothing happened.</summary>
public sealed class MetaApiException(HttpStatusCode status, string error, string message) : Exception($"MetaApi {error}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public string Error { get; } = error;

    public bool IsAuthentication => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public bool IsNotFound => Status == HttpStatusCode.NotFound;
}

/// <summary>
/// MetaApi's REST API for a MetaTrader account (metaapi.cloud): account, positions, symbol specifications, trades and
/// deal history. MetaApi runs the MT5 terminal in its cloud; the account is added and deployed in the MetaApi web app,
/// which gives the account id and API token entered on the Settings page. Network failures and timeouts surface as
/// <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/> (outcome unknown for trades).
/// </summary>
public sealed class MetaApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Uri BaseAddress(string region) => new($"https://mt-client-api-v1.{region}.agiliumtrade.ai/");

    public async Task<Mt5AccountInfo> GetAccountAsync(Mt5Credentials c, CancellationToken ct)
    {
        var a = await GetAsync(c, "account-information", ct);
        return new Mt5AccountInfo(Text(a, "login") ?? "", Text(a, "server") ?? "", Text(a, "broker") ?? "", Text(a, "currency") ?? "",
            Num(a, "balance") ?? 0, Num(a, "equity") ?? 0, Text(a, "type"));
    }

    public async Task<IReadOnlyList<Mt5Position>> GetPositionsAsync(Mt5Credentials c, CancellationToken ct) =>
        (await GetAsync(c, "positions", ct)).EnumerateArray().Select(Position).ToList();

    public async Task<Mt5SymbolSpec> GetSpecificationAsync(Mt5Credentials c, string symbol, CancellationToken ct)
    {
        var s = await GetAsync(c, $"symbols/{Uri.EscapeDataString(symbol)}/specification", ct);
        return new Mt5SymbolSpec(symbol, Num(s, "contractSize") ?? 100_000m, Num(s, "minVolume") ?? 0.01m, Num(s, "maxVolume") ?? 100m,
            Num(s, "volumeStep") ?? 0.01m, (int)(Num(s, "digits") ?? 5));
    }

    /// <summary>Deals of one position (opening and closing); empty until MT5 has recorded them.</summary>
    public async Task<IReadOnlyList<Mt5Deal>> GetDealsAsync(Mt5Credentials c, string positionId, CancellationToken ct) =>
        (await GetAsync(c, $"history-deals/position/{Uri.EscapeDataString(positionId)}", ct)).EnumerateArray()
        .Select(d => new Mt5Deal(Text(d, "id") ?? "", Text(d, "entryType"), Num(d, "price") ?? 0, Num(d, "profit") ?? 0, Num(d, "commission") ?? 0,
            Num(d, "swap") ?? 0, Time(d, "time") ?? DateTime.UtcNow, Text(d, "reason")))
        .ToList();

    /// <summary>A market order with broker-side stop loss and take profit.</summary>
    public Task<Mt5TradeResult> OpenAsync(Mt5Credentials c, string symbol, bool buy, decimal volume, decimal stopLoss, decimal takeProfit,
        string clientId, CancellationToken ct) =>
        TradeAsync(c, new JsonObject
        {
            ["actionType"] = buy ? "ORDER_TYPE_BUY" : "ORDER_TYPE_SELL",
            ["symbol"] = symbol,
            ["volume"] = volume,
            ["stopLoss"] = stopLoss,
            ["takeProfit"] = takeProfit,
            // MetaApi: comment and clientId together at most 26 characters.
            ["clientId"] = clientId,
            ["comment"] = "HVTB"
        }, ct);

    public Task<Mt5TradeResult> CloseAsync(Mt5Credentials c, string positionId, CancellationToken ct) =>
        TradeAsync(c, new JsonObject { ["actionType"] = "POSITION_CLOSE_ID", ["positionId"] = positionId }, ct);

    private async Task<Mt5TradeResult> TradeAsync(Mt5Credentials c, JsonObject request, CancellationToken ct)
    {
        using var message = Request(c, HttpMethod.Post, "trade");
        message.Content = JsonContent.Create(request, options: Json);
        using var response = await http.SendAsync(message, ct);
        var body = await ReadAsync(response, ct);
        return new Mt5TradeResult(Text(body, "stringCode") ?? Text(body, "numericCode") ?? "", Text(body, "message") ?? "",
            Text(body, "orderId"), Text(body, "positionId"));
    }

    private async Task<JsonElement> GetAsync(Mt5Credentials c, string path, CancellationToken ct)
    {
        using var message = Request(c, HttpMethod.Get, path);
        using var response = await http.SendAsync(message, ct);
        return await ReadAsync(response, ct);
    }

    private static HttpRequestMessage Request(Mt5Credentials c, HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(BaseAddress(c.Region), $"users/current/accounts/{Uri.EscapeDataString(c.AccountId)}/{path}"));
        message.Headers.Add("auth-token", c.Token);
        return message;
    }

    /// <summary>The JSON body; an HTTP error with MetaApi's error body becomes a <see cref="MetaApiException"/>.</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            // Server-side failure: for a trade, whether it executed is unknown.
            throw new HttpRequestException($"MetaApi returned {(int)response.StatusCode}: {Truncate(text)}", null, response.StatusCode);
        }

        string error = response.StatusCode.ToString(), detail = Truncate(text);
        try
        {
            var body = JsonDocument.Parse(text).RootElement;
            error = Text(body, "error") ?? error;
            detail = Text(body, "message") ?? detail;
        }
        catch (JsonException)
        {
            // Not JSON: keep the raw text.
        }

        throw new MetaApiException(response.StatusCode, error, detail);
    }

    private static Mt5Position Position(JsonElement p) => new(
        Text(p, "id") ?? "", Text(p, "symbol") ?? "", Text(p, "type") == "POSITION_TYPE_BUY", Num(p, "volume") ?? 0, Num(p, "openPrice") ?? 0,
        Num(p, "stopLoss"), Num(p, "takeProfit"), Num(p, "profit") ?? 0, Num(p, "commission") ?? 0, Num(p, "swap") ?? 0,
        Time(p, "time") ?? DateTime.UtcNow, Text(p, "clientId"));

    private static string Truncate(string text) => text.Length > 300 ? text[..300] : text;

    internal static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            }
            : null;

    internal static decimal? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetDecimal(),
                JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => null
            }
            : null;

    private static DateTime? Time(JsonElement e, string name) =>
        Text(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime
            : null;
}
