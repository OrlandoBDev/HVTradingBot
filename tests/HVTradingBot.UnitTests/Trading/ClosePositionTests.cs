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

namespace HVTradingBot.UnitTests.Trading;

public class ClosePositionTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Closing_early_records_a_manual_exit_and_updates_the_daily_pnl_immediately()
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal { KeepDecisions = true };
        var state = new InMemoryStateStore();
        var risk = new RiskManager(new RiskOptions { MinUnits = 1, UnitStep = 1 }, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, new ReplayClock(), learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(new RiskOptions()), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance);
        var bars = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = bars }, CancellationToken.None);
        var last = bars[^1];
        var status = new MarketDataStatus(last.CloseTimeUtc, last.CloseTimeUtc);

        var opened = await engine.PlaceTestTradeAsync(Instruments.EurUsd, status, "tester", "c1", CancellationToken.None);
        Assert.True(opened.Filled, opened.Message);

        var (result, closed) = await engine.ClosePositionAsync(opened.PositionId!.Value, "c2", CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        Assert.NotNull(closed);
        Assert.Equal(ExitReason.Manual, closed.Reason);
        Assert.Empty(await broker.GetPositionsAsync(CancellationToken.None));
        Assert.Equal(closed.RealizedPnl, (await state.GetAsync(CancellationToken.None)).DailyRealizedPnl);
        Assert.True(closed.RealizedPnl < 0); // closed at once: pays spread, slippage and commission
    }

    [Fact]
    public async Task Closing_an_unknown_position_is_refused()
    {
        var broker = new InMemorySimulatedBroker("USD", 10_000m, new ExecutionCostOptions());
        var result = await broker.ClosePositionAsync(Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.Equal(OrderStatus.Rejected, result.Status);
    }
}
