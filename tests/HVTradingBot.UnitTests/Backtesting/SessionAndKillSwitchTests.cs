using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.UnitTests.Backtesting;

public class SessionAndKillSwitchTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Backtest_resumes_trading_on_the_day_after_a_daily_loss_breach()
    {
        // A daily loss limit so small that the first losing trade breaches it.
        var risk = new RiskOptions { MaxDailyLossPercent = 0.1m, KillSwitchOnDailyLossBreach = true };
        var data = Instruments.Defaults.ToDictionary(i => i, i => MarketSeriesGenerator.Generate(i, 11, End, 90));
        var engine = new BacktestEngine(new TradingEngineOptions { EvaluationTimeFrame = TimeFrame.H1 }, risk, new ScoringOptions(), new RegimeOptions(),
            new ExecutionCostOptions(), new LearningOptions());

        var result = await engine.RunAsync(data, CancellationToken.None);

        var firstLoss = result.Trades.Where(t => t.RealizedPnl < 0).Min(t => t.ClosedAtUtc);
        Assert.Contains(result.Trades, t => t.OpenedAtUtc.Date > firstLoss.Date); // not stopped for good by the kill switch
    }

    private static readonly Instrument Synthetic = Instruments.Register(
        new Instrument("R_TEST", "R_TEST", "USD", 0.01m, 2) { AssetClass = AssetClass.SyntheticIndex, Name = "Test index" });

    [Fact]
    public async Task Derived_markets_are_evaluated_only_while_forex_is_closed()
    {
        // Forex bars stop 3 days before the synthetic series ends (e.g. a weekend); the synthetic market trades throughout.
        var forexEnd = End.AddDays(-3);
        var bars = new Dictionary<Instrument, IReadOnlyList<Candle>>
        {
            [Instruments.EurUsd] = MarketSeriesGenerator.Generate(Instruments.EurUsd, 3, forexEnd, 30),
            [Synthetic] = Continuous(End.AddDays(-33), End)
        };

        var journal = new InMemoryJournal { KeepDecisions = true };
        var engine = NewEngine(bars.Keys, journal, derivedOnlyWhenForexClosed: true);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>>(), CancellationToken.None);
        foreach (var group in bars.SelectMany(kv => kv.Value.Select(b => new InstrumentBar(kv.Key, b))).GroupBy(b => b.Bar.OpenTimeUtc).OrderBy(g => g.Key))
        {
            var t = group.First().Bar.CloseTimeUtc;
            await engine.ProcessBarsAsync(group.ToList(), new MarketDataStatus(t, t), "t", CancellationToken.None);
        }

        // Forex is open at time t if it produced a bar within the preceding 15 minutes (weekends in the Forex data count as closed).
        var forexBarCloses = bars[Instruments.EurUsd].Select(b => b.CloseTimeUtc).ToHashSet();
        bool ForexOpenAt(DateTime t) => Enumerable.Range(0, 4).Any(k => forexBarCloses.Contains(t.AddMinutes(-5 * k)));

        var synthetic = journal.Decisions.Where(d => d.Instrument == "R_TEST").ToList();
        Assert.NotEmpty(synthetic);
        Assert.All(synthetic, d => Assert.False(ForexOpenAt(d.MarketTimeUtc), $"evaluated at {d.MarketTimeUtc:u} while Forex was open"));
        Assert.Contains(synthetic, d => d.MarketTimeUtc > forexEnd.AddMinutes(15));     // takes over after Forex stops
        Assert.Contains(journal.Decisions, d => d.Instrument == "EUR/USD");
    }

    /// <summary>A 24/7 random walk (synthetic markets have no weekends).</summary>
    private static List<Candle> Continuous(DateTime start, DateTime end)
    {
        var random = new Random(5);
        var price = 1000m;
        var bars = new List<Candle>();
        for (var t = start; t < end; t = t.AddMinutes(5))
        {
            var open = price;
            price = Math.Round(price * (1 + (decimal)((random.NextDouble() - 0.5) * 0.004)), 2);
            bars.Add(new Candle(t, TimeFrame.M5, open, Math.Max(open, price) + 0.5m, Math.Min(open, price) - 0.5m, price, 0.2m, 1));
        }

        return bars;
    }

    private static TradingEngine NewEngine(IEnumerable<Instrument> instruments, InMemoryJournal journal, bool derivedOnlyWhenForexClosed)
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var risk = new RiskManager(new RiskOptions(), costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        return new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), new InMemoryStateStore(), journal, journal, new ReplayClock(),
            learning, TradingUniverse.From(instruments, "USD", derivedOnlyWhenForexClosed), new TradingEngineOptions { EvaluationTimeFrame = TimeFrame.H1 },
            new FixedRiskOptions(new RiskOptions()), new RegimeOptions(), NullLogger<TradingEngine>.Instance);
    }
}
