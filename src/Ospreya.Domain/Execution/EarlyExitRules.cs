using Ospreya.Domain.Analysis;
using Ospreya.Domain.Common;

namespace Ospreya.Domain.Execution;

/// <summary>
/// Whether an open trade should be closed before its stop because the reason for taking it is gone. Checked on each
/// closed 1H bar (noise inside the hour never closes a trade), and only for trades at a loss: a trade in profit is left
/// to its stop and target. Any one of these closes it:
/// <list type="bullet">
/// <item>the trend turned against it (a bearish regime for a buy, a bullish one for a sell);</item>
/// <item>it is at least <see cref="MomentumLossR"/> toward the stop and 1H momentum turned against it (EMA9 crossed EMA20
/// the wrong way and the MACD histogram points against it);</item>
/// <item>it is still losing after <see cref="StaleAfter"/> and never got <see cref="StaleMinProgressR"/> into profit.</item>
/// </list>
/// </summary>
public static class EarlyExitRules
{
    public const decimal MomentumLossR = 0.5m;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);
    public const decimal StaleMinProgressR = 0.5m;

    /// <summary>The reason to close now, or null to keep the trade. <paramref name="exitPrice"/>: bid for a buy, ask for a sell.</summary>
    public static string? Check(OpenPosition position, decimal exitPrice, MarketRegime regime, IndicatorSnapshot primary, DateTime nowUtc)
    {
        var risk = Math.Abs(position.EntryPrice - position.StopLoss);
        if (risk <= 0)
        {
            return null;
        }

        var sign = position.Direction.Sign();
        var r = sign * (exitPrice - position.EntryPrice) / risk;
        if (r >= 0)
        {
            return null;
        }

        var trendAgainst = position.Direction == Direction.Long ? MarketRegime.TrendingBearish : MarketRegime.TrendingBullish;
        if (regime == trendAgainst)
        {
            return $"The trend turned against the trade (regime {regime}) at {r:0.00}R.";
        }

        if (r <= -MomentumLossR && primary is { Ema9: { } ema9, Ema20: { } ema20, MacdHistogram: { } histogram }
            && sign * (ema9 - ema20) < 0 && sign * histogram < 0)
        {
            return $"1H momentum turned against the trade (EMA9 crossed EMA20, MACD against) at {r:0.00}R.";
        }

        var held = nowUtc - position.OpenedAtUtc;
        var bestR = position.MaxFavorableExcursion / risk;
        if (held >= StaleAfter && bestR < StaleMinProgressR)
        {
            return $"No progress after {held.TotalHours:0} h (best {bestR:0.00}R), now {r:0.00}R.";
        }

        return null;
    }
}
