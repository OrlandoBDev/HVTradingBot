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

    private static async Task<(TradingEngine Engine, InMemoryJournal Journal, InMemorySimulatedBroker Broker, InMemoryStateStore State, MarketDataStatus Status)> Ready(
        bool processFirstBar = true, decimal minRewardToRisk = 2m)
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal { KeepDecisions = true };
        var state = new InMemoryStateStore();
        var clock = new ReplayClock();
        var risk = new RiskManager(new RiskOptions { MinUnits = 1, UnitStep = 1, MinRewardToRisk = minRewardToRisk }, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, clock, learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(new RiskOptions { MinRewardToRisk = minRewardToRisk }), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance);

        var bars = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = bars.Take(bars.Count - 1).ToList() }, CancellationToken.None);
        var last = bars[^1];
        clock.UtcNow = last.CloseTimeUtc;
        var status = new MarketDataStatus(last.CloseTimeUtc, last.CloseTimeUtc);
        if (processFirstBar)
        {
            await engine.ProcessBarsAsync([new InstrumentBar(Instruments.EurUsd, last)], status, "t", CancellationToken.None);
        }

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
    public async Task Test_trade_works_right_after_startup_before_any_new_bar()
    {
        var (engine, _, broker, _, status) = await Ready(processFirstBar: false);
        var live = new Quote(Instruments.EurUsd, End, 1.10000m, 1.10008m);

        var outcome = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None, [live]);

        Assert.True(outcome.Filled, outcome.Message);
        Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));
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

    /// <summary>
    /// Stop and target are rounded to the market's price precision; the target must still give at least the minimum
    /// R:R, or the risk engine refuses the test trade ("R:R 2.00 below minimum 2.00").
    /// </summary>
    [Theory]
    [MemberData(nameof(LivePrices))]
    public async Task Rounded_stop_and_target_never_fall_below_the_minimum_reward_to_risk(decimal ask, decimal minRewardToRisk)
    {
        var (engine, journal, _, _, status) = await Ready(processFirstBar: false, minRewardToRisk: minRewardToRisk);
        var live = new Quote(Instruments.EurUsd, End, ask - 0.00008m, ask);

        var outcome = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None, [live]);

        Assert.True(outcome.Filled, outcome.Message);
        Assert.True(journal.Decisions.Single().Setup!.RewardToRisk >= minRewardToRisk);
    }

    public static TheoryData<decimal, decimal> LivePrices()
    {
        var data = new TheoryData<decimal, decimal>();
        for (var i = 0; i < 12; i++)
        {
            data.Add(1.10000m + i * 0.00007m, 2m);
        }

        data.Add(1.10013m, 2.5m); // a stricter limit set on the Settings page
        return data;
    }
}
