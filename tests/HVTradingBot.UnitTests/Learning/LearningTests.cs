using HVTradingBot.Application.Learning;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.UnitTests.TestData;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.UnitTests.Learning;

public class LearningTests
{
    private static readonly SetupKey Key = new("TrendFollowing", MarketRegime.TrendingBullish, AssetClass.Forex);
    private static readonly LearningOptions Options = new();

    private static IEnumerable<SetupOutcome> Outcomes(int n, decimal r) =>
        Enumerable.Range(0, n).Select(i => new SetupOutcome(Key, r, Bars.Start.AddHours(i)));

    [Fact]
    public void Few_lucky_trades_barely_move_the_score()
    {
        var p = StrategyPerformanceModel.Evaluate(Key, Outcomes(3, 2m).ToList(), Options);
        // shrunk = 6 / (3 + 20) = 0.26R -> 5.2 points
        Assert.Equal(0.261m, p.ShrunkR);
        Assert.Equal(5.2m, p.ScoreAdjustment);
        Assert.False(p.Disabled);
    }

    [Fact]
    public void Adjustments_are_bounded()
    {
        Assert.Equal(Options.MaxBoost, StrategyPerformanceModel.Evaluate(Key, Outcomes(200, 3m).ToList(), Options).ScoreAdjustment);
        Assert.Equal(-Options.MaxPenalty, StrategyPerformanceModel.Evaluate(Key, Outcomes(200, -1m).ToList(), Options).ScoreAdjustment);
    }

    [Fact]
    public void Persistent_losers_are_disabled_only_after_enough_samples()
    {
        Assert.False(StrategyPerformanceModel.Evaluate(Key, Outcomes(29, -1m).ToList(), Options).Disabled);
        Assert.True(StrategyPerformanceModel.Evaluate(Key, Outcomes(30, -1m).ToList(), Options).Disabled);
    }

    [Fact]
    public void Old_outcomes_fall_out_of_the_lookback_window()
    {
        var old = Outcomes(50, -1m).Select(o => o with { ClosedAtUtc = Bars.Start.AddDays(-200) });
        Assert.Empty(StrategyPerformanceModel.Compute(old, Bars.Start, Options));
    }

    private static VirtualSetup Setup(Direction direction = Direction.Long) =>
        new(Guid.NewGuid(), $"EURUSD-TF-{direction}-1", null, Key, "EUR/USD", direction, 80, 1.1000m,
            direction == Direction.Long ? 1.0980m : 1.1020m, direction == Direction.Long ? 1.1040m : 1.0960m, Bars.Start, DecisionState.Observe);

    private static (LearningService Service, InMemoryVirtualTradeStore Store) Service()
    {
        var store = new InMemoryVirtualTradeStore();
        return (new LearningService(store, Options, new ExecutionCostOptions { SlippagePips = 0 }, NullLogger<LearningService>.Instance), store);
    }

    [Fact]
    public async Task Virtual_trade_resolves_as_a_win_at_the_target()
    {
        var (service, store) = Service();
        await store.AddAsync([Setup()], CancellationToken.None);

        await service.OnBarAsync(Instruments.EurUsd, new Candle(Bars.Start.AddMinutes(5), TimeFrame.M5, 1.1010m, 1.1050m, 1.1005m, 1.1045m, 0m, 1), CancellationToken.None);
        await service.RefreshAsync(Bars.Start.AddHours(1), CancellationToken.None);

        var perf = service.Get(Key)!;
        Assert.Equal(1, perf.Samples);
        Assert.Equal(2m, perf.AverageR);
        Assert.Empty(await store.GetOpenAsync("EUR/USD", CancellationToken.None));
    }

    [Fact]
    public async Task Virtual_trade_resolves_as_a_loss_at_the_stop_and_duplicates_are_ignored()
    {
        var (service, store) = Service();
        var setup = Setup(Direction.Short);
        await store.AddAsync([setup, setup with { Id = Guid.NewGuid() }], CancellationToken.None);

        await service.OnBarAsync(Instruments.EurUsd, new Candle(Bars.Start.AddMinutes(5), TimeFrame.M5, 1.1000m, 1.1030m, 1.0995m, 1.1025m, 0m, 1), CancellationToken.None);
        await service.RefreshAsync(Bars.Start.AddHours(1), CancellationToken.None);

        Assert.Equal(-1m, service.Get(Key)!.AverageR);
        Assert.Equal(1, service.Get(Key)!.Samples);
    }

    [Fact]
    public async Task Virtual_trade_expires_at_market()
    {
        var (service, store) = Service();
        await store.AddAsync([Setup()], CancellationToken.None);

        var late = Bars.Start.AddHours(Options.ExpireAfterHours);
        await service.OnBarAsync(Instruments.EurUsd, new Candle(late, TimeFrame.M5, 1.1010m, 1.1012m, 1.1008m, 1.1010m, 0m, 1), CancellationToken.None);
        await service.RefreshAsync(late, CancellationToken.None);

        Assert.Equal(0.5m, service.Get(Key)!.AverageR); // +10 pips on a 20-pip risk
    }

    private sealed class FixedPerformance(StrategyPerformance performance) : IStrategyPerformanceProvider
    {
        public StrategyPerformance? Get(SetupKey key) => key.Strategy == performance.Key.Strategy ? performance : null;
    }

    private sealed class LongSetup : ITradingStrategy
    {
        public string Name => "TrendFollowing";

        public IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } = Enum.GetValues<MarketRegime>();

        public Task<StrategyResult> EvaluateAsync(MarketContext context, CancellationToken cancellationToken) =>
            Task.FromResult(StrategyResult.Trade(Name,
                new TradeSetup(Direction.Long, context.Quote.Mid, context.Quote.Mid - 0.002m, context.Quote.Mid + 0.005m), "fixed", CompatibleRegimes));
    }

    private static MarketContext RangingContext()
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        foreach (var bar in Bars.Flat(288 * 12, 1.1m))
        {
            series.Add(bar);
        }

        return MarketContext.Build(series, Bars.Quote(Instruments.EurUsd, 1.1m, at: series.LastBar!.CloseTimeUtc), new RegimeOptions(), false)
            with { Regime = MarketRegime.Ranging };
    }

    [Fact]
    public async Task Evaluator_applies_the_learned_adjustment()
    {
        var context = RangingContext();
        var key = new SetupKey("TrendFollowing", MarketRegime.Ranging, AssetClass.Forex);
        var plain = await new SignalEvaluator([new LongSetup()], new ScoringOptions()).EvaluateAsync(context, CancellationToken.None);
        var boosted = await new SignalEvaluator([new LongSetup()], new ScoringOptions(),
            new FixedPerformance(new StrategyPerformance(key, 40, 25, 0.6m, 0.4m, 8m, false))).EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(8m, boosted.Best!.Score.Learned);
        Assert.Equal(Math.Min(100, plain.Best!.Score.Total + 8), boosted.Best.Score.Total);
        Assert.Contains(boosted.Reasons, r => r.StartsWith("Learning +8"));
    }

    [Fact]
    public async Task Evaluator_skips_combinations_disabled_by_learning()
    {
        var context = RangingContext();
        var key = new SetupKey("TrendFollowing", MarketRegime.Ranging, AssetClass.Forex);
        var evaluation = await new SignalEvaluator([new LongSetup()], new ScoringOptions { ObserveThreshold = 0, CandidateThreshold = 0 },
            new FixedPerformance(new StrategyPerformance(key, 40, 5, -0.5m, -0.33m, -15m, true))).EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionState.NoTrade, evaluation.State);
        Assert.Contains(evaluation.Reasons, r => r.Contains("disabled"));
    }
}
