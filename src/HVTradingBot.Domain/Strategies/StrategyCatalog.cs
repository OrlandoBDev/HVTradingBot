namespace HVTradingBot.Domain.Strategies;

public static class StrategyCatalog
{
    public static IReadOnlyList<ITradingStrategy> CreateDefault() =>
    [
        new TrendFollowingStrategy(),
        new BreakoutRetestStrategy(),
        new MomentumStrategy(),
        new MeanReversionStrategy(),
        new VolatilityExpansionStrategy()
    ];
}
