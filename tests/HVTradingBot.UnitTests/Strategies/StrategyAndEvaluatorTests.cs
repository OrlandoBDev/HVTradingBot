using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Strategies;

public class StrategyAndEvaluatorTests
{
    private static MarketContext FlatContext(bool stale = false)
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        foreach (var bar in Bars.Flat(288 * 12, 1.1m))
        {
            series.Add(bar);
        }

        var quote = Bars.Quote(Instruments.EurUsd, 1.1m, at: series.LastBar!.CloseTimeUtc);
        return MarketContext.Build(series, quote, new RegimeOptions(), stale);
    }

    [Fact]
    public async Task Every_strategy_returns_no_trade_in_a_flat_market()
    {
        var context = FlatContext();
        foreach (var strategy in StrategyCatalog.CreateDefault())
        {
            var result = await strategy.EvaluateAsync(context, CancellationToken.None);
            Assert.True(result.IsNoTrade, $"{strategy.Name}: {result.Reason}");
        }
    }

    [Fact]
    public async Task Stale_data_forces_no_trade_for_every_strategy()
    {
        var context = FlatContext(stale: true);
        foreach (var strategy in StrategyCatalog.CreateDefault())
        {
            var result = await strategy.EvaluateAsync(context, CancellationToken.None);
            Assert.True(result.IsNoTrade);
            Assert.Contains("stale", result.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Evaluator_default_outcome_is_no_trade()
    {
        var evaluator = new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions());
        var evaluation = await evaluator.EvaluateAsync(FlatContext(), CancellationToken.None);
        Assert.Equal(DecisionState.NoTrade, evaluation.State);
        Assert.Null(evaluation.Best);
        Assert.Equal(5, evaluation.StrategyResults.Count);
    }

    [Fact]
    public async Task Evaluator_abstains_when_strategies_disagree_on_direction()
    {
        var context = FlatContext() with { Regime = MarketRegime.Ranging };
        var evaluator = new SignalEvaluator([new FixedStrategy("A", Direction.Long), new FixedStrategy("B", Direction.Short)], new ScoringOptions());

        var evaluation = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionState.NoTrade, evaluation.State);
        Assert.Contains(evaluation.Reasons, r => r.Contains("disagree"));
        Assert.Equal(2, evaluation.ScoredCandidates.Count);
    }

    [Fact]
    public async Task Evaluator_abstains_in_uncertain_regime_even_with_a_setup()
    {
        var context = FlatContext() with { Regime = MarketRegime.Uncertain };
        var evaluator = new SignalEvaluator([new FixedStrategy("A", Direction.Long)], new ScoringOptions { ObserveThreshold = 0, CandidateThreshold = 0 });

        var evaluation = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionState.NoTrade, evaluation.State);
        Assert.Contains(evaluation.Reasons, r => r.Contains("Uncertain"));
    }

    [Fact]
    public async Task Score_thresholds_are_configurable()
    {
        var context = FlatContext() with { Regime = MarketRegime.Ranging };
        var strategies = new ITradingStrategy[] { new FixedStrategy("A", Direction.Long) };

        var strict = await new SignalEvaluator(strategies, new ScoringOptions { ObserveThreshold = 101, CandidateThreshold = 101 })
            .EvaluateAsync(context, CancellationToken.None);
        var lenient = await new SignalEvaluator(strategies, new ScoringOptions { ObserveThreshold = 0, CandidateThreshold = 0 })
            .EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionState.NoTrade, strict.State);
        Assert.Equal(DecisionState.Candidate, lenient.State);
    }

    [Fact]
    public void Score_components_never_exceed_their_maximums()
    {
        var context = FlatContext() with { Regime = MarketRegime.Ranging };
        var result = StrategyResult.Trade("A", new TradeSetup(Direction.Long, 1.1m, 1.09m, 1.2m), "x", [MarketRegime.Ranging]);

        var score = TradeScorer.Score(context, result, [result]);

        Assert.InRange(score.Total, 0, 100);
        Assert.True(score.RiskReward <= ScoreBreakdown.MaxRiskReward);
        Assert.True(score.Trend <= ScoreBreakdown.MaxTrend);
    }

    [Fact]
    public void Invalid_setups_are_rejected()
    {
        Assert.False(new TradeSetup(Direction.Long, 1.1m, 1.2m, 1.3m).IsValid);
        Assert.False(new TradeSetup(Direction.Short, 1.1m, 1.09m, 1.0m).IsValid);
        Assert.Throws<ArgumentException>(() =>
            StrategyResult.Trade("x", new TradeSetup(Direction.Long, 1.1m, 1.1m, 1.2m), "zero risk", []));
    }

    private sealed class FixedStrategy(string name, Direction direction) : ITradingStrategy
    {
        public string Name => name;

        public IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } = Enum.GetValues<MarketRegime>();

        public Task<StrategyResult> EvaluateAsync(MarketContext context, CancellationToken cancellationToken)
        {
            var entry = context.Quote.Mid;
            var sign = direction.Sign();
            var setup = new TradeSetup(direction, entry, entry - sign * 0.002m, entry + sign * 0.005m);
            return Task.FromResult(StrategyResult.Trade(name, setup, "fixed", CompatibleRegimes));
        }
    }
}
