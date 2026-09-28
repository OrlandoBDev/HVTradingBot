using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Brokers.Mt5;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// Forex on a MetaTrader 5 demo account through MetaApi (a fake MetaApi here): demo-only, lot sizing, idempotent
/// client ids, unknown outcomes reconciled by client id, and closes read from the deal history.
/// </summary>
public abstract class Mt5BrokerTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CurrencyConverter Converter = new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.1m });
    private readonly FakeMetaApi _api = new();
    private readonly Clock _clock = new();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM mt5_settings;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Mt5SettingsStore Store() => new(fixture.DbFactory, fixture.DataProtectionProvider, _clock);

    private async Task<Mt5Broker> BrokerAsync(bool connect = true)
    {
        await Store().SaveAsync(true, "metaapi-token-0123456789abcdef", "acc-1", "london", "", 0.007m, "tester", CancellationToken.None);
        var broker = new Mt5Broker(new MetaApiGateway(new MetaApiClient(new HttpClient(_api)), Store()), Store(), fixture.DbFactory, new TradingEngineOptions { Instruments = ["EUR/USD"] },
            _clock, NullLogger<Mt5Broker>.Instance);
        if (connect)
        {
            await broker.GetAccountAsync(CancellationToken.None);
            await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0), Converter, CancellationToken.None);
        }

        return broker;
    }

    private static Candle Bar(DateTime open, decimal close = 1.1m) => new(open, TimeFrame.M5, close, close + 0.0005m, close - 0.0005m, close, 0.0001m, 10);

    // 12,345 units = 0.12345 lots -> 0.12 lots (rounded down to the 0.01 step).
    private static TradeOrder Order(string id = "EURUSD-T-L-1", decimal units = 12_345m, Instrument? instrument = null) =>
        new(id, instrument ?? Instruments.EurUsd, Direction.Long, units, 1.10005m, 1.0980m, 1.1041m, 30m, "Test", 80, null, "corr");

    [Fact]
    public async Task Connects_to_the_demo_account_and_reports_it()
    {
        var broker = await BrokerAsync(connect: false);

        var account = await broker.GetAccountAsync(CancellationToken.None);

        Assert.Equal(10_000m, account.Balance);
        Assert.Equal(new BrokerDescriptor("MT5", "MT5-5550001", true), broker.Descriptor);
        Assert.All(_api.Requests, r =>
        {
            Assert.Equal("metaapi-token-0123456789abcdef", r.Headers.GetValues("auth-token").Single());
            Assert.Equal("mt-client-api-v1.london.agiliumtrade.ai", r.RequestUri!.Host);
        });
        var status = (await Store().GetViewAsync(CancellationToken.None)).Status!;
        Assert.Equal(("Connected", "5550001", true), (status.State, status.Login, status.IsDemo));
    }

    [Fact]
    public async Task Real_money_accounts_are_refused()
    {
        _api.TradeMode = "ACCOUNT_TRADE_MODE_REAL";
        var broker = await BrokerAsync(connect: false);

        var error = await Assert.ThrowsAsync<BrokerUnavailableException>(() => broker.GetAccountAsync(CancellationToken.None));

        Assert.Contains("real-money", error.Message);
        Assert.Equal("Failed", (await Store().GetViewAsync(CancellationToken.None)).Status!.State);
    }

    [Fact]
    public async Task A_rejected_token_leaves_the_broker_unavailable()
    {
        _api.Unauthorized = true;
        var broker = await BrokerAsync(connect: false);

        var error = await Assert.ThrowsAsync<BrokerUnavailableException>(() => broker.GetAccountAsync(CancellationToken.None));

        Assert.Contains("rejected the token", error.Message);
    }

    [Fact]
    public async Task Places_forex_in_whole_lot_steps_with_broker_side_stop_and_target_once_per_signal()
    {
        var broker = await BrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);
        var again = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        Assert.Equal(OrderStatus.Duplicate, again.Status);
        var trade = Assert.Single(_api.Trades);
        Assert.Equal("ORDER_TYPE_BUY", trade["actionType"]!.GetValue<string>());
        Assert.Equal("EURUSD", trade["symbol"]!.GetValue<string>());
        Assert.Equal(0.12m, trade["volume"]!.GetValue<decimal>());
        Assert.Equal(1.098m, trade["stopLoss"]!.GetValue<decimal>());
        Assert.Equal(1.1041m, trade["takeProfit"]!.GetValue<decimal>());
        Assert.Equal(Mt5Broker.ClientId("EURUSD-T-L-1"), trade["clientId"]!.GetValue<string>());
        Assert.True(trade["clientId"]!.GetValue<string>().Length + trade["comment"]!.GetValue<string>().Length <= 26);

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var position = await db.Positions.SingleAsync();
        Assert.Equal(("MT5", "MT5-5550001", "7001", 12_000m, 1.10012m), (position.Broker, position.BrokerAccountId, position.BrokerContractId, position.Units, position.EntryPrice));
        Assert.Equal(29.16m, position.InitialRiskAmount); // 30 scaled to the 12,000 units actually traded
        var open = Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));
        Assert.Equal(position.Id, open.Id);
    }

    [Fact]
    public async Task Refuses_what_mt5_cannot_trade_here_without_sending_anything()
    {
        var broker = await BrokerAsync();
        var vol = Instruments.Register(new Instrument("R_MT5TEST", "R_MT5TEST", "USD", 0.01m, 2) { AssetClass = AssetClass.SyntheticIndex, Name = "Test index" });

        var tooSmall = await broker.PlaceOrderAsync(Order("EURUSD-T-L-2", units: 900m), CancellationToken.None);
        var notForex = await broker.PlaceOrderAsync(Order("R-T-L-1", instrument: vol), CancellationToken.None);
        _api.RefuseTrades = "TRADE_RETCODE_NO_MONEY";
        var refused = await broker.PlaceOrderAsync(Order("EURUSD-T-L-3"), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, tooSmall.Status);
        Assert.Contains("below the broker minimum", tooSmall.RejectReason);
        Assert.Contains("not Forex", notForex.RejectReason);
        Assert.Contains("TRADE_RETCODE_NO_MONEY", refused.RejectReason);
        Assert.Single(_api.Trades); // only the last one reached MT5
    }

    [Fact]
    public async Task A_lost_answer_is_reconciled_by_client_id()
    {
        var broker = await BrokerAsync();
        _api.TimeOutTrades = true;

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        // The fake executed it before the answer was lost: the position carrying the client id is found at once.
        Assert.Equal(OrderStatus.Filled, result.Status);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Equal("Filled", (await db.Orders.SingleAsync()).Status);
        Assert.Equal("7001", (await db.Positions.SingleAsync()).BrokerContractId);
    }

    [Fact]
    public async Task A_closed_position_is_read_from_the_deal_history_with_all_costs()
    {
        var broker = await BrokerAsync();
        await broker.PlaceOrderAsync(Order(), CancellationToken.None);
        _api.ClosePosition("7001", price: 1.098m, profit: -24.14m, commission: -0.42m, swap: -0.1m, reason: "DEAL_REASON_SL");

        var closed = Assert.Single(await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0.AddMinutes(5), 1.098m), Converter, CancellationToken.None));

        Assert.Equal(ExitReason.StopLoss, closed.Reason);
        Assert.Equal(-25.08m, closed.RealizedPnl); // -24.14 - 0.42 (opening) - 0.42 (closing) - 0.10
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var position = await db.Positions.SingleAsync();
        Assert.False(position.IsOpen);
        Assert.Equal(0.84m, position.Commission);
    }

    [Fact]
    public async Task Closing_sends_a_close_by_position_id()
    {
        var broker = await BrokerAsync();
        var opened = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        var result = await broker.ClosePositionAsync(opened.PositionId!.Value.ToString(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        Assert.Contains(_api.Trades, t => t["actionType"]!.GetValue<string>() == "POSITION_CLOSE_ID" && t["positionId"]!.GetValue<string>() == "7001");
    }

    [Fact]
    public async Task Settings_keep_the_token_secret_validate_input_and_size_with_mt5_commission()
    {
        var journal = new EfDecisionJournal(fixture.DbFactory, _clock);
        var actions = new Mt5SettingsActions(Store(), new EfTradingStateStore(fixture.DbFactory), journal);
        var caller = new DashboardCaller("tester", "c1");

        var missing = await actions.SaveAsync(new Mt5SettingsRequest(true, null, null, "london", "", 0.007m), caller, CancellationToken.None);
        var bad = await actions.SaveAsync(new Mt5SettingsRequest(false, "short", "acc 1", "mars", "a b", 5m), caller, CancellationToken.None);
        var saved = await actions.SaveAsync(new Mt5SettingsRequest(true, "metaapi-token-0123456789abcdef", "acc-1", "london", ".r", 0.007m), caller,
            CancellationToken.None);

        Assert.Equal(["token", "accountId"], Assert.IsType<DashboardResult.InvalidResult>(missing).Errors.Keys.Order().Reverse());
        Assert.Equal(5, Assert.IsType<DashboardResult.InvalidResult>(bad).Errors.Count);
        var dto = (Mt5SettingsDto)Assert.IsType<DashboardResult.OkResult>(saved).Value!;
        Assert.Equal((true, true, "cdef", ".r"), (dto.Enabled, dto.TokenConfigured, dto.TokenHint, dto.SymbolSuffix));
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            Assert.DoesNotContain("metaapi-token", (await db.Mt5Settings.SingleAsync()).TokenProtected);
        }

        var risk = new RiskOptionsSource(new RiskOptions(), new RiskSettingsStore(fixture.DbFactory, _clock), NullLogger<RiskOptionsSource>.Instance, Store());
        var view = await risk.RefreshAsync(CancellationToken.None);
        Assert.Equal(0.007m, risk.Current.AssumedCommissionPercent); // sizing uses MT5's cost while MT5 is on
        Assert.Equal(0.05m, view.Effective.AssumedCommissionPercent); // the Risk page keeps showing the user's own value

        await actions.ClearAsync(caller, CancellationToken.None);
        await risk.RefreshAsync(CancellationToken.None);
        Assert.Equal(0.05m, risk.Current.AssumedCommissionPercent);
        Assert.False(await Store().IsActiveAsync(CancellationToken.None));
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => T0;
    }

    /// <summary>MetaApi's REST API for one MT5 demo account, in memory.</summary>
    private sealed class FakeMetaApi : HttpMessageHandler
    {
        private readonly List<JsonObject> _positions = [];
        private readonly Dictionary<string, List<object>> _deals = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<JsonObject> Trades { get; } = [];
        public string TradeMode { get; set; } = "ACCOUNT_TRADE_MODE_DEMO";
        public bool Unauthorized { get; set; }
        public bool TimeOutTrades { get; set; }
        public string? RefuseTrades { get; set; }

        public void ClosePosition(string id, decimal price, decimal profit, decimal commission, decimal swap, string reason)
        {
            _positions.RemoveAll(p => p["id"]!.GetValue<string>() == id);
            _deals[id] =
            [
                new { id = "d1", entryType = "DEAL_ENTRY_IN", price = 1.10012m, profit = 0m, commission, swap = 0m, time = "2026-01-05T10:05:00Z", reason = "DEAL_REASON_EXPERT" },
                new { id = "d2", entryType = "DEAL_ENTRY_OUT", price, profit, commission, swap, time = "2026-01-05T11:00:00.000Z", reason }
            ];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Unauthorized)
            {
                return Json(HttpStatusCode.Unauthorized, new { error = "UnauthorizedError", message = "Invalid auth-token header" });
            }

            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/account-information"))
            {
                return Json(HttpStatusCode.OK, new
                {
                    platform = "mt5", broker = "Deriv.com Limited", currency = "USD", server = "Deriv-Demo", balance = 10_000m, equity = 10_000m,
                    login = 5550001, name = "Test", type = TradeMode
                });
            }

            if (path.EndsWith("/positions"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonArray(_positions.Select(p => (JsonNode)p.DeepClone()).ToArray()).ToJsonString(), Encoding.UTF8, "application/json") };
            }

            if (path.Contains("/symbols/EURUSD/specification"))
            {
                return Json(HttpStatusCode.OK, new { symbol = "EURUSD", contractSize = 100_000, minVolume = 0.01, maxVolume = 50, volumeStep = 0.01, digits = 5 });
            }

            if (path.Contains("/history-deals/position/"))
            {
                return Json(HttpStatusCode.OK, _deals.GetValueOrDefault(path[(path.LastIndexOf('/') + 1)..]) ?? []);
            }

            if (path.EndsWith("/trade"))
            {
                var trade = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                Trades.Add(trade);
                if (RefuseTrades is { } code)
                {
                    return Json(HttpStatusCode.OK, new { numericCode = 10019, stringCode = code, message = "No money" });
                }

                if (trade["actionType"]!.GetValue<string>() == "POSITION_CLOSE_ID")
                {
                    return Json(HttpStatusCode.OK, new { numericCode = 10009, stringCode = "TRADE_RETCODE_DONE", message = "Request completed" });
                }

                _positions.Add(new JsonObject
                {
                    ["id"] = "7001", ["type"] = trade["actionType"]!.GetValue<string>() == "ORDER_TYPE_BUY" ? "POSITION_TYPE_BUY" : "POSITION_TYPE_SELL",
                    ["symbol"] = trade["symbol"]!.GetValue<string>(), ["volume"] = trade["volume"]!.GetValue<decimal>(), ["openPrice"] = 1.10012m,
                    ["stopLoss"] = trade["stopLoss"]!.GetValue<decimal>(), ["takeProfit"] = trade["takeProfit"]!.GetValue<decimal>(), ["profit"] = 0,
                    ["commission"] = -0.42m, ["swap"] = 0, ["time"] = "2026-01-05T10:05:00.000Z", ["clientId"] = trade["clientId"]!.GetValue<string>()
                });
                if (TimeOutTrades)
                {
                    throw new TaskCanceledException("The request timed out."); // executed, but the answer never arrived
                }

                return Json(HttpStatusCode.OK, new { numericCode = 10009, stringCode = "TRADE_RETCODE_DONE", message = "Request completed", orderId = "7001", positionId = "7001" });
            }

            return Json(HttpStatusCode.NotFound, new { error = "NotFoundError", message = $"No route {path}" });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}

[Collection(PostgresCollection.Name)]
public sealed class Mt5BrokerTestsOnPostgres(PostgresFixture fixture) : Mt5BrokerTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class Mt5BrokerTestsOnSqlite(SqliteFixture fixture) : Mt5BrokerTests(fixture);
