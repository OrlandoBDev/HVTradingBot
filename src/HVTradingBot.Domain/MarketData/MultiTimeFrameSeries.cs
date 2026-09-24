using HVTradingBot.Domain.Analysis;

namespace HVTradingBot.Domain.MarketData;

/// <summary>
/// Builds higher timeframes from a stream of 5-minute bars. Only fully closed bars are exposed
/// so that strategies can never see a partially formed (future) bar.
/// </summary>
public sealed class MultiTimeFrameSeries
{
    private readonly int _maxBarsPerTimeFrame;
    private readonly Dictionary<TimeFrame, List<Candle>> _closed = new();
    private readonly Dictionary<TimeFrame, Candle> _forming = new();
    private readonly Dictionary<TimeFrame, IndicatorSnapshot> _indicatorCache = new();
    private (int Count, DateTime LastOpen, MarketStructure Structure)? _structureCache;

    public MultiTimeFrameSeries(Instrument instrument, int maxBarsPerTimeFrame = 400)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBarsPerTimeFrame, 50);
        Instrument = instrument;
        _maxBarsPerTimeFrame = maxBarsPerTimeFrame;
        foreach (var timeFrame in Enum.GetValues<TimeFrame>())
        {
            _closed[timeFrame] = [];
        }
    }

    public Instrument Instrument { get; }

    public Candle? LastBar => _closed[TimeFrame.M5].LastOrDefault();

    /// <summary>Closed bars (read-only view; valid until the next <see cref="Add"/>).</summary>
    public IReadOnlyList<Candle> Closed(TimeFrame timeFrame) => _closed[timeFrame].AsReadOnly();

    /// <summary>1H market structure, cached until the next 1H bar closes.</summary>
    public MarketStructure PrimaryStructure(decimal? atr)
    {
        var closed = _closed[TimeFrame.H1];
        var lastOpen = closed.Count == 0 ? DateTime.MinValue : closed[^1].OpenTimeUtc;
        if (_structureCache is not { } cache || cache.Count != closed.Count || cache.LastOpen != lastOpen)
        {
            cache = (closed.Count, lastOpen, MarketStructureAnalyzer.Analyze(closed, atr));
            _structureCache = cache;
        }

        return cache.Structure;
    }

    /// <summary>
    /// Indicators for the latest closed bar of <paramref name="timeFrame"/>, cached until that timeframe closes another bar.
    /// Returns null when no bar has closed yet.
    /// </summary>
    public IndicatorSnapshot? Indicators(TimeFrame timeFrame)
    {
        var closed = _closed[timeFrame];
        if (closed.Count == 0)
        {
            return null;
        }

        if (!_indicatorCache.TryGetValue(timeFrame, out var cached) || cached.BarCount != closed.Count || cached.LastBarOpenUtc != closed[^1].OpenTimeUtc)
        {
            cached = IndicatorSnapshot.Calculate(timeFrame, closed);
            _indicatorCache[timeFrame] = cached;
        }

        return cached;
    }

    /// <summary>Adds a closed 5m bar. Returns the timeframes whose bar closed with this update.</summary>
    public IReadOnlyList<TimeFrame> Add(Candle bar)
    {
        if (bar.TimeFrame != TimeFrame.M5)
        {
            throw new ArgumentException("Only 5-minute bars can be added.", nameof(bar));
        }

        if (LastBar is { } last && bar.OpenTimeUtc <= last.OpenTimeUtc)
        {
            throw new InvalidOperationException(
                $"{Instrument}: bar {bar.OpenTimeUtc:O} is not after the last bar {last.OpenTimeUtc:O}.");
        }

        var closedNow = new List<TimeFrame> { TimeFrame.M5 };
        Append(TimeFrame.M5, bar);

        foreach (var timeFrame in Enum.GetValues<TimeFrame>().Where(t => t != TimeFrame.M5))
        {
            var start = timeFrame.BarStart(bar.OpenTimeUtc);

            if (_forming.TryGetValue(timeFrame, out var forming) && forming.OpenTimeUtc != start)
            {
                // A gap (e.g. weekend) closed the previous bar without reaching its natural end.
                Append(timeFrame, forming);
                _forming.Remove(timeFrame);
                if (!closedNow.Contains(timeFrame))
                {
                    closedNow.Add(timeFrame);
                }
            }

            var updated = _forming.TryGetValue(timeFrame, out var current)
                ? current with
                {
                    High = Math.Max(current.High, bar.High),
                    Low = Math.Min(current.Low, bar.Low),
                    Close = bar.Close,
                    Spread = bar.Spread,
                    Volume = current.Volume + bar.Volume
                }
                : bar with { OpenTimeUtc = start, TimeFrame = timeFrame };

            if (bar.CloseTimeUtc >= updated.CloseTimeUtc)
            {
                Append(timeFrame, updated);
                _forming.Remove(timeFrame);
                if (!closedNow.Contains(timeFrame))
                {
                    closedNow.Add(timeFrame);
                }
            }
            else
            {
                _forming[timeFrame] = updated;
            }
        }

        return closedNow;
    }

    private void Append(TimeFrame timeFrame, Candle candle)
    {
        var list = _closed[timeFrame];
        list.Add(candle);
        if (list.Count > _maxBarsPerTimeFrame)
        {
            list.RemoveRange(0, list.Count - _maxBarsPerTimeFrame);
        }
    }
}
