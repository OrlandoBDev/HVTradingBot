namespace HVTradingBot.Domain.Analysis;

public enum MarketRegime
{
    TrendingBullish,
    TrendingBearish,
    Ranging,
    LowVolatility,
    HighVolatility,
    ExtremeVolatility,
    NewsEvent,
    Uncertain
}

public sealed class RegimeOptions
{
    public decimal TrendAdxThreshold { get; set; } = 23m;
    public decimal RangeAdxThreshold { get; set; } = 20m;
    public decimal HighVolatilityPercentile { get; set; } = 0.90m;
    public decimal ExtremeVolatilityPercentile { get; set; } = 0.98m;
    public decimal LowVolatilityPercentile { get; set; } = 0.10m;
}

/// <summary>
/// Classifies the regime from the 4H trend state and 1H volatility.
/// Order of precedence: extreme volatility, trend, high volatility, range/low volatility, otherwise uncertain.
/// </summary>
public static class RegimeClassifier
{
    public static MarketRegime Classify(IndicatorSnapshot structural, IndicatorSnapshot primary, RegimeOptions options)
    {
        var volatilityRank = primary.AtrPercentRank;
        if (volatilityRank >= options.ExtremeVolatilityPercentile)
        {
            return MarketRegime.ExtremeVolatility;
        }

        if (structural is { Adx: { } adx, Ema20: { } ema20, Ema50: { } ema50 })
        {
            if (adx >= options.TrendAdxThreshold && ema20 > ema50 && structural.Close > ema50)
            {
                return MarketRegime.TrendingBullish;
            }

            if (adx >= options.TrendAdxThreshold && ema20 < ema50 && structural.Close < ema50)
            {
                return MarketRegime.TrendingBearish;
            }

            if (volatilityRank >= options.HighVolatilityPercentile)
            {
                return MarketRegime.HighVolatility;
            }

            if (adx < options.RangeAdxThreshold)
            {
                return volatilityRank is { } rank && rank <= options.LowVolatilityPercentile
                    ? MarketRegime.LowVolatility
                    : MarketRegime.Ranging;
            }
        }

        return MarketRegime.Uncertain;
    }
}
