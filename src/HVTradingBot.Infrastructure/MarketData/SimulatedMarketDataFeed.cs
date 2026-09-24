using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.MarketData;

/// <summary>
/// Accelerated synthetic feed. Bars are persisted, and on restart the feed continues from the last stored bar
/// so prices stay continuous with any open paper positions.
/// </summary>
public sealed class SimulatedMarketDataFeed(
    IDbContextFactory<TradingDbContext> dbFactory,
    SimulatedMarketOptions options,
    TradingUniverse universe,
    IClock clock,
    ILogger<SimulatedMarketDataFeed> logger) : IMarketDataFeed
{
    private const int InsertBatchSize = 5000;
    private readonly Dictionary<Instrument, MarketSeriesGenerator> _generators = new();
    private MarketDataStatus _status = new(null, null);

    private IReadOnlyCollection<Quote> _latestQuotes = [];

    public MarketDataStatus Status => _status;

    /// <summary>The simulated feed has no ticks; the latest quote is the last bar's close.</summary>
    public IReadOnlyCollection<Quote> LatestQuotes => _latestQuotes;

    public async Task<IReadOnlyDictionary<Instrument, IReadOnlyList<Candle>>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var history = new Dictionary<Instrument, IReadOnlyList<Candle>>();
        var warmupBars = options.WarmupDays * 288;

        foreach (var instrument in universe.Data)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var stored = await db.Candles.AsNoTracking()
                .Where(c => c.Instrument == instrument.Symbol && c.TimeFrame == nameof(TimeFrame.M5))
                .OrderByDescending(c => c.OpenTimeUtc)
                .Take(warmupBars)
                .ToListAsync(cancellationToken);

            List<Candle> bars;
            if (stored.Count == 0)
            {
                var end = TimeFrame.M5.BarStart(clock.UtcNow);
                bars = MarketSeriesGenerator.Generate(instrument, options.Seed, end, options.WarmupDays).ToList();
                logger.LogInformation("Generated {Count} warm-up bars for {Instrument}", bars.Count, instrument.Symbol);
                await PersistAsync(instrument, bars, cancellationToken);
            }
            else
            {
                bars = stored.OrderBy(c => c.OpenTimeUtc).Select(ToCandle).ToList();
                logger.LogInformation("Loaded {Count} stored bars for {Instrument}, last {LastBar:u}", bars.Count, instrument.Symbol, bars[^1].OpenTimeUtc);
            }

            var last = bars[^1];
            _generators[instrument] = new MarketSeriesGenerator(instrument, options.Seed, last.Close, last.CloseTimeUtc);
            history[instrument] = bars;
        }

        var lastBar = history.Values.Max(b => b[^1].CloseTimeUtc);
        _status = new MarketDataStatus(lastBar, clock.UtcNow);
        return history;
    }

    public async Task<IReadOnlyList<InstrumentBar>> NextBarsAsync(CancellationToken cancellationToken)
    {
        if (_generators.Count == 0)
        {
            throw new InvalidOperationException("LoadHistoryAsync must be called first.");
        }

        await Task.Delay(options.BarIntervalMilliseconds, cancellationToken);

        // All instruments advance in lock-step on the same bar time.
        var bars = _generators.Select(g => new InstrumentBar(g.Key, g.Value.Next())).ToList();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.Candles.AddRange(bars.Select(b => ToEntity(b.Instrument, b.Bar)));
            await db.SaveChangesAsync(cancellationToken);
        }

        _status = new MarketDataStatus(bars[0].Bar.CloseTimeUtc, clock.UtcNow);
        _latestQuotes = bars.Select(b => new Quote(b.Instrument, b.Bar.CloseTimeUtc, b.Instrument.RoundPrice(b.Bar.CloseBid),
            b.Instrument.RoundPrice(b.Bar.CloseAsk))).ToList();
        return bars;
    }

    private async Task PersistAsync(Instrument instrument, IReadOnlyList<Candle> bars, CancellationToken cancellationToken)
    {
        foreach (var chunk in bars.Chunk(InsertBatchSize))
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Candles.AddRange(chunk.Select(c => ToEntity(instrument, c)));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static CandleEntity ToEntity(Instrument instrument, Candle c) => new()
    {
        Instrument = instrument.Symbol,
        TimeFrame = c.TimeFrame.ToString(),
        OpenTimeUtc = c.OpenTimeUtc,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Spread = c.Spread,
        Volume = c.Volume
    };

    public static Candle ToCandle(CandleEntity c) =>
        new(DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc), Enum.Parse<TimeFrame>(c.TimeFrame), c.Open, c.High, c.Low, c.Close, c.Spread, c.Volume);
}
