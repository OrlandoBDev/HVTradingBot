using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>Targets transitions from contraction (Bollinger squeeze) to expansion (wide bar closing outside the band).</summary>
public sealed class VolatilityExpansionStrategy : StrategyBase
{
    public override string Name => "VolatilityExpansion";

    public override IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } =
    [
        MarketRegime.LowVolatility, MarketRegime.Ranging, MarketRegime.TrendingBullish,
        MarketRegime.TrendingBearish, MarketRegime.HighVolatility
    ];

    protected override StrategyResult Evaluate(MarketContext context, decimal atr)
    {
        var p = context.Primary;
        if (p is not { BollingerUpper: { } upper, BollingerLower: { } lower, BollingerMiddle: { } middle }
            || p.BollingerWidthRecent.Count < 6)
        {
            return NoTrade("Indicators not warmed up.");
        }

        var widths = p.BollingerWidthRecent;
        var priorMin = widths.Take(widths.Count - 1).Min();
        var squeezed = widths.Take(widths.Count - 1).TakeLast(5).Any(w => w <= priorMin * 1.05m)
            && p.BollingerWidthPercentRank is not null;
        if (!squeezed)
        {
            return NoTrade("No recent volatility contraction.");
        }

        var last = context.PrimaryCandles[^1];
        if (last.Range < atr * 1.3m)
        {
            return NoTrade("Expansion bar is not wide enough.");
        }

        Direction? direction = last.Close > upper ? Direction.Long : last.Close < lower ? Direction.Short : null;
        if (direction is null)
        {
            return NoTrade("No close outside the bands.");
        }

        if (RegimeDirection(context.Regime) is { } trend && trend != direction)
        {
            return NoTrade("Expansion against the higher-timeframe trend.");
        }

        var entry = direction == Direction.Long ? context.Quote.Ask : context.Quote.Bid;
        var stopDistance = Math.Max(Math.Abs(entry - middle), atr);
        return Trade(context, direction.Value, stopDistance, stopDistance * 2.2m,
            $"{direction} expansion out of a Bollinger squeeze.");
    }
}
