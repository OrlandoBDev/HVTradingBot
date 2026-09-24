using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Analysis;

/// <summary>
/// Deterministic technical indicators. Every function returns a series aligned with its input;
/// entries are null until enough history exists (warm-up).
/// </summary>
public static class Indicators
{
    public static decimal?[] Sma(IReadOnlyList<decimal> values, int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        var result = new decimal?[values.Count];
        decimal sum = 0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period)
            {
                sum -= values[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = sum / period;
            }
        }

        return result;
    }

    /// <summary>Exponential moving average seeded with the SMA of the first <paramref name="period"/> values.</summary>
    public static decimal?[] Ema(IReadOnlyList<decimal> values, int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        var result = new decimal?[values.Count];
        if (values.Count < period)
        {
            return result;
        }

        var k = 2m / (period + 1);
        decimal seed = 0;
        for (var i = 0; i < period; i++)
        {
            seed += values[i];
        }

        var ema = seed / period;
        result[period - 1] = ema;
        for (var i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * k + ema;
            result[i] = ema;
        }

        return result;
    }

    /// <summary>Wilder's RSI.</summary>
    public static decimal?[] Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        var result = new decimal?[closes.Count];
        if (closes.Count <= period)
        {
            return result;
        }

        decimal gain = 0, loss = 0;
        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0) gain += change; else loss -= change;
        }

        var avgGain = gain / period;
        var avgLoss = loss / period;
        result[period] = RsiValue(avgGain, avgLoss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var up = change > 0 ? change : 0;
            var down = change < 0 ? -change : 0;
            avgGain = (avgGain * (period - 1) + up) / period;
            avgLoss = (avgLoss * (period - 1) + down) / period;
            result[i] = RsiValue(avgGain, avgLoss);
        }

        return result;
    }

    private static decimal RsiValue(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0)
        {
            return avgGain == 0 ? 50m : 100m;
        }

        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }

    public static MacdSeries Macd(IReadOnlyList<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
    {
        var fastEma = Ema(closes, fast);
        var slowEma = Ema(closes, slow);
        var macd = new decimal?[closes.Count];
        for (var i = 0; i < closes.Count; i++)
        {
            if (fastEma[i] is { } f && slowEma[i] is { } s)
            {
                macd[i] = f - s;
            }
        }

        var firstValid = Array.FindIndex(macd, v => v.HasValue);
        var signalLine = new decimal?[closes.Count];
        var histogram = new decimal?[closes.Count];
        if (firstValid >= 0)
        {
            var valid = macd.Skip(firstValid).Select(v => v!.Value).ToArray();
            var signalValid = Ema(valid, signal);
            for (var i = 0; i < signalValid.Length; i++)
            {
                signalLine[firstValid + i] = signalValid[i];
                if (signalValid[i] is { } sig)
                {
                    histogram[firstValid + i] = valid[i] - sig;
                }
            }
        }

        return new MacdSeries(macd, signalLine, histogram);
    }

    public static decimal[] TrueRange(IReadOnlyList<Candle> candles)
    {
        var result = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            result[i] = i == 0
                ? c.High - c.Low
                : Math.Max(c.High - c.Low,
                    Math.Max(Math.Abs(c.High - candles[i - 1].Close), Math.Abs(c.Low - candles[i - 1].Close)));
        }

        return result;
    }

    /// <summary>Wilder's Average True Range.</summary>
    public static decimal?[] Atr(IReadOnlyList<Candle> candles, int period = 14) =>
        WilderSmooth(TrueRange(candles), period, startIndex: 1);

    /// <summary>Wilder's Average Directional Index.</summary>
    public static decimal?[] Adx(IReadOnlyList<Candle> candles, int period = 14)
    {
        var count = candles.Count;
        var result = new decimal?[count];
        if (count < period * 2 + 1)
        {
            return result;
        }

        var plusDm = new decimal[count];
        var minusDm = new decimal[count];
        for (var i = 1; i < count; i++)
        {
            var up = candles[i].High - candles[i - 1].High;
            var down = candles[i - 1].Low - candles[i].Low;
            plusDm[i] = up > down && up > 0 ? up : 0;
            minusDm[i] = down > up && down > 0 ? down : 0;
        }

        var tr = TrueRange(candles);
        var smoothTr = WilderSmooth(tr, period, 1);
        var smoothPlus = WilderSmooth(plusDm, period, 1);
        var smoothMinus = WilderSmooth(minusDm, period, 1);

        var dx = new decimal[count];
        var dxStart = -1;
        for (var i = 0; i < count; i++)
        {
            if (smoothTr[i] is not { } atr || atr == 0 || smoothPlus[i] is not { } p || smoothMinus[i] is not { } m)
            {
                continue;
            }

            var plusDi = 100m * p / atr;
            var minusDi = 100m * m / atr;
            var sum = plusDi + minusDi;
            dx[i] = sum == 0 ? 0 : 100m * Math.Abs(plusDi - minusDi) / sum;
            if (dxStart < 0) dxStart = i;
        }

        if (dxStart < 0)
        {
            return result;
        }

        var adx = WilderSmooth(dx, period, dxStart);
        return adx;
    }

    /// <summary>Wilder smoothing (RMA), seeded with the simple average of the first <paramref name="period"/> values starting at <paramref name="startIndex"/>.</summary>
    private static decimal?[] WilderSmooth(IReadOnlyList<decimal> values, int period, int startIndex)
    {
        var result = new decimal?[values.Count];
        var seedEnd = startIndex + period - 1;
        if (seedEnd >= values.Count)
        {
            return result;
        }

        decimal sum = 0;
        for (var i = startIndex; i <= seedEnd; i++)
        {
            sum += values[i];
        }

        var avg = sum / period;
        result[seedEnd] = avg;
        for (var i = seedEnd + 1; i < values.Count; i++)
        {
            avg = (avg * (period - 1) + values[i]) / period;
            result[i] = avg;
        }

        return result;
    }

    public static BollingerSeries Bollinger(IReadOnlyList<decimal> closes, int period = 20, decimal stdDevs = 2m)
    {
        var middle = Sma(closes, period);
        var upper = new decimal?[closes.Count];
        var lower = new decimal?[closes.Count];
        for (var i = period - 1; i < closes.Count; i++)
        {
            var mean = middle[i]!.Value;
            decimal variance = 0;
            for (var j = i - period + 1; j <= i; j++)
            {
                var d = closes[j] - mean;
                variance += d * d;
            }

            var sd = DecimalMath.Sqrt(variance / period);
            upper[i] = mean + stdDevs * sd;
            lower[i] = mean - stdDevs * sd;
        }

        return new BollingerSeries(upper, middle, lower);
    }

    /// <summary>Rate of change in percent.</summary>
    public static decimal?[] RateOfChange(IReadOnlyList<decimal> closes, int period = 10)
    {
        var result = new decimal?[closes.Count];
        for (var i = period; i < closes.Count; i++)
        {
            if (closes[i - period] != 0)
            {
                result[i] = (closes[i] - closes[i - period]) / closes[i - period] * 100m;
            }
        }

        return result;
    }

    public static decimal?[] Momentum(IReadOnlyList<decimal> closes, int period = 10)
    {
        var result = new decimal?[closes.Count];
        for (var i = period; i < closes.Count; i++)
        {
            result[i] = closes[i] - closes[i - period];
        }

        return result;
    }

    /// <summary>
    /// Percentile rank (0..1) of the last value within the trailing window. Ties count half, so a constant series ranks 0.5.
    /// </summary>
    public static decimal? PercentRank(IReadOnlyList<decimal?> series, int lookback)
    {
        if (series.Count == 0 || series[^1] is not { } last)
        {
            return null;
        }

        var window = series.Skip(Math.Max(0, series.Count - lookback)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (window.Count < Math.Min(lookback, 20))
        {
            return null;
        }

        return (window.Count(v => v < last) + window.Count(v => v == last) / 2m) / window.Count;
    }
}

public sealed record MacdSeries(decimal?[] Macd, decimal?[] Signal, decimal?[] Histogram);

public sealed record BollingerSeries(decimal?[] Upper, decimal?[] Middle, decimal?[] Lower);
