using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.MarketData;

/// <summary>
/// Real Forex prices from Deriv's public WebSocket (no authentication). 5-minute candles are fetched after each bar
/// closes; a tick subscription supplies bid/ask (spread) and drives data-freshness. Outside market hours no bars
/// arrive, the feed goes stale and the risk engine blocks new trades.
/// </summary>
public sealed class DerivMarketDataFeed(
    IDbContextFactory<TradingDbContext> dbFactory,
    DerivOptions derivOptions,
    MarketDataOptions marketDataOptions,
    TradingUniverse universe,
    IDerivSocketFactory socketFactory,
    IClock clock,
    ILogger<DerivMarketDataFeed> logger) : IMarketDataFeed, IAsyncDisposable
{
    private const int Granularity = 300;
    private const int MaxCandlesPerRequest = 1000;
    private static readonly TimeSpan BarSettleDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxTickAgeForSpread = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly Dictionary<Instrument, (decimal Bid, decimal Ask, DateTime ReceivedUtc, DateTime TickTimeUtc)> _ticks = new();
    private readonly Dictionary<Instrument, DateTime> _lastBarOpen = new();
    private readonly Queue<IReadOnlyList<InstrumentBar>> _ready = new();
    private IReadOnlyList<Instrument> _instruments => universe.Data;
    private IDerivSocket? _socket;
    private DateTime? _lastBarTimeUtc;

    public MarketDataStatus Status
    {
        get
        {
            lock (_sync)
            {
                var lastTick = _ticks.Count == 0 ? (DateTime?)null : _ticks.Values.Max(t => t.ReceivedUtc);
                return new MarketDataStatus(_lastBarTimeUtc, lastTick);
            }
        }
    }

    public IReadOnlyCollection<Quote> LatestQuotes
    {
        get
        {
            lock (_sync)
            {
                return _ticks.Select(t => new Quote(t.Key, t.Value.TickTimeUtc, t.Value.Bid, t.Value.Ask)).ToList();
            }
        }
    }

    public async Task<IReadOnlyDictionary<Instrument, IReadOnlyList<Candle>>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var socket = await EnsureConnectedAsync(cancellationToken);
        var from = clock.UtcNow.AddDays(-marketDataOptions.WarmupDays);
        var history = new Dictionary<Instrument, IReadOnlyList<Candle>>();

        foreach (var instrument in _instruments)
        {
            // Closed markets (e.g. stock indices outside trading hours) send no ticks; don't wait long for them.
            var spread = await CurrentSpreadAsync(instrument, waitForTick: true, cancellationToken);
            var bars = await FetchClosedCandlesAsync(socket, instrument, from, spread, cancellationToken);
            if (bars.Count == 0)
            {
                logger.LogWarning("Deriv returned no 5m candles for {Instrument}; it will join once data arrives", instrument.Symbol);
                continue;
            }

            await PersistAsync(instrument, bars, cancellationToken);
            _lastBarOpen[instrument] = bars[^1].OpenTimeUtc;
            history[instrument] = bars;
            logger.LogInformation("Loaded {Count} Deriv 5m candles for {Instrument} ({From:u} to {To:u}), spread {Spread}",
                bars.Count, instrument.Symbol, bars[0].OpenTimeUtc, bars[^1].OpenTimeUtc, spread);
        }

        if (history.Count == 0)
        {
            throw new InvalidOperationException("Deriv returned no candle history for any selected market.");
        }

        _lastBarTimeUtc = history.Values.Max(b => b[^1].CloseTimeUtc);
        return history;
    }

    public async Task<IReadOnlyList<InstrumentBar>> NextBarsAsync(CancellationToken cancellationToken)
    {
        if (_ready.TryDequeue(out var queued))
        {
            return queued;
        }

        var now = clock.UtcNow;
        var nextClose = TimeFrame.M5.BarStart(now).AddMinutes(5) + BarSettleDelay;
        await Task.Delay(nextClose - now, cancellationToken);

        var socket = await EnsureConnectedAsync(cancellationToken);
        var fresh = new List<InstrumentBar>();
        foreach (var instrument in _instruments)
        {
            var since = _lastBarOpen.GetValueOrDefault(instrument, clock.UtcNow.AddHours(-1)).AddSeconds(1);
            var spread = await CurrentSpreadAsync(instrument, waitForTick: false, cancellationToken);
            var bars = await FetchClosedCandlesAsync(socket, instrument, since, spread, cancellationToken);
            if (bars.Count == 0)
            {
                continue;
            }

            await PersistAsync(instrument, bars, cancellationToken);
            _lastBarOpen[instrument] = bars[^1].OpenTimeUtc;
            fresh.AddRange(bars.Select(b => new InstrumentBar(instrument, b)));
        }

        // The engine expects one bar per instrument per call; back-filled gaps are replayed in time order.
        foreach (var group in fresh.GroupBy(b => b.Bar.OpenTimeUtc).OrderBy(g => g.Key))
        {
            _ready.Enqueue(group.ToList());
            _lastBarTimeUtc = group.First().Bar.CloseTimeUtc;
        }

        return _ready.TryDequeue(out var next) ? next : [];
    }

    private async Task<IDerivSocket> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_socket is { IsConnected: true })
        {
            return _socket;
        }

        if (_socket is not null)
        {
            logger.LogWarning("Deriv market-data connection lost; reconnecting");
            await _socket.DisposeAsync();
        }

        _socket = await socketFactory.ConnectAsync(new Uri(derivOptions.PublicWebSocketUrl),
            TimeSpan.FromSeconds(derivOptions.RequestTimeoutSeconds), cancellationToken);

        foreach (var instrument in _instruments)
        {
            try
            {
                var first = await _socket.SubscribeAsync(new JsonObject { ["ticks"] = DerivSymbols.For(instrument) }, OnTick, cancellationToken);
                OnTick(first);
            }
            catch (DerivApiException ex)
            {
                // e.g. market closed: no live ticks until it reopens; freshness checks will block trading.
                logger.LogWarning("Tick subscription for {Instrument} rejected: {Message}", instrument.Symbol, ex.Message);
            }
        }

        return _socket;
    }

    private void OnTick(JsonElement message)
    {
        if (!message.TryGetProperty("tick", out var tick)
            || DerivSymbols.ToInstrument(tick.TryGetProperty("symbol", out var s) ? s.GetString() : null) is not { } instrument
            || !tick.TryGetProperty("bid", out var bid) || !tick.TryGetProperty("ask", out var ask))
        {
            return;
        }

        lock (_sync)
        {
            var tickTime = tick.TryGetProperty("epoch", out var ep) && ep.TryGetInt64(out var epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime
                : clock.UtcNow;
            _ticks[instrument] = (bid.GetDecimal(), ask.GetDecimal(), clock.UtcNow, tickTime);
        }
    }

    private async Task<decimal> CurrentSpreadAsync(Instrument instrument, bool waitForTick, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < (waitForTick ? 20 : 1); attempt++)
        {
            lock (_sync)
            {
                if (_ticks.TryGetValue(instrument, out var t) && clock.UtcNow - t.ReceivedUtc <= MaxTickAgeForSpread && t.Ask > t.Bid)
                {
                    return instrument.RoundPrice(t.Ask - t.Bid);
                }
            }

            if (waitForTick)
            {
                await Task.Delay(250, cancellationToken);
            }
        }

        // Market closed or no tick yet: assume a typical spread (1 pip / 1 price increment) for stored bars only.
        return instrument.FromPips(1m);
    }

    /// <summary>
    /// Pages backwards through Deriv's candle history from now to <paramref name="fromUtc"/>. Candles are stamped with
    /// the spread observed now (Deriv candles carry mid prices only), and the still-forming bar is excluded.
    /// </summary>
    private async Task<List<Candle>> FetchClosedCandlesAsync(
        IDerivSocket socket, Instrument instrument, DateTime fromUtc, decimal spread, CancellationToken cancellationToken)
    {
        var fromEpoch = new DateTimeOffset(fromUtc).ToUnixTimeSeconds();
        var nowEpoch = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds();
        var collected = new Dictionary<long, Candle>();
        JsonNode end = "latest";

        // Deriv returns the candles inside a time window of count x granularity ending at `end`, so pages that span a
        // weekend hold fewer than `count` candles. Keep paging back until the window start passes `fromUtc`.
        for (var page = 0; page < 60; page++)
        {
            var response = await socket.SendAsync(new JsonObject
            {
                ["ticks_history"] = DerivSymbols.For(instrument),
                ["style"] = "candles",
                ["granularity"] = Granularity,
                ["count"] = MaxCandlesPerRequest,
                ["end"] = end.DeepClone()
            }, cancellationToken);

            if (!response.TryGetProperty("candles", out var candles) || candles.GetArrayLength() == 0)
            {
                break;
            }

            var oldest = long.MaxValue;
            foreach (var c in candles.EnumerateArray())
            {
                var epoch = c.GetProperty("epoch").GetInt64();
                oldest = Math.Min(oldest, epoch);
                // Skip bars outside the range, the still-forming bar, and any bar not aligned to the 5-minute grid.
                if (epoch < fromEpoch || epoch + Granularity > nowEpoch || epoch % Granularity != 0)
                {
                    continue;
                }

                collected[epoch] = new Candle(
                    DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime,
                    TimeFrame.M5,
                    c.GetProperty("open").GetDecimal(),
                    c.GetProperty("high").GetDecimal(),
                    c.GetProperty("low").GetDecimal(),
                    c.GetProperty("close").GetDecimal(),
                    spread,
                    0);
            }

            var windowStart = (end is JsonValue v && v.TryGetValue<long>(out var endEpoch) ? endEpoch : nowEpoch)
                              - (long)MaxCandlesPerRequest * Granularity;
            var nextEnd = Math.Min(oldest, windowStart) / Granularity * Granularity;
            if (nextEnd <= fromEpoch)
            {
                break;
            }

            end = nextEnd;
        }

        return collected.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    private async Task PersistAsync(Instrument instrument, IReadOnlyList<Candle> bars, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var from = bars[0].OpenTimeUtc;
        var existing = (await db.Candles.AsNoTracking()
                .Where(c => c.Instrument == instrument.Symbol && c.TimeFrame == nameof(TimeFrame.M5) && c.OpenTimeUtc >= from)
                .Select(c => c.OpenTimeUtc)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = bars.Where(b => !existing.Contains(b.OpenTimeUtc)).ToList();
        foreach (var chunk in missing.Chunk(5000))
        {
            db.Candles.AddRange(chunk.Select(c => new CandleEntity
            {
                Instrument = instrument.Symbol,
                TimeFrame = nameof(TimeFrame.M5),
                OpenTimeUtc = c.OpenTimeUtc,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Spread = c.Spread,
                Volume = c.Volume
            }));
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }
    }
}

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>"Deriv" (real prices, default) or "Simulated" (offline synthetic data).</summary>
    public MarketDataProvider Provider { get; set; } = MarketDataProvider.Deriv;

    [System.ComponentModel.DataAnnotations.Range(10, 120)]
    public int WarmupDays { get; set; } = 45;

    /// <summary>How often the worker re-reads Deriv's market list (new markets, contract availability).</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 168)]
    public int CatalogRefreshHours { get; set; } = 24;
}

public enum MarketDataProvider
{
    Deriv,
    Simulated
}
