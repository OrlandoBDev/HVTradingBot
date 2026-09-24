using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>
/// Trades pullbacks in an established trend: 4H regime trending, 1H EMAs aligned, price pulled back to the
/// EMA20 area and resumed above/below EMA9, with RSI not stretched.
/// </summary>
public sealed class TrendFollowingStrategy : StrategyBase
{
    public override string Name => "TrendFollowing";

    public override IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } =
        [MarketRegime.TrendingBullish, MarketRegime.TrendingBearish];

    protected override StrategyResult Evaluate(MarketContext context, decimal atr)
    {
        var p = context.Primary;
        if (p is not { Ema9: { } ema9, Ema20: { } ema20, Ema50: { } ema50, Rsi: { } rsi })
        {
            return NoTrade("Indicators not warmed up.");
        }

        var direction = RegimeDirection(context.Regime)!.Value;
        var sign = direction.Sign();
        var candles = context.PrimaryCandles;
        if (candles.Count < 3)
        {
            return NoTrade("Not enough bars.");
        }

        var aligned = sign * (ema20 - ema50) > 0 && sign * (p.Close - ema50) > 0;
        if (!aligned)
        {
            return NoTrade("1H EMAs not aligned with the higher-timeframe trend.");
        }

        var pullbackBars = candles.TakeLast(4).ToArray();
        var touchedEma20 = direction == Direction.Long
            ? pullbackBars.Any(c => c.Low <= ema20 + atr * 0.5m)
            : pullbackBars.Any(c => c.High >= ema20 - atr * 0.5m);
        if (!touchedEma20)
        {
            return NoTrade("No pullback to the EMA20 zone.");
        }

        var resumed = sign * (p.Close - ema9) > 0 && sign * (candles[^1].Close - candles[^1].Open) > 0;
        if (!resumed)
        {
            return NoTrade("Trend has not resumed after the pullback.");
        }

        var rsiOk = direction == Direction.Long ? rsi is >= 45 and <= 70 : rsi is >= 30 and <= 55;
        if (!rsiOk)
        {
            return NoTrade($"RSI {rsi:F1} outside the trend-continuation zone.");
        }

        return Trade(context, direction, atr * 1.5m, atr * 3.2m,
            $"{direction} pullback to EMA20 in {context.Regime}; RSI {rsi:F1}.");
    }
}
