using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Domain.Scoring;

public static class TradeScorer
{
    public static ScoreBreakdown Score(MarketContext context, StrategyResult candidate, IReadOnlyCollection<StrategyResult> allResults)
    {
        var setup = candidate.Setup ?? throw new ArgumentException("Cannot score a NO_TRADE result.", nameof(candidate));
        var sign = setup.Direction.Sign();
        var p = context.Primary;

        return new ScoreBreakdown(
            Trend: TrendScore(p, sign),
            Momentum: MomentumScore(p, setup.Direction),
            Structure: StructureScore(context.PrimaryStructure, setup.Direction),
            HigherTimeframe: HigherTimeframeScore(context, sign),
            Volatility: VolatilityScore(p.AtrPercentRank),
            RegimeFit: RegimeFitScore(context.Regime, candidate, setup.Direction),
            RiskReward: Math.Min(ScoreBreakdown.MaxRiskReward, setup.RewardToRisk / 3m * ScoreBreakdown.MaxRiskReward),
            SpreadPenalty: SpreadPenalty(context),
            ConflictPenalty: allResults.Any(r => r.Setup is { } other && other.Direction != setup.Direction) ? 15m : 0m,
            UncertaintyPenalty: context.Regime == MarketRegime.Uncertain ? 10m : 0m);
    }

    private static decimal TrendScore(IndicatorSnapshot p, int sign)
    {
        decimal score = 0;
        if (p.Ema20 is { } e20 && sign * (p.Close - e20) > 0) score += 4;
        if (p.Ema50 is { } e50 && sign * (p.Close - e50) > 0) score += 4;
        if (p.Ema200 is { } e200 && sign * (p.Close - e200) > 0) score += 3;
        if (p is { Ema20: { } a, Ema50: { } b } && sign * (a - b) > 0) score += 4;
        if (p.Adx is { } adx) score += adx >= 30 ? 5 : adx >= 22 ? 3 : adx >= 18 ? 1 : 0;
        return Math.Min(score, ScoreBreakdown.MaxTrend);
    }

    private static decimal MomentumScore(IndicatorSnapshot p, Direction direction)
    {
        decimal score = 0;
        var sign = direction.Sign();
        if (p.Rsi is { } rsi)
        {
            var favorable = direction == Direction.Long ? rsi is > 50 and < 72 : rsi is < 50 and > 28;
            score += favorable ? 5 : 0;
        }

        if (p.MacdHistogram is { } h && sign * h > 0) score += 5;
        if (p.RateOfChange is { } roc && sign * roc > 0) score += 5;
        return Math.Min(score, ScoreBreakdown.MaxMomentum);
    }

    private static decimal StructureScore(MarketStructure s, Direction direction)
    {
        decimal score = 0;
        if (direction == Direction.Long)
        {
            if (s.HigherHighs) score += 5;
            if (s.HigherLows) score += 5;
            if (s.Trend == StructureTrend.Up) score += 4;
            if (s.BullishRetest) score += 6;
            else if (s.BullishBreakout) score += 3;
        }
        else
        {
            if (s.LowerHighs) score += 5;
            if (s.LowerLows) score += 5;
            if (s.Trend == StructureTrend.Down) score += 4;
            if (s.BearishRetest) score += 6;
            else if (s.BearishBreakout) score += 3;
        }

        if (s.FalseBreakoutCandidate) score -= 4;
        return Math.Clamp(score, 0, ScoreBreakdown.MaxStructure);
    }

    private static decimal HigherTimeframeScore(MarketContext context, int sign)
    {
        decimal score = 0;
        var h4 = context.Structural;
        if (h4 is { Ema20: { } a, Ema50: { } b } && sign * (a - b) > 0) score += 6;
        if (h4.Ema50 is { } e50 && sign * (h4.Close - e50) > 0) score += 4;
        if (context.Daily is { Ema20: { } d20 } daily && sign * (daily.Close - d20) > 0) score += 5;
        return Math.Min(score, ScoreBreakdown.MaxHigherTimeframe);
    }

    private static decimal VolatilityScore(decimal? atrRank) => atrRank switch
    {
        null => 3,
        >= 0.25m and <= 0.85m => 10,
        >= 0.10m and <= 0.92m => 6,
        _ => 2
    };

    private static decimal RegimeFitScore(MarketRegime regime, StrategyResult candidate, Direction direction)
    {
        if (!candidate.CompatibleRegimes.Contains(regime)) return 0;
        return regime switch
        {
            MarketRegime.TrendingBullish => direction == Direction.Long ? 10 : 2,
            MarketRegime.TrendingBearish => direction == Direction.Short ? 10 : 2,
            MarketRegime.Ranging or MarketRegime.LowVolatility => 7,
            MarketRegime.HighVolatility => 5,
            _ => 2
        };
    }

    private static decimal SpreadPenalty(MarketContext context)
    {
        if (context.Primary.Atr is not { } atr || atr == 0) return 5;
        var ratio = context.Quote.Spread / atr;
        return ratio switch
        {
            <= 0.05m => 0,
            <= 0.10m => 3,
            <= 0.20m => 7,
            _ => 15
        };
    }
}
