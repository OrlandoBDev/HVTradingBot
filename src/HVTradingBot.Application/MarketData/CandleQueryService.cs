using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.MarketData;

/// <summary>Validated request for a chart's candles. Bounds are UTC; <see cref="ToUtc"/> is exclusive.</summary>
public sealed record CandleQuery(string Symbol, TimeFrame TimeFrame, DateTime? FromUtc, DateTime? ToUtc, int Limit);

/// <summary>
/// Serves stored candles for charts. Only 5m bars are stored, so higher timeframes are built from them here; the newest
/// bar of a higher timeframe may still be forming.
/// </summary>
public sealed class CandleQueryService(ICandleStore store)
{
    public const int DefaultLimit = 300;
    public const int MaxLimit = 2000;

    /// <summary>
    /// Parses query-string values. Returns null and fills <paramref name="errors"/> (keyed by parameter) when invalid.
    /// Not named TryParse: minimal APIs would then try to bind this service from the request and refuse to start.
    /// </summary>
    public static CandleQuery? ParseQuery(string? instrument, string? timeframe, DateTime? from, DateTime? to, int? limit,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(instrument))
        {
            errors["instrument"] = ["Instrument is required, e.g. EUR/USD."];
        }

        var timeFrame = TimeFrame.M5;
        if (!string.IsNullOrWhiteSpace(timeframe)
            && (!Enum.TryParse(timeframe.Trim(), ignoreCase: true, out timeFrame) || !Enum.IsDefined(timeFrame)))
        {
            errors["timeframe"] = [$"Unknown timeframe '{timeframe}'. Use one of {string.Join(", ", Enum.GetNames<TimeFrame>())}."];
        }

        var count = limit ?? DefaultLimit;
        if (count is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"Limit must be between 1 and {MaxLimit}."];
        }

        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        if (fromUtc is { } f && toUtc is { } t && f >= t)
        {
            errors["from"] = ["From must be before to."];
        }

        if (errors.Count > 0)
        {
            return null;
        }

        // Stored bars use the canonical symbol; accept any casing for known markets.
        var symbol = instrument!.Trim();
        if (Instruments.TryGet(symbol, out var known))
        {
            symbol = known.Symbol;
        }

        return new CandleQuery(symbol, timeFrame, fromUtc, toUtc, count);
    }

    /// <summary>Up to <see cref="CandleQuery.Limit"/> candles, oldest first; the newest are kept when there are more.</summary>
    public async Task<IReadOnlyList<Candle>> GetAsync(CandleQuery query, CancellationToken cancellationToken)
    {
        if (query.TimeFrame == TimeFrame.M5)
        {
            return await store.GetM5Async(query.Symbol, query.FromUtc, query.ToUtc, query.Limit, cancellationToken);
        }

        // Start on a bar boundary so the first bar is not cut short by the range.
        var fromUtc = query.FromUtc is { } from ? query.TimeFrame.BarStart(from) : (DateTime?)null;
        var perBar = (int)(query.TimeFrame.Duration().Ticks / TimeFrame.M5.Duration().Ticks);
        // One extra bar's worth, because the oldest bar fetched is usually partial and gets dropped.
        var m5Limit = (query.Limit + 1) * perBar;
        var m5 = await store.GetM5Async(query.Symbol, fromUtc, query.ToUtc, m5Limit, cancellationToken);

        var bars = Aggregate(m5, query.TimeFrame);
        if (m5.Count == m5Limit && bars.Count > 0)
        {
            // The limit cut into the oldest bar, so it is missing its first 5m bars.
            bars.RemoveAt(0);
        }

        return bars.Count > query.Limit ? bars.GetRange(bars.Count - query.Limit, query.Limit) : bars;
    }

    /// <summary>Builds <paramref name="timeFrame"/> bars from 5m bars (oldest first), the same way the trading engine does.</summary>
    public static List<Candle> Aggregate(IReadOnlyList<Candle> m5, TimeFrame timeFrame)
    {
        var bars = new List<Candle>();
        foreach (var bar in m5)
        {
            var start = timeFrame.BarStart(bar.OpenTimeUtc);
            if (bars.Count > 0 && bars[^1].OpenTimeUtc == start)
            {
                var current = bars[^1];
                bars[^1] = current with
                {
                    High = Math.Max(current.High, bar.High),
                    Low = Math.Min(current.Low, bar.Low),
                    Close = bar.Close,
                    Spread = bar.Spread,
                    Volume = current.Volume + bar.Volume
                };
            }
            else
            {
                bars.Add(bar with { OpenTimeUtc = start, TimeFrame = timeFrame });
            }
        }

        return bars;
    }

    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };
}
