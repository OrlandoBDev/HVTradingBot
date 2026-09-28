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

namespace HVTradingBot.UnitTests.Trading;

/// <summary>Trading a set amount (e.g. $100) instead of the whole balance, like a small real account.</summary>
public class TradingCapitalTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_capital_follows_the_apps_results_and_never_exceeds_the_balance()
    {
        var state = new TradingSystemState().WithCapital(100m);
        var options = new RiskOptions();

        Assert.Equal(10_000m, state.SizingBalance(10_000m, null)); // no capital: the whole balance
        Assert.Equal(100m, state.SizingBalance(10_000m, 100m));
        state = state.WithClosedTrade(-2m, End, options).WithClosedSignalTrade(5m, End);
        Assert.Equal(103m, state.SizingBalance(10_000m, 100m));
        Assert.Equal(50m, state.SizingBalance(50m, 100m)); // never more than the account holds
        Assert.Equal(200m, state.SizingBalance(10_000m, 200m)); // a new amount starts fresh, before the engine resets it
        Assert.Equal(0m, state.WithClosedTrade(-500m, End, options).SizingBalance(10_000m, 100m));
    }

    [Fact]
    public async Task The_engine_sizes_trades_and_limits_on_the_trading_capital()
    {
        var options = new RiskOptions { MinUnits = 1, UnitStep = 1, MaxRiskPerTradePercent = 2m, TradingCapital = 1_000m };
        var (engine, state, status) = await Ready(options);

        var portfolio = await engine.BuildPortfolioAsync(status, CancellationToken.None);
        var outcome = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None,
            [new Quote(Instruments.EurUsd, End, 1.10000m, 1.10008m)]);

        Assert.Equal(1_000m, portfolio.Balance); // of a 10,000 account
        Assert.Equal(1_000m, (await state.GetAsync(CancellationToken.None)).CapitalBase);
        Assert.True(outcome.Filled, outcome.Message);
        var risk = decimal.Parse(outcome.Message.Split("risk ")[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(risk is > 0 and <= 20m, $"risk {risk}"); // 2% of 1,000, not of 10,000
    }

    private static async Task<(TradingEngine Engine, InMemoryStateStore State, MarketDataStatus Status)> Ready(RiskOptions options)
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal();
        var state = new InMemoryStateStore();
        var clock = new ReplayClock();
        var risk = new RiskManager(options, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, clock, learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(options), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance);
        var bars = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = bars.Take(bars.Count - 1).ToList() },
            CancellationToken.None);
        clock.UtcNow = bars[^1].CloseTimeUtc;
        return (engine, state, new MarketDataStatus(bars[^1].CloseTimeUtc, bars[^1].CloseTimeUtc));
    }
}
