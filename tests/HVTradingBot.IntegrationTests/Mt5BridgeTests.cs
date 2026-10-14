using System.Globalization;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Bridge;
using HVTradingBot.Infrastructure.Brokers.Mt5;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// Forex on MetaTrader 5 through the free Expert Advisor bridge (a simulated Expert Advisor here, calling the same sync the
/// API endpoint calls): orders delivered once, an offline or silent Expert Advisor never leaves a guessed outcome, and
/// closes read from the deals it reports.
/// </summary>
public abstract class Mt5BridgeTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CurrencyConverter Converter = new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.1m });
    private readonly Clock _clock = new() { UtcNow = T0 };
    private readonly CancellationTokenSource _stop = new();
    private FakeExpertAdvisor _ea = null!;
    private Task? _loop;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM mt5_settings;");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM mt5_bridge_commands;");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM mt5_bridge_state;");
        _ea = new FakeExpertAdvisor(Bridge());
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }
    }

    private Mt5SettingsStore Store() => new(fixture.DbFactory, fixture.DataProtectionProvider, _clock);

    private Mt5BridgeStore Bridge() => new(fixture.DbFactory, _clock);

    private void StartEa() => _loop = _ea.RunAsync(_stop.Token);

    private async Task<Mt5Broker> BrokerAsync(bool connect = true)
    {
        await Store().SaveAsync(true, null, null, "london", "", 0.007m, "tester", CancellationToken.None, Mt5SettingsStore.BridgeConnection);
        var gateway = new BridgeGateway(Bridge(), _clock, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(20));
        var broker = new Mt5Broker(gateway, Store(), fixture.DbFactory, new TradingEngineOptions { Instruments = ["EUR/USD"] }, _clock,
            NullLogger<Mt5Broker>.Instance);
        await _ea.SyncOnceAsync(CancellationToken.None); // the Expert Advisor has reported at least once
        if (connect)
        {
            await broker.GetAccountAsync(CancellationToken.None);
            await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0), Converter, CancellationToken.None);
        }

        return broker;
    }

    private static Candle Bar(DateTime open, decimal close = 1.1m) => new(open, TimeFrame.M5, close, close + 0.0005m, close - 0.0005m, close, 0.0001m, 10);

    private static TradeOrder Order(string id = "EURUSD-T-L-1", decimal units = 12_345m) =>
        new(id, Instruments.EurUsd, Direction.Long, units, 1.10005m, 1.0980m, 1.1041m, 30m, "Test", 80, null, "corr");

    [Fact]
    public async Task Connects_through_the_expert_advisor_and_refuses_real_accounts()
    {
        var broker = await BrokerAsync(connect: false);

        var account = await broker.GetAccountAsync(CancellationToken.None);

        Assert.Equal(10_000m, account.Balance);
        Assert.Equal(new BrokerDescriptor("MT5", "MT5-5550001", true), broker.Descriptor);

        _ea.TradeMode = "ACCOUNT_TRADE_MODE_REAL";
        await _ea.SyncOnceAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<BrokerUnavailableException>(() => broker.GetAccountAsync(CancellationToken.None));
        Assert.Contains("real-money", error.Message);
    }

    [Fact]
    public async Task An_order_is_delivered_once_and_filled_with_the_expert_advisors_lot_size()
    {
        var broker = await BrokerAsync();
        StartEa();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);
        var again = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        Assert.Equal(OrderStatus.Duplicate, again.Status);
        var open = Assert.Single(_ea.Received);
        Assert.Equal($"OPEN|{open.Split('|')[1]}|EURUSD|BUY|12345|1.0980|1.1041|{Mt5Broker.ClientId("EURUSD-T-L-1")}", open);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var position = await db.Positions.SingleAsync();
        Assert.Equal(("MT5", "9001", 12_000m, 1.10012m, 0.42m), (position.Broker, position.BrokerContractId, position.Units, position.EntryPrice, position.Commission));
        Assert.Equal("Done", (await db.Mt5BridgeCommands.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_offline_expert_advisor_refuses_new_orders_without_queueing_them()
    {
        var broker = await BrokerAsync();
        _clock.UtcNow = T0.AddMinutes(2); // no report for 2 minutes

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains("Expert Advisor last reported 2 min ago", result.RejectReason);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Empty(await db.Mt5BridgeCommands.ToListAsync());
    }

    [Fact]
    public async Task An_order_never_picked_up_is_withdrawn_and_rejected()
    {
        var broker = await BrokerAsync(); // the Expert Advisor reported once, then went quiet (within the offline window)

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains("did not pick up the order", result.RejectReason);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Equal("Expired", (await db.Mt5BridgeCommands.SingleAsync()).Status);
        Assert.Equal("OK\n", await Bridge().SyncAsync(_ea.Report(), CancellationToken.None)); // never delivered afterwards
    }

    [Fact]
    public async Task An_order_taken_without_a_result_is_reconciled_by_client_id()
    {
        var broker = await BrokerAsync();
        _ea.DropResults = true; // executes, but the result never comes back
        StartEa();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Equal("Filled", (await db.Orders.SingleAsync()).Status);
        Assert.Equal("9001", (await db.Positions.SingleAsync()).BrokerContractId);
    }

    [Fact]
    public async Task A_refusal_from_mt5_is_a_rejection()
    {
        var broker = await BrokerAsync();
        _ea.Refuse = (10019, "Not enough money");
        StartEa();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains("RETCODE_10019 Not enough money", result.RejectReason);
    }

    [Fact]
    public async Task Closes_are_sent_by_ticket_and_read_from_the_reported_deals()
    {
        var broker = await BrokerAsync();
        StartEa();
        var opened = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        var close = await broker.ClosePositionAsync(opened.PositionId!.Value.ToString(), CancellationToken.None);
        await _ea.SyncOnceAsync(CancellationToken.None);
        var closed = Assert.Single(await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0.AddMinutes(5), 1.098m), Converter, CancellationToken.None));

        Assert.Equal(OrderStatus.Filled, close.Status);
        Assert.StartsWith("CLOSE|", _ea.Received[1]);
        Assert.EndsWith("|9001", _ea.Received[1]);
        Assert.Equal(ExitReason.StopLoss, closed.Reason); // no SL/TP reason on the deal: classified by price (1.098 is the stop)
        Assert.Equal(-25.08m, closed.RealizedPnl); // -24.14 - 0.42 (opening) - 0.42 (closing) - 0.10
    }

    [Fact]
    public async Task Bridge_settings_need_no_metaapi_login_and_create_a_key()
    {
        var actions = new Mt5SettingsActions(Store(), new EfTradingStateStore(fixture.DbFactory), new EfDecisionJournal(fixture.DbFactory, _clock), Bridge());
        var caller = new DashboardCaller("tester", "c1");

        var saved = await actions.SaveAsync(new Mt5SettingsRequest(true, null, null, "london", "", 0.007m, "Bridge"), caller, CancellationToken.None);
        var dto = (Mt5SettingsDto)Assert.IsType<DashboardResult.OkResult>(saved).Value!;
        var renewed = (Mt5SettingsDto)Assert.IsType<DashboardResult.OkResult>(
            await actions.SaveAsync(new Mt5SettingsRequest(true, null, null, "london", "", 0.007m, null, NewBridgeKey: true), caller, CancellationToken.None)).Value!;
        var bad = await actions.SaveAsync(new Mt5SettingsRequest(true, null, null, "london", "", 0.007m, "Telnet"), caller, CancellationToken.None);

        Assert.Equal("Bridge", dto.Connection);
        Assert.Matches("^[0-9a-f]{32}$", dto.BridgeKey);
        Assert.NotEqual(dto.BridgeKey, renewed.BridgeKey);
        Assert.True(await Store().IsActiveAsync(CancellationToken.None));
        Assert.True(await Store().IsValidBridgeKeyAsync(renewed.BridgeKey!, CancellationToken.None));
        Assert.False(await Store().IsValidBridgeKeyAsync(dto.BridgeKey!, CancellationToken.None));
        Assert.False(await Store().IsValidBridgeKeyAsync("", CancellationToken.None));
        Assert.Contains("connection", Assert.IsType<DashboardResult.InvalidResult>(bad).Errors.Keys);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            Assert.DoesNotContain(renewed.BridgeKey!, (await db.Mt5Settings.SingleAsync()).BridgeKeyProtected);
        }

        await actions.SaveAsync(new Mt5SettingsRequest(false, null, null, "london", "", 0.007m), caller, CancellationToken.None);
        Assert.False(await Store().IsValidBridgeKeyAsync(renewed.BridgeKey!, CancellationToken.None)); // switched off: the Expert Advisor is locked out
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; set; }
    }

    /// <summary>The HVTradingBot Expert Advisor and the MT5 demo account it runs on, in memory.</summary>
    private sealed class FakeExpertAdvisor(Mt5BridgeStore bridge)
    {
        private readonly List<BridgePosition> _positions = [];
        private readonly List<BridgeDeal> _deals = [];
        private readonly List<BridgeResult> _results = [];
        private readonly SemaphoreSlim _gate = new(1, 1);

        public List<string> Received { get; } = [];
        public string TradeMode { get; set; } = "ACCOUNT_TRADE_MODE_DEMO";
        public bool DropResults { get; set; }
        public (int Code, string Message)? Refuse { get; set; }

        public BridgeSync Report() => new("1.00", new BridgeAccount(5550001, "Deriv-Demo", "Deriv.com Limited", "USD", 10_000m, 10_000m, TradeMode),
            [.. _positions], [.. _deals], [.. _results]);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(cancellationToken);
                    await Task.Delay(20, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public async Task SyncOnceAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var answer = await bridge.SyncAsync(Report(), cancellationToken);
                _results.Clear();
                foreach (var line in answer.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                {
                    Received.Add(line);
                    Execute(line.Split('|'));
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Execute(string[] f)
        {
            var id = long.Parse(f[1], CultureInfo.InvariantCulture);
            if (Refuse is { } refuse)
            {
                _results.Add(new BridgeResult(id, false, refuse.Code, refuse.Message, null, null, null, null, null));
                return;
            }

            var epoch = new DateTimeOffset(T0).ToUnixTimeSeconds();
            if (f[0] == "OPEN")
            {
                // 12,345 units = 0.12345 lots -> 0.12 lots (rounded down to the 0.01 step).
                var lots = Math.Floor(decimal.Parse(f[4], CultureInfo.InvariantCulture) / 100_000m / 0.01m) * 0.01m;
                _positions.Add(new BridgePosition(9001, f[2], f[3], lots, 1.10012m, decimal.Parse(f[5], CultureInfo.InvariantCulture),
                    decimal.Parse(f[6], CultureInfo.InvariantCulture), 0, 0, epoch, f[7], 770077, 100_000m));
                _deals.Add(new BridgeDeal(8001, 9001, "IN", 1.10012m, 0, -0.42m, 0, epoch, "EXPERT"));
                if (!DropResults)
                {
                    _results.Add(new BridgeResult(id, true, 10009, "done", 9001, 1.10012m, lots, lots * 100_000m, -0.42m));
                }
            }
            else
            {
                var ticket = long.Parse(f[2], CultureInfo.InvariantCulture);
                _positions.RemoveAll(p => p.Ticket == ticket);
                _deals.Add(new BridgeDeal(8002, ticket, "OUT", 1.098m, -24.14m, -0.42m, -0.1m, epoch + 3600, "EXPERT"));
                _results.Add(new BridgeResult(id, true, 10009, "done", ticket, 1.098m, null, null, -0.42m));
            }
        }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class Mt5BridgeTestsOnPostgres(PostgresFixture fixture) : Mt5BridgeTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class Mt5BridgeTestsOnSqlite(SqliteFixture fixture) : Mt5BridgeTests(fixture);
