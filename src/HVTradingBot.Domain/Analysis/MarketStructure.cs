using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Analysis;

public enum StructureTrend
{
    Up,
    Down,
    Range
}

public sealed record SwingPoint(int Index, DateTime TimeUtc, decimal Price, bool IsHigh);

/// <summary>
/// Market structure for one timeframe. Swing points use a fractal of <c>SwingStrength</c> bars on each side,
/// so a swing is confirmed only after those bars have closed (no look-ahead).
/// </summary>
public sealed record MarketStructure(
    StructureTrend Trend,
    bool HigherHighs,
    bool HigherLows,
    bool LowerHighs,
    bool LowerLows,
    decimal? Support,
    decimal? Resistance,
    bool BullishBreakout,
    bool BearishBreakout,
    bool BullishRetest,
    bool BearishRetest,
    bool FalseBreakoutCandidate,
    bool VolatilityExpansion,
    bool VolatilityContraction,
    IReadOnlyList<SwingPoint> Swings);

public static class MarketStructureAnalyzer
{
    public const int SwingStrength = 2;
    private const int RangeLookback = 20;
    private const int BreakoutWindow = 5;

    public static MarketStructure Analyze(IReadOnlyList<Candle> candles, decimal? atr)
    {
        var swings = FindSwings(candles);
        var highs = swings.Where(s => s.IsHigh).TakeLast(2).ToArray();
        var lows = swings.Where(s => !s.IsHigh).TakeLast(2).ToArray();

        var hh = highs.Length == 2 && highs[1].Price > highs[0].Price;
        var lh = highs.Length == 2 && highs[1].Price < highs[0].Price;
        var hl = lows.Length == 2 && lows[1].Price > lows[0].Price;
        var ll = lows.Length == 2 && lows[1].Price < lows[0].Price;

        var trend = hh && hl ? StructureTrend.Up : lh && ll ? StructureTrend.Down : StructureTrend.Range;

        decimal? support = null, resistance = null;
        bool bullBreak = false, bearBreak = false, bullRetest = false, bearRetest = false, falseBreak = false;

        // Level = extreme of the window that ends before the breakout window, so the level itself is not moved by the breakout.
        if (candles.Count >= RangeLookback + BreakoutWindow && atr is { } atrValue && atrValue > 0)
        {
            var levelWindow = candles.Skip(candles.Count - RangeLookback - BreakoutWindow).Take(RangeLookback).ToArray();
            resistance = levelWindow.Max(c => c.High);
            support = levelWindow.Min(c => c.Low);

            var recent = candles.Skip(candles.Count - BreakoutWindow).ToArray();
            var last = recent[^1];
            var tolerance = atrValue * 0.3m;

            bullBreak = recent.Any(c => c.Close > resistance);
            bearBreak = recent.Any(c => c.Close < support);

            bullRetest = bullBreak
                && recent[..^1].Any(c => c.Close > resistance)
                && last.Low <= resistance + tolerance
                && last.Close > resistance.Value
                && last.Close > last.Open;

            bearRetest = bearBreak
                && recent[..^1].Any(c => c.Close < support)
                && last.High >= support - tolerance
                && last.Close < support.Value
                && last.Close < last.Open;

            falseBreak = (recent[..^1].Any(c => c.Close > resistance) && last.Close < resistance)
                || (recent[..^1].Any(c => c.Close < support) && last.Close > support);
        }

        var ranges = candles.TakeLast(RangeLookback).Select(c => c.Range).ToArray();
        var expansion = false;
        var contraction = false;
        if (ranges.Length == RangeLookback)
        {
            var baseline = ranges[..^3].Average();
            var latest = ranges[^3..].Average();
            expansion = baseline > 0 && latest > baseline * 1.5m;
            contraction = baseline > 0 && latest < baseline * 0.6m;
        }

        return new MarketStructure(trend, hh, hl, lh, ll, support, resistance,
            bullBreak, bearBreak, bullRetest, bearRetest, falseBreak, expansion, contraction, swings);
    }

    public static IReadOnlyList<SwingPoint> FindSwings(IReadOnlyList<Candle> candles)
    {
        var swings = new List<SwingPoint>();
        for (var i = SwingStrength; i < candles.Count - SwingStrength; i++)
        {
            var isHigh = true;
            var isLow = true;
            for (var j = 1; j <= SwingStrength; j++)
            {
                isHigh &= candles[i].High > candles[i - j].High && candles[i].High >= candles[i + j].High;
                isLow &= candles[i].Low < candles[i - j].Low && candles[i].Low <= candles[i + j].Low;
            }

            if (isHigh) swings.Add(new SwingPoint(i, candles[i].OpenTimeUtc, candles[i].High, true));
            if (isLow) swings.Add(new SwingPoint(i, candles[i].OpenTimeUtc, candles[i].Low, false));
        }

        return swings;
    }
}
