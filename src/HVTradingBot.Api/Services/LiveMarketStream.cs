using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Api.Services;

/// <summary>New closed bars and changed quotes since the previous call.</summary>
public sealed record LiveMarketUpdate(IReadOnlyList<LiveBarDto> Bars, IReadOnlyList<LiveTickDto> Ticks);

/// <summary>
/// Remembers what has already been pushed to dashboards and returns only what is new. The Deriv tick subscription lives
/// in the worker (another process), so this reads what the worker stores: the 5-minute candles it saves after each bar
/// closes and the latest quote it writes to the market snapshot every few seconds. Read-only.
/// </summary>
public sealed class LiveMarketStream(
    IDbContextFactory<TradingDbContext> dbFactory,
    MarketCatalogStore catalog,
    TradingEngineOptions engineOptions)
{
    /// <summary>Upper bound on bars pushed in one broadcast, e.g. after the worker back-fills a gap.</summary>
    public const int MaxBarsPerUpdate = 500;

    private static readonly string BarTimeFrame = nameof(TimeFrame.M5);

    private readonly Dictionary<string, DateTime> _lastBarOpen = new();
    private readonly Dictionary<string, (DateTime Time, decimal Bid, decimal Ask)> _lastTick = new();

    public async Task<LiveMarketUpdate> NextAsync(CancellationToken cancellationToken)
    {
        var selection = await catalog.GetSelectionAsync(cancellationToken);
        var selected = (selection.Instruments ?? engineOptions.Instruments).ToList();

        // Forget deselected markets so re-selecting one later starts fresh instead of replaying the gap.
        foreach (var symbol in _lastBarOpen.Keys.Concat(_lastTick.Keys).Except(selected).ToList())
        {
            _lastBarOpen.Remove(symbol);
            _lastTick.Remove(symbol);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return new LiveMarketUpdate(await NewBarsAsync(db, selected, cancellationToken), await ChangedTicksAsync(db, selected, cancellationToken));
    }

    private async Task<IReadOnlyList<LiveBarDto>> NewBarsAsync(TradingDbContext db, List<string> selected, CancellationToken cancellationToken)
    {
        // A market seen for the first time starts after its newest stored bar: its history (weeks of candles loaded at
        // worker start-up) is not pushed, only bars that close from now on.
        var unseen = selected.Where(s => !_lastBarOpen.ContainsKey(s)).ToList();
        if (unseen.Count > 0)
        {
            var newest = await db.Candles.AsNoTracking()
                .Where(c => unseen.Contains(c.Instrument) && c.TimeFrame == BarTimeFrame)
                .GroupBy(c => c.Instrument)
                .Select(g => new { Instrument = g.Key, OpenTimeUtc = g.Max(c => c.OpenTimeUtc) })
                .ToListAsync(cancellationToken);
            foreach (var n in newest)
            {
                _lastBarOpen[n.Instrument] = n.OpenTimeUtc;
            }
        }

        var tracked = selected.Where(_lastBarOpen.ContainsKey).ToList();
        if (tracked.Count == 0)
        {
            return [];
        }

        var since = tracked.Min(s => _lastBarOpen[s]);
        var rows = await db.Candles.AsNoTracking()
            .Where(c => tracked.Contains(c.Instrument) && c.TimeFrame == BarTimeFrame && c.OpenTimeUtc > since)
            .OrderBy(c => c.OpenTimeUtc).ThenBy(c => c.Instrument)
            .ToListAsync(cancellationToken);

        var bars = rows.Where(c => c.OpenTimeUtc > _lastBarOpen[c.Instrument]).Take(MaxBarsPerUpdate).ToList();
        foreach (var bar in bars)
        {
            _lastBarOpen[bar.Instrument] = bar.OpenTimeUtc;
        }

        return bars.Select(c => new LiveBarDto(c.Instrument, c.TimeFrame, c.OpenTimeUtc, c.Open, c.High, c.Low, c.Close, c.Volume)).ToList();
    }

    private async Task<IReadOnlyList<LiveTickDto>> ChangedTicksAsync(TradingDbContext db, List<string> selected, CancellationToken cancellationToken)
    {
        var rows = await db.MarketSnapshots.AsNoTracking()
            .Where(s => selected.Contains(s.Instrument))
            .OrderBy(s => s.Instrument)
            .ToListAsync(cancellationToken);

        var ticks = new List<LiveTickDto>();
        foreach (var s in rows)
        {
            var quote = (s.MarketTimeUtc, s.Bid, s.Ask);
            if (_lastTick.TryGetValue(s.Instrument, out var last) && last == quote)
            {
                continue;
            }

            _lastTick[s.Instrument] = quote;
            ticks.Add(new LiveTickDto(s.Instrument, s.MarketTimeUtc, s.Bid, s.Ask, s.SpreadPips));
        }

        return ticks;
    }
}
