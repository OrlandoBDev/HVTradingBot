namespace HVTradingBot.Domain.Scoring;

/// <summary>
/// Component scores follow the table in docs/TRADING_ENGINE.md. Penalties are positive numbers subtracted from the total.
/// <see cref="Learned"/> is the bounded adjustment from observed performance (see StrategyPerformanceModel);
/// <see cref="Timing"/> is a penalty while the 15-minute trend opposes the setup.
/// <see cref="NewsSentiment"/> and <see cref="MarketTrend"/> are the bounded adjustments from recent headlines and the
/// cross-market currency trend; <see cref="ContextLearned"/> is what learning found those conditions to be worth.
/// </summary>
public sealed record ScoreBreakdown(
    decimal Trend,
    decimal Momentum,
    decimal Structure,
    decimal HigherTimeframe,
    decimal Volatility,
    decimal RegimeFit,
    decimal RiskReward,
    decimal SpreadPenalty,
    decimal ConflictPenalty,
    decimal UncertaintyPenalty,
    decimal Learned = 0,
    decimal TimingPenalty = 0,
    decimal NewsSentiment = 0,
    decimal MarketTrend = 0,
    decimal ContextLearned = 0)
{
    public const decimal MaxTrend = 20, MaxMomentum = 15, MaxStructure = 20, MaxHigherTimeframe = 15,
        MaxVolatility = 10, MaxRegimeFit = 10, MaxRiskReward = 10;

    public decimal Raw => Trend + Momentum + Structure + HigherTimeframe + Volatility + RegimeFit + RiskReward;

    public decimal Penalties => SpreadPenalty + ConflictPenalty + UncertaintyPenalty + TimingPenalty;

    /// <summary>Everything outside the indicator score: learning, news sentiment and the cross-market trend.</summary>
    public decimal Adjustments => Learned + NewsSentiment + MarketTrend + ContextLearned;

    public int Total => (int)Math.Round(Math.Clamp(Raw - Penalties + Adjustments, 0, 100), MidpointRounding.AwayFromZero);
}
