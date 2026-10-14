using System.ComponentModel.DataAnnotations;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.News;

namespace HVTradingBot.Domain.Learning;

/// <summary>What the engine learns about: a strategy, in a market regime, on an asset class.</summary>
public sealed record SetupKey(string Strategy, MarketRegime Regime, AssetClass AssetClass)
{
    public override string ToString() => $"{Strategy}/{Regime}/{AssetClass}";
}

/// <summary>A resolved setup outcome in R (profit / initial risk), from a real or virtual trade.</summary>
public sealed record SetupOutcome(SetupKey Key, decimal RMultiple, DateTime ClosedAtUtc)
{
    /// <summary>The news around the setup when it was found (null when no news data applied, e.g. Derived markets).</summary>
    public NewsCondition? News { get; init; }

    /// <summary>How the setup related to the cross-market currency trend (null when not a currency pair).</summary>
    public TrendAlignment? Trend { get; init; }
}

/// <summary>A market condition the engine also learns about per strategy: <c>News</c> or <c>Trend</c>, and its value.</summary>
public sealed record ContextKey(string Strategy, string Factor, string Value)
{
    public const string NewsFactor = "News";
    public const string TrendFactor = "Trend";

    public static ContextKey For(string strategy, NewsCondition condition) => new(strategy, NewsFactor, condition.ToString());

    public static ContextKey For(string strategy, TrendAlignment alignment) => new(strategy, TrendFactor, alignment.ToString());

    public override string ToString() => $"{Strategy}/{Factor}:{Value}";
}

/// <summary>
/// How a strategy did under one news or trend condition compared with the same strategy under all conditions of that
/// factor. <see cref="ScoreAdjustment"/> is the bounded score change for new setups found under this condition.
/// </summary>
public sealed record ContextPerformance(
    ContextKey Key,
    int Samples,
    int Wins,
    decimal AverageR,
    decimal BaselineR,
    decimal ShrunkExcessR,
    decimal ScoreAdjustment)
{
    public decimal WinRate => Samples == 0 ? 0 : Math.Round((decimal)Wins / Samples * 100m, 1);
}

public sealed record StrategyPerformance(
    SetupKey Key,
    int Samples,
    int Wins,
    decimal AverageR,
    decimal ShrunkR,
    decimal ScoreAdjustment,
    bool Disabled)
{
    public decimal WinRate => Samples == 0 ? 0 : Math.Round((decimal)Wins / Samples * 100m, 1);
}

public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    public bool Enabled { get; set; } = true;

    /// <summary>Pseudo-samples at 0R that every estimate is shrunk towards; larger = slower, more sceptical learning.</summary>
    [Range(1, 500)] public int PriorStrength { get; set; } = 20;

    /// <summary>Score points per 1R of shrunk expectancy.</summary>
    [Range(0, 100)] public decimal PointsPerR { get; set; } = 20m;

    [Range(0, 30)] public decimal MaxBoost { get; set; } = 8m;

    [Range(0, 50)] public decimal MaxPenalty { get; set; } = 15m;

    /// <summary>A combination is switched off once it has this many samples and a shrunk expectancy below <see cref="DisableBelowR"/>.</summary>
    [Range(5, 1000)] public int DisableAfterSamples { get; set; } = 30;

    [Range(-5, 0)] public decimal DisableBelowR { get; set; } = -0.15m;

    /// <summary>Only outcomes from this window count, so the model follows changing markets.</summary>
    [Range(7, 730)] public int LookbackDays { get; set; } = 90;

    /// <summary>Virtual trades that hit neither stop nor target within this time are closed at the market.</summary>
    [Range(1, 720)] public int ExpireAfterHours { get; set; } = 72;

    /// <summary>Largest boost from learned news and trend conditions (added to the per-strategy adjustment above).</summary>
    [Range(0, 20)] public decimal ContextMaxBoost { get; set; } = 3m;

    /// <summary>Largest penalty from learned news and trend conditions.</summary>
    [Range(0, 30)] public decimal ContextMaxPenalty { get; set; } = 6m;
}

/// <summary>
/// Deterministic, auditable adaptation. Expectancy per <see cref="SetupKey"/> is shrunk towards zero
/// (mean = sum R / (n + prior)), so a handful of lucky trades cannot move scores much. The result can nudge the trade
/// score within [-MaxPenalty, +MaxBoost] or disable a combination; it never changes position size or risk limits.
/// </summary>
public static class StrategyPerformanceModel
{
    public static IReadOnlyDictionary<SetupKey, StrategyPerformance> Compute(
        IEnumerable<SetupOutcome> outcomes, DateTime nowUtc, LearningOptions options)
    {
        var since = nowUtc.AddDays(-options.LookbackDays);
        return outcomes
            .Where(o => o.ClosedAtUtc >= since)
            .GroupBy(o => o.Key)
            .ToDictionary(g => g.Key, g => Evaluate(g.Key, g.ToList(), options));
    }

    public static StrategyPerformance Evaluate(SetupKey key, IReadOnlyList<SetupOutcome> outcomes, LearningOptions options)
    {
        var n = outcomes.Count;
        var sum = outcomes.Sum(o => o.RMultiple);
        var average = n == 0 ? 0 : sum / n;
        var shrunk = sum / (n + options.PriorStrength);
        var adjustment = Math.Clamp(shrunk * options.PointsPerR, -options.MaxPenalty, options.MaxBoost);
        var disabled = n >= options.DisableAfterSamples && shrunk < options.DisableBelowR;
        return new StrategyPerformance(key, n, outcomes.Count(o => o.RMultiple > 0), Math.Round(average, 3), Math.Round(shrunk, 3),
            Math.Round(adjustment, 1), disabled);
    }

    /// <summary>
    /// Learns which news and trend conditions help or hurt each strategy. Each outcome is measured against the
    /// strategy's average across every condition of the same factor, and the excess is shrunk towards zero
    /// (sum of excess R / (n + prior)). A strategy that is poor everywhere is therefore not penalised twice (that is the
    /// per-strategy model's job); only the difference the news or trend makes moves the score, within
    /// [-ContextMaxPenalty, +ContextMaxBoost].
    /// </summary>
    public static IReadOnlyDictionary<ContextKey, ContextPerformance> ComputeContext(
        IEnumerable<SetupOutcome> outcomes, DateTime nowUtc, LearningOptions options)
    {
        var since = nowUtc.AddDays(-options.LookbackDays);
        var tagged = outcomes
            .Where(o => o.ClosedAtUtc >= since)
            .SelectMany(o => Tags(o).Select(key => (Key: key, o.RMultiple)))
            .ToList();

        var baselines = tagged
            .GroupBy(t => (t.Key.Strategy, t.Key.Factor))
            .ToDictionary(g => g.Key, g => g.Average(t => t.RMultiple));

        return tagged
            .GroupBy(t => t.Key)
            .ToDictionary(g => g.Key, g =>
            {
                var n = g.Count();
                var baseline = baselines[(g.Key.Strategy, g.Key.Factor)];
                var excess = g.Sum(t => t.RMultiple - baseline) / (n + options.PriorStrength);
                var adjustment = Math.Clamp(excess * options.PointsPerR, -options.ContextMaxPenalty, options.ContextMaxBoost);
                return new ContextPerformance(g.Key, n, g.Count(t => t.RMultiple > 0), Math.Round(g.Average(t => t.RMultiple), 3),
                    Math.Round(baseline, 3), Math.Round(excess, 3), Math.Round(adjustment, 1));
            });
    }

    /// <summary>Sum of the learned adjustments for the setup's conditions, clamped to the context bounds.</summary>
    public static decimal ContextAdjustment(IReadOnlyDictionary<ContextKey, ContextPerformance> model, string strategy,
        NewsCondition? news, TrendAlignment? trend, LearningOptions options)
    {
        decimal total = 0;
        if (news is { } n && model.TryGetValue(ContextKey.For(strategy, n), out var byNews))
        {
            total += byNews.ScoreAdjustment;
        }

        if (trend is { } t && model.TryGetValue(ContextKey.For(strategy, t), out var byTrend))
        {
            total += byTrend.ScoreAdjustment;
        }

        return Math.Clamp(total, -options.ContextMaxPenalty, options.ContextMaxBoost);
    }

    private static IEnumerable<ContextKey> Tags(SetupOutcome outcome)
    {
        if (outcome.News is { } news)
        {
            yield return ContextKey.For(outcome.Key.Strategy, news);
        }

        if (outcome.Trend is { } trend)
        {
            yield return ContextKey.For(outcome.Key.Strategy, trend);
        }
    }
}

/// <summary>Current learned performance, looked up while scoring.</summary>
public interface IStrategyPerformanceProvider
{
    StrategyPerformance? Get(SetupKey key);

    /// <summary>
    /// Learned score change for a setup of <paramref name="strategy"/> under these news and trend conditions, within
    /// [-ContextMaxPenalty, +ContextMaxBoost]; 0 until anything has been learned.
    /// </summary>
    decimal GetContextAdjustment(string strategy, NewsCondition? news, TrendAlignment? trend) => 0;
}
