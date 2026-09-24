using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
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

public class TestTradeTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(TradingEngine Engine, InMemoryJournal Journal, InMemorySimulatedBroker Broker, InMemoryStateStore State, MarketDataStatus Status)> Ready()
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal { KeepDecisions = true };
        var state = new InMemoryStateStore();
        var clock = new ReplayClock();
        var risk = new RiskManager(new RiskOptions { MinUnits = 1, UnitStep = 1 }, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, clock, learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(new RiskOptions()), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance);

        var bars = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = bars.Take(bars.Count - 1).ToList() }, CancellationToken.None);
        var last = bars[^1];
        clock.UtcNow = last.CloseTimeUtc;
        var status = new MarketDataStatus(last.CloseTimeUtc, last.CloseTimeUtc);
        await engine.ProcessBarsAsync([new InstrumentBar(Instruments.EurUsd, last)], status, "t", CancellationToken.None); // gives the broker a quote
        return (engine, journal, broker, state, status);
    }

    [Fact]
    public async Task Test_trade_goes_through_risk_execution_and_the_journal()
    {
        var (engine, journal, broker, _, status) = await Ready();

        var outcome = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None);

        Assert.True(outcome.Filled, outcome.Message);
        var position = Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));
        Assert.Equal(TradingEngine.TestTradeStrategy, position.Strategy);
        var decision = journal.Decisions.Single(d => d.Strategy == TradingEngine.TestTradeStrategy);
        Assert.Equal(DecisionState.Executed, decision.State);
        Assert.True(decision.Risk!.IsApproved);
        Assert.True(decision.Setup!.RewardToRisk >= 2m);
    }

    [Fact]
    public async Task Test_trade_is_refused_while_the_kill_switch_is_on()
    {
        var (engine, journal, broker, state, status) = await Ready();
        await state.UpdateAsync(s => s.WithKillSwitch(true, "manual", End), CancellationToken.None);

        var outcome = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None);

        Assert.False(outcome.Filled);
        Assert.Contains("KillSwitch", outcome.Message);
        Assert.Empty(await broker.GetPositionsAsync(CancellationToken.None));
    }
}
