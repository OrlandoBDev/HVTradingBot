using System.ComponentModel.DataAnnotations;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Learning;

/// <summary>What the engine learns about: a strategy, in a market regime, on an asset class.</summary>
public sealed record SetupKey(string Strategy, MarketRegime Regime, AssetClass AssetClass)
{
    public override string ToString() => $"{Strategy}/{Regime}/{AssetClass}";
}

/// <summary>A resolved setup outcome in R (profit / initial risk), from a real or virtual trade.</summary>
public sealed record SetupOutcome(SetupKey Key, decimal RMultiple, DateTime ClosedAtUtc);

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
}

/// <summary>Current learned performance, looked up while scoring.</summary>
public interface IStrategyPerformanceProvider
{
    StrategyPerformance? Get(SetupKey key);
}
