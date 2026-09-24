namespace HVTradingBot.Application.Configuration;

public sealed class RiskOptions
{
    public const string SectionName = "Risk";
    public decimal MaxRiskPerTradePercent { get; init; } = 0.5m;
    public decimal MaxDailyLossPercent { get; init; } = 2m;
    public decimal MaxWeeklyLossPercent { get; init; } = 5m;
    public int MaxOpenPositions { get; init; } = 3;
    public decimal MinimumRiskReward { get; init; } = 2m;
    public int MaximumConsecutiveLosses { get; init; } = 3;
    public int MarketDataStaleAfterSeconds { get; init; } = 30;
}
