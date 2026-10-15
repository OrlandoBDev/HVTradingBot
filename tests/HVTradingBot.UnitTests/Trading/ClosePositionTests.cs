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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_trade_going_nowhere_is_closed_early_only_when_early_exit_is_on(bool earlyExit)
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal { KeepDecisions = true };
        var state = new InMemoryStateStore();
        var options = new RiskOptions { MinUnits = 1, UnitStep = 1, EarlyExitEnabled = earlyExit };
        var risk = new RiskManager(options, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, new ReplayClock(), learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(options), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance);
        var history = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = history }, CancellationToken.None);
        var last = history[^1];
        var opened = await engine.PlaceTestTradeAsync(Instruments.EurUsd, new MarketDataStatus(last.CloseTimeUtc, last.CloseTimeUtc), "tester", "c1",
            CancellationToken.None);
        Assert.True(opened.Filled, opened.Message);
        var position = Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));

        // A day and a bit drifting a quarter of the risk against the trade, never near the stop or target.
        var oneR = Math.Abs(position.EntryPrice - position.StopLoss);
        var price = position.EntryPrice - position.Direction.Sign() * oneR / 4;
        var range = oneR / 20;
        var closedEarly = false;
        for (var i = 0; i < 26 * 12 && !closedEarly; i++)
        {
            var bar = new Candle(last.CloseTimeUtc.AddMinutes(5 * i), TimeFrame.M5, price, price + range, price - range, price, last.Spread, 10);
            await engine.ProcessBarsAsync([new InstrumentBar(Instruments.EurUsd, bar)], new MarketDataStatus(bar.CloseTimeUtc, bar.CloseTimeUtc), $"b{i}",
                CancellationToken.None);
            closedEarly = (await broker.GetPositionsAsync(CancellationToken.None)).All(p => p.Id != position.Id);
        }

        Assert.Equal(earlyExit, closedEarly);
        Assert.Equal(earlyExit, journal.Audit.Any(a => a.Action == "EarlyExit" && a.Details.Contains(position.ClientOrderId)));
        if (earlyExit)
        {
            Assert.Contains(journal.Audit, a => a.Action == "PositionClosed" && a.Details.Contains($"{position.ClientOrderId} EarlyExit"));
            Assert.True((await state.GetAsync(CancellationToken.None)).DailyRealizedPnl < 0); // a smaller loss, still counted
        }
    }

    [Fact]
    public async Task Closing_an_unknown_position_is_refused()
    {
        var broker = new InMemorySimulatedBroker("USD", 10_000m, new ExecutionCostOptions());
        var result = await broker.ClosePositionAsync(Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.Equal(OrderStatus.Rejected, result.Status);
    }
}
