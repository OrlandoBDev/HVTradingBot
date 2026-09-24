using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Domain.Decisions;

/// <summary>
/// Runs every strategy independently, scores the candidates (including the learned adjustment and 15-minute timing),
/// applies abstention rules and the score policy. Default outcome is NO_TRADE; this class never talks to a broker.
/// </summary>
public sealed class SignalEvaluator(
    IReadOnlyList<ITradingStrategy> strategies,
    ScoringOptions options,
    IStrategyPerformanceProvider? performance = null)
{
    /// <summary>Penalty while the 15-minute trend and momentum oppose the setup (execution timing, docs/TRADING_ENGINE.md).</summary>
    public const decimal AdverseTimingPenalty = 10m;

    public async Task<SignalEvaluation> EvaluateAsync(MarketContext context, CancellationToken cancellationToken)
    {
        var results = new List<StrategyResult>(strategies.Count);
        foreach (var strategy in strategies)
        {
            results.Add(await strategy.EvaluateAsync(context, cancellationToken));
        }

        var allScored = results
            .Where(r => !r.IsNoTrade)
            .Select(r => Score(context, r, results))
            .OrderByDescending(c => c.Score.Total)
            .ThenBy(c => c.Result.Strategy, StringComparer.Ordinal)
            .ToList();

        if (allScored.Count == 0)
        {
            return new SignalEvaluation(context, DecisionState.NoTrade, null, results, allScored, ["No strategy produced a setup."]);
        }

        var scored = allScored.Where(c => !c.DisabledByLearning).ToList();
        if (scored.Count == 0)
        {
            var p = allScored[0].Performance!;
            return new SignalEvaluation(context, DecisionState.NoTrade, allScored[0], results, allScored,
                [$"Learning: {p.Key} is disabled ({p.Samples} setups, shrunk expectancy {p.ShrunkR:+0.00;-0.00}R)."]);
        }

        var best = scored[0];
        var abstentions = AbstentionReasons(context, scored).ToList();
        if (abstentions.Count > 0)
        {
            return new SignalEvaluation(context, DecisionState.NoTrade, best, results, scored, abstentions);
        }

        var total = best.Score.Total;
        var (state, reason) = total switch
        {
            _ when total < options.ObserveThreshold => (DecisionState.NoTrade, $"Score {total} below {options.ObserveThreshold}."),
            _ when total < options.CandidateThreshold => (DecisionState.Observe, $"Score {total} is observe-only (< {options.CandidateThreshold})."),
            _ when total >= options.HighQualityThreshold => (DecisionState.Candidate, $"High-quality candidate, score {total}."),
            _ => (DecisionState.Candidate, $"Candidate, score {total}.")
        };

        var notes = new List<string> { reason };
        if (best.Score.Learned != 0 && best.Performance is { } perf)
        {
            notes.Add($"Learning {best.Score.Learned:+0.#;-0.#} ({perf.Samples} setups, avg {perf.AverageR:+0.00;-0.00}R).");
        }

        if (best.Score.TimingPenalty > 0)
        {
            notes.Add($"15m timing against the setup (-{best.Score.TimingPenalty:0}); re-checked every 5 minutes.");
        }

        return new SignalEvaluation(context, state, best, results, allScored, notes);
    }

    private ScoredCandidate Score(MarketContext context, StrategyResult result, IReadOnlyCollection<StrategyResult> all)
    {
        var score = TradeScorer.Score(context, result, all);
        var perf = performance?.Get(new SetupKey(result.Strategy, context.Regime, context.Instrument.AssetClass));
        score = score with
        {
            Learned = perf?.ScoreAdjustment ?? 0,
            TimingPenalty = TimingAgainst(context, result.Setup!.Direction) ? AdverseTimingPenalty : 0
        };
        return new ScoredCandidate(result, score, perf);
    }

    /// <summary>True when the latest closed 15-minute bar shows trend and momentum pointing the other way.</summary>
    public static bool TimingAgainst(MarketContext context, Direction direction)
    {
        if (context.Indicators.GetValueOrDefault(TimeFrame.M15) is not { Ema9: { } ema9, Ema20: { } ema20, Rsi: { } rsi })
        {
            return false;
        }

        return direction == Direction.Long ? ema9 < ema20 && rsi < 45 : ema9 > ema20 && rsi > 55;
    }

    private IEnumerable<string> AbstentionReasons(MarketContext context, IReadOnlyList<ScoredCandidate> scored)
    {
        if (context.IsDataStale)
        {
            yield return "Abstain: market data is stale.";
        }

        if (context.Regime is MarketRegime.Uncertain or MarketRegime.ExtremeVolatility or MarketRegime.NewsEvent)
        {
            yield return $"Abstain: regime {context.Regime}.";
        }

        if (context.AverageSpread > 0 && context.Quote.Spread > context.AverageSpread * options.AbnormalSpreadMultiple)
        {
            yield return $"Abstain: spread {context.Quote.Spread} is abnormal vs average {context.AverageSpread:0.#####}.";
        }

        if (scored.Select(c => c.Setup.Direction).Distinct().Count() > 1)
        {
            yield return "Abstain: strategies disagree on direction.";
        }
    }
}
