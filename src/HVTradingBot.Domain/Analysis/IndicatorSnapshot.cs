using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Analysis;

/// <summary>Indicator values for the latest closed bar of one timeframe, plus short history needed by strategies.</summary>
public sealed record IndicatorSnapshot(
    TimeFrame TimeFrame,
    int BarCount,
    DateTime LastBarOpenUtc,
    decimal Close,
    decimal? Ema9,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Ema200,
    decimal? Rsi,
    decimal? Macd,
    decimal? MacdSignal,
    IReadOnlyList<decimal> MacdHistogramRecent,
    decimal? Atr,
    decimal? AtrPercentRank,
    decimal? Adx,
    decimal? BollingerUpper,
    decimal? BollingerMiddle,
    decimal? BollingerLower,
    IReadOnlyList<decimal> BollingerWidthRecent,
    decimal? BollingerWidthPercentRank,
    decimal? RateOfChange,
    decimal? Momentum)
{
    public decimal? MacdHistogram => MacdHistogramRecent.Count > 0 ? MacdHistogramRecent[^1] : null;

    public static IndicatorSnapshot Calculate(TimeFrame timeFrame, IReadOnlyList<Candle> candles)
    {
        if (candles.Count == 0)
        {
            throw new ArgumentException("At least one candle is required.", nameof(candles));
        }

        var closes = candles.Select(c => c.Close).ToArray();
        var macd = Indicators.Macd(closes);
        var atr = Indicators.Atr(candles);
        var bollinger = Indicators.Bollinger(closes);

        var width = new decimal?[closes.Length];
        for (var i = 0; i < closes.Length; i++)
        {
            if (bollinger.Upper[i] is { } u && bollinger.Lower[i] is { } l && bollinger.Middle[i] is { } m && m != 0)
            {
                width[i] = (u - l) / m;
            }
        }

        return new IndicatorSnapshot(
            timeFrame,
            candles.Count,
            candles[^1].OpenTimeUtc,
            closes[^1],
            Indicators.Ema(closes, 9)[^1],
            Indicators.Ema(closes, 20)[^1],
            Indicators.Ema(closes, 50)[^1],
            Indicators.Ema(closes, 200)[^1],
            Indicators.Rsi(closes)[^1],
            macd.Macd[^1],
            macd.Signal[^1],
            Recent(macd.Histogram, 5),
            atr[^1],
            Indicators.PercentRank(atr, 100),
            Indicators.Adx(candles)[^1],
            bollinger.Upper[^1],
            bollinger.Middle[^1],
            bollinger.Lower[^1],
            Recent(width, 10),
            Indicators.PercentRank(width, 100),
            Indicators.RateOfChange(closes)[^1],
            Indicators.Momentum(closes)[^1]);
    }

    private static IReadOnlyList<decimal> Recent(decimal?[] series, int count) =>
        series.Skip(Math.Max(0, series.Length - count)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
}
