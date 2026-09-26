using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.News;

namespace HVTradingBot.Domain.Analysis;

/// <summary>
/// Everything a strategy may look at for one instrument at one point in time. Contains closed bars only.
/// Indicators are calculated for the analysis timeframes (15m timing, 1H setup, 4H structure, Daily regime); 5m bars
/// are available as candles for execution-level checks such as spread.
/// </summary>
public sealed record MarketContext(
    Instrument Instrument,
    DateTime AsOfUtc,
    Quote Quote,
    IReadOnlyDictionary<TimeFrame, IReadOnlyList<Candle>> Candles,
    IReadOnlyDictionary<TimeFrame, IndicatorSnapshot> Indicators,
    MarketStructure PrimaryStructure,
    MarketRegime Regime,
    decimal AverageSpread,
    bool IsDataStale)
{
    /// <summary>News, economic calendar and cross-market trend at <see cref="AsOfUtc"/>; null when not in use.</summary>
    public MarketIntelligenceInputs? Intelligence { get; init; }

    /// <summary>Primary setup timeframe (1H).</summary>
    public IndicatorSnapshot Primary => Indicators[TimeFrame.H1];

    /// <summary>Structural direction timeframe (4H).</summary>
    public IndicatorSnapshot Structural => Indicators[TimeFrame.H4];

    /// <summary>Broad regime timeframe (Daily). May be sparsely warmed up.</summary>
    public IndicatorSnapshot? Daily => Indicators.GetValueOrDefault(TimeFrame.D1);

    public IReadOnlyList<Candle> PrimaryCandles => Candles[TimeFrame.H1];

    /// <summary>15m (entry timing), 1H (setup), 4H (structure), Daily (regime). 5m bars drive evaluation cadence and spread.</summary>
    public static readonly IReadOnlyList<TimeFrame> AnalysisTimeFrames = [TimeFrame.M15, TimeFrame.H1, TimeFrame.H4, TimeFrame.D1];

    public static MarketContext Build(
        MultiTimeFrameSeries series,
        Quote quote,
        RegimeOptions regimeOptions,
        bool isDataStale)
    {
        var candles = new Dictionary<TimeFrame, IReadOnlyList<Candle>>();
        var indicators = new Dictionary<TimeFrame, IndicatorSnapshot>();
        foreach (var timeFrame in Enum.GetValues<TimeFrame>())
        {
            var closed = series.Closed(timeFrame);
            candles[timeFrame] = closed;
            if (AnalysisTimeFrames.Contains(timeFrame) && series.Indicators(timeFrame) is { } snapshot)
            {
                indicators[timeFrame] = snapshot;
            }
        }

        if (!indicators.ContainsKey(TimeFrame.H1) || !indicators.ContainsKey(TimeFrame.H4))
        {
            throw new InvalidOperationException($"{series.Instrument}: not enough history to build a market context.");
        }

        var primary = indicators[TimeFrame.H1];
        var structure = series.PrimaryStructure(primary.Atr);
        var regime = RegimeClassifier.Classify(indicators[TimeFrame.H4], primary, regimeOptions);
        var m5 = candles[TimeFrame.M5];
        var averageSpread = m5.Count == 0 ? quote.Spread : MeanSpread(m5, 288);

        return new MarketContext(series.Instrument, quote.TimestampUtc, quote, candles, indicators, structure, regime,
            averageSpread, isDataStale);
    }

    private static decimal MeanSpread(IReadOnlyList<Candle> bars, int lookback)
    {
        var start = Math.Max(0, bars.Count - lookback);
        decimal sum = 0;
        for (var i = start; i < bars.Count; i++)
        {
            sum += bars[i].Spread;
        }

        return sum / (bars.Count - start);
    }
}
