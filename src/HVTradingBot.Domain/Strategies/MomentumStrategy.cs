using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>Directional acceleration: MACD histogram expanding, positive rate of change and trend strength.</summary>
public sealed class MomentumStrategy : StrategyBase
{
    public override string Name => "Momentum";

    public override IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } =
        [MarketRegime.TrendingBullish, MarketRegime.TrendingBearish, MarketRegime.HighVolatility];

    protected override StrategyResult Evaluate(MarketContext context, decimal atr)
    {
        var p = context.Primary;
        if (p is not { Rsi: { } rsi, Adx: { } adx, RateOfChange: { } roc } || p.MacdHistogramRecent.Count < 3)
        {
            return NoTrade("Indicators not warmed up.");
        }

        if (adx < 20)
        {
            return NoTrade($"ADX {adx:F1} too weak for momentum.");
        }

        var h = p.MacdHistogramRecent.TakeLast(3).ToArray();
        Direction? direction = null;
        if (h[0] > 0 && h[1] > h[0] && h[2] > h[1] && roc > 0 && rsi is >= 55 and <= 75)
        {
            direction = Direction.Long;
        }
        else if (h[0] < 0 && h[1] < h[0] && h[2] < h[1] && roc < 0 && rsi is >= 25 and <= 45)
        {
            direction = Direction.Short;
        }

        if (direction is null)
        {
            return NoTrade("No accelerating momentum.");
        }

        if (RegimeDirection(context.Regime) is { } trend && trend != direction)
        {
            return NoTrade("Momentum conflicts with the higher-timeframe trend.");
        }

        return Trade(context, direction.Value, atr * 1.5m, atr * 3.2m,
            $"{direction} momentum; ADX {adx:F1}, RSI {rsi:F1}, ROC {roc:F3}%.");
    }
}
