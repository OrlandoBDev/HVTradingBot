using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// "Ready for real money?": only the bot's own Forex trades count, and the evaluation setup (Forex only, no
/// high-score extras) is applied through the normal, audited settings paths.
/// </summary>
public abstract class EvaluationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Clock _clock = new();
    private static readonly DashboardCaller Caller = new("tester", "c1");

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        db.Markets.AddRange(Market("frxEURUSD", "EUR/USD", "Forex"), Market("frxGBPUSD", "GBP/USD", "Forex"),
            Market("R_100", "R_100", "SyntheticIndex"));

        // 40 bot trades on EUR/USD over two months (+2R, -1R alternating: +0.5R a trade), plus trades that do not count.
        for (var i = 0; i < 40; i++)
        {
            db.Positions.Add(Position($"EURUSD-TF-{i}", "EUR/USD", i % 2 == 0 ? 2m : -1m, Now.AddDays(-60 + i * 1.5)));
        }

        db.Positions.AddRange(
            Position("SIG-EURUSD-1", "EUR/USD", 5m, Now.AddDays(-5)),
            Position("TEST-1", "EUR/USD", -0.1m, Now.AddDays(-5), TradingEngine.TestTradeStrategy),
            Position("R100-1", "R_100", -1m, Now.AddDays(-4)));
        await db.SaveChangesAsync();

        var catalog = new MarketCatalogStore(fixture.DbFactory, _clock);
        await catalog.LoadAndRegisterAsync(CancellationToken.None);
        await catalog.SaveSelectionAsync(["EUR/USD", "GBP/USD", "R_100"], "tester", CancellationToken.None);
        await new RiskSettingsStore(fixture.DbFactory, _clock).SaveAsync(
            RiskLimits.From(new RiskOptions()) with { MaxExtraDerivedPositions = 2 }, "tester", CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_the_bots_forex_trades_are_judged()
    {
        var evaluation = await Service().GetAsync(CancellationToken.None);

        Assert.Equal(40, evaluation.ForexTrades);
        Assert.Equal(1, evaluation.OtherTrades); // the Derived trade; signal and test trades are left out
        Assert.Equal(0.5m, evaluation.Metrics.AverageR);
        Assert.False(evaluation.Ready);
        Assert.False(Check(evaluation, "trades").Passed);
        Assert.Equal("40 of 200.", Check(evaluation, "trades").Detail);
        Assert.False(Check(evaluation, "extras").Passed);
        Assert.Contains("R_100", Check(evaluation, "forexOnly").Detail);
        Assert.True(Check(evaluation, "drawdown").Passed);
    }

    [Fact]
    public async Task The_evaluation_setup_keeps_only_forex_and_turns_extras_off()
    {
        var result = await Service().ApplySetupAsync(Caller, CancellationToken.None);

        var evaluation = (EvaluationDto)Assert.IsType<DashboardResult.OkResult>(result).Value!;
        Assert.True(Check(evaluation, "extras").Passed);
        Assert.True(Check(evaluation, "forexOnly").Passed);
        Assert.Equal(["EUR/USD", "GBP/USD"], (await new MarketCatalogStore(fixture.DbFactory, _clock).GetSelectionAsync(CancellationToken.None)).Instruments);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "EvaluationSetupApplied"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "RiskLimitsUpdated"));
    }

    private static EvaluationCheck Check(EvaluationDto evaluation, string key) => evaluation.Checks.Single(c => c.Key == key);

    private EvaluationService Service()
    {
        var state = new EfTradingStateStore(fixture.DbFactory);
        var catalog = new MarketCatalogStore(fixture.DbFactory, _clock);
        var riskSettings = new RiskSettingsStore(fixture.DbFactory, _clock);
        var riskSource = new RiskOptionsSource(new RiskOptions(), riskSettings, NullLogger<RiskOptionsSource>.Instance);
        var journal = new EfDecisionJournal(fixture.DbFactory, _clock);
        var queries = new DashboardQueries(fixture.DbFactory, state, _clock, riskSource, new TradingEngineOptions(), catalog,
            new CloseRequestStore(fixture.DbFactory, _clock));
        var actions = new DashboardActions(queries, state, journal, _clock, null!, null!, null!, new HVTradingBot.Domain.Learning.LearningOptions(), null!,
            riskSource, riskSettings, null!, null!, catalog, new TradingEngineOptions(), null!, NullLogger<DashboardActions>.Instance);
        return new EvaluationService(fixture.DbFactory, state, queries, actions, catalog, new TradingEngineOptions(), journal, _clock);
    }

    private static MarketEntity Market(string brokerSymbol, string symbol, string assetClass) => new()
    {
        BrokerSymbol = brokerSymbol, Symbol = symbol, Name = symbol, Market = "test", Submarket = "test", AssetClass = assetClass,
        BaseCurrency = symbol.Length == 7 ? symbol[..3] : "R100", QuoteCurrency = "USD", PipSize = 0.0001m, PriceDecimals = 5, Multipliers = "[]",
        IsTradable = true, IsOpen = true, UpdatedAtUtc = Now
    };

    private static PositionEntity Position(string clientOrderId, string instrument, decimal r, DateTime closed, string strategy = "Trend Following") => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        Broker = "Paper",
        ClientOrderId = clientOrderId,
        Instrument = instrument,
        Direction = "Long",
        Units = 1000,
        EntryPrice = 1.1m,
        StopLoss = 1.09m,
        TakeProfit = 1.12m,
        InitialRiskAmount = 100,
        OpenedAtUtc = closed.AddHours(-3),
        Strategy = strategy,
        Score = 80,
        IsOpen = false,
        RealizedPnl = r * 100m,
        RMultiple = r,
        ClosedAtUtc = closed
    };

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => Now;
    }
}

[Collection(PostgresCollection.Name)]
public sealed class EvaluationTestsOnPostgres(PostgresFixture fixture) : EvaluationTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class EvaluationTestsOnSqlite(SqliteFixture fixture) : EvaluationTests(fixture);
