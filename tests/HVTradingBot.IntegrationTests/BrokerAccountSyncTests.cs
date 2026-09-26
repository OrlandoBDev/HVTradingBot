using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// The dashboard shows the whole Deriv account: contracts the app opened and ones opened elsewhere (Deriv's website,
/// another bot), synced by the worker. The app's own profit is reported separately.
/// </summary>
public abstract class BrokerAccountSyncTests(DatabaseFixture fixture) : IAsyncLifetime
{
    // Wednesday: the week started on Monday 5 January, the month on 1 January.
    private static readonly DateTime Now = new(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly long Monday = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long Tuesday = new DateTimeOffset(2026, 1, 6, 15, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long TodayMorning = new DateTimeOffset(2026, 1, 7, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long LastMonth = new DateTimeOffset(2025, 12, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private readonly FakeDerivSocket _socket = new();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM positions;");

        // The app's own trades: one open (contract 100), one closed on Tuesday (contract 90, +5).
        db.Positions.AddRange(
            AppPosition("100", open: true, pnl: null, closed: null),
            AppPosition("90", open: false, pnl: 5m, closed: new DateTime(2026, 1, 6, 15, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var state = new EfTradingStateStore(fixture.DbFactory);
        await state.UpdateAsync(s => s with { BrokerName = "Deriv", BrokerAccountId = FakeDerivRest.DemoAccount, BrokerIsDemo = true },
            CancellationToken.None);

        _socket.Handlers["portfolio"] = _ => new
        {
            portfolio = new
            {
                contracts = new object[]
                {
                    new { contract_id = 100, contract_type = "MULTUP", underlying = "frxEURUSD", buy_price = 50m, purchase_time = Monday, currency = "USD" },
                    new { contract_id = 200, contract_type = "MULTDOWN", symbol = "R_100", buy_price = 10m, purchase_time = Tuesday, currency = "USD" }
                }
            }
        };
        _socket.Handlers["proposal_open_contract"] = request => request["contract_id"]!.GetValue<long>() switch
        {
            100 => new { proposal_open_contract = (object)new { contract_id = 100, profit = 3.5m, current_spot = 1.1012m, entry_spot = 1.1m, is_sold = 0 } },
            200 => new
            {
                proposal_open_contract = (object)new
                {
                    contract_id = 200, profit = "-1.25", current_spot = 1234.5m, entry_spot = 1230m, is_sold = 0, multiplier = 100,
                    limit_order = new { stop_loss = new { value = "1250" } }
                }
            },
            _ => throw new DerivApiException("ContractNotFound", "Unknown contract.")
        };
        _socket.Handlers["profit_table"] = _ => new
        {
            profit_table = new
            {
                transactions = new object[]
                {
                    new { contract_id = 90, buy_price = 50m, sell_price = 55m, purchase_time = Monday, sell_time = Tuesday, shortcode = "MULTUP_FRXEURUSD_50.00_100_1767603600_4921257599_0_0.00_N1" },
                    new { contract_id = 300, buy_price = 20m, sell_price = 12m, purchase_time = Tuesday, sell_time = TodayMorning, shortcode = "MULTDOWN_R_100_20.00_100_1767711600_4921257599_0_0.00_N1" },
                    new { contract_id = 400, buy_price = 10m, sell_price = 14m, purchase_time = LastMonth, sell_time = LastMonth + 600, shortcode = "MULTUP_1HZ100V_10.00_50_1766224800_4921257599_0_0.00_N1" }
                }
            }
        };
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Open_contracts_and_history_include_trades_made_outside_the_app()
    {
        await SyncAsync();
        var queries = Queries();

        var open = await queries.GetOpenPositionsAsync(CancellationToken.None);
        var app = Assert.Single(open, p => p.Source == DashboardQueries.AppSource);
        Assert.Equal(3.5m, app.UnrealizedPnl); // Deriv's profit (with commission), not the quote estimate
        Assert.Equal("100", app.ContractId);
        var external = Assert.Single(open, p => p.Source == DashboardQueries.ExternalSource);
        Assert.Equal(("R_100", "Short", -1.25m, 10m, 1250m), (external.Instrument, external.Direction, external.UnrealizedPnl, external.Stake, external.StopLoss));

        var history = await queries.GetTradeHistoryPageAsync(1, 10, CancellationToken.None);
        Assert.Equal(3, history.Total); // the app's closed trade once, plus two external ones
        Assert.Equal(["300", "90", "400"], history.Items.Select(t => t.ContractId));
        Assert.Equal([DashboardQueries.ExternalSource, DashboardQueries.AppSource, DashboardQueries.ExternalSource], history.Items.Select(t => t.Source));
        Assert.Equal("1HZ100V", history.Items[2].Instrument);
        Assert.Equal(-8m, history.Items[0].RealizedPnl);

        var secondPage = await queries.GetTradeHistoryPageAsync(2, 2, CancellationToken.None);
        Assert.Equal(["400"], secondPage.Items.Select(t => t.ContractId));

        var status = await queries.GetStatusAsync(CancellationToken.None);
        Assert.Equal(2, status.OpenPositions);
        Assert.Equal(2.25m, status.Account.UnrealizedPnl);
    }

    [Fact]
    public async Task Overview_profit_separates_the_account_from_the_app()
    {
        await SyncAsync();

        var profit = await Queries().GetProfitSummaryAsync(CancellationToken.None);

        Assert.True(profit.AccountFromBroker);
        // Account: today -8 (contract 300), week -8 + 5 (90), month the same; December's +4 is outside all three.
        Assert.Equal((-8m, -3m, -3m, 2.25m, 2, 3, 2), (profit.Account.Today, profit.Account.Week, profit.Account.Month, profit.Account.Open,
            profit.Account.OpenTrades, profit.Account.ClosedTrades, profit.Account.Wins));
        Assert.Null(profit.Account.AllTime);
        // App: only its own trades.
        Assert.Equal((0m, 5m, 5m, 5m, 3.5m, 1, 1, 1), (profit.App.Today, profit.App.Week, profit.App.Month, profit.App.AllTime, profit.App.Open,
            profit.App.OpenTrades, profit.App.ClosedTrades, profit.App.Wins));
    }

    [Fact]
    public async Task Contract_sold_since_the_last_sync_is_recorded_with_its_result()
    {
        await SyncAsync();
        _socket.Handlers["portfolio"] = _ => new
        {
            portfolio = new { contracts = new object[] { new { contract_id = 100, contract_type = "MULTUP", underlying = "frxEURUSD", buy_price = 50m } } }
        };
        var previous = _socket.Handlers["proposal_open_contract"];
        _socket.Handlers["proposal_open_contract"] = request => request["contract_id"]!.GetValue<long>() == 200
            ? new { proposal_open_contract = (object)new { contract_id = 200, profit = 2m, is_sold = 1, sell_time = TodayMorning + 60, exit_tick = 1228m } }
            : previous(request);

        Assert.True(await Sync().SyncOpenAsync(CancellationToken.None));

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var sold = await db.BrokerContracts.SingleAsync(c => c.ContractId == "200");
        Assert.False(sold.IsOpen);
        Assert.Equal((2m, 1228m), (sold.Profit, sold.ExitSpot));
    }

    [Fact]
    public async Task Sync_only_reads_from_the_broker()
    {
        await SyncAsync();

        Assert.Equal(0, _socket.Count("buy") + _socket.Count("sell"));
    }

    private async Task SyncAsync()
    {
        var sync = Sync();
        await sync.SyncOpenAsync(CancellationToken.None);
        await sync.SyncHistoryAsync(CancellationToken.None);
    }

    private DerivAccountSync Sync()
    {
        var options = new DerivOptions();
        var settings = fixture.NewSettingsStore(new DerivEnvironmentCredentials("123", "token-abcd", null));
        var session = new DerivSession(options, new DerivRestClient(new HttpClient(new FakeDerivRest()), options), _socket, settings, settings,
            new FixedClock(), NullLogger<DerivSession>.Instance);
        return new DerivAccountSync(session, fixture.DbFactory, new FixedClock(), NullLogger<DerivAccountSync>.Instance);
    }

    private DashboardQueries Queries()
    {
        var clock = new FixedClock();
        return new DashboardQueries(fixture.DbFactory, new EfTradingStateStore(fixture.DbFactory), clock,
            new RiskOptionsSource(new RiskOptions(), new RiskSettingsStore(fixture.DbFactory, clock), NullLogger<RiskOptionsSource>.Instance),
            new TradingEngineOptions(), new MarketCatalogStore(fixture.DbFactory, clock), new CloseRequestStore(fixture.DbFactory, clock));
    }

    private static PositionEntity AppPosition(string contractId, bool open, decimal? pnl, DateTime? closed) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        Broker = DerivBroker.BrokerName,
        BrokerAccountId = FakeDerivRest.DemoAccount,
        BrokerContractId = contractId,
        ClientOrderId = $"EURUSD-{contractId}",
        Instrument = "EUR/USD",
        Direction = "Long",
        Units = 1000,
        EntryPrice = 1.1m,
        StopLoss = 1.09m,
        TakeProfit = 1.12m,
        InitialRiskAmount = 10,
        OpenedAtUtc = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc),
        Strategy = "Trend Following",
        Score = 80,
        IsOpen = open,
        RealizedPnl = pnl,
        ClosedAtUtc = closed
    };

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}

[Collection(PostgresCollection.Name)]
public sealed class BrokerAccountSyncTestsOnPostgres(PostgresFixture fixture) : BrokerAccountSyncTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class BrokerAccountSyncTestsOnSqlite(SqliteFixture fixture) : BrokerAccountSyncTests(fixture);
