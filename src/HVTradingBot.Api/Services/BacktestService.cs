using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Api.Services;

public sealed class BacktestService(
    BacktestEngine engine,
    IDbContextFactory<TradingDbContext> dbFactory,
    SimulatedMarketOptions simulatedOptions,
    TradingEngineOptions engineOptions,
    MarketCatalogStore catalog,
    IClock clock)
{
    public const int MaxDays = 365;

    public async Task<(BacktestRunDto? Run, string? Error)> RunAsync(BacktestRequest request, CancellationToken cancellationToken)
    {
        if (request.Days is < 20 or > MaxDays)
        {
            return (null, $"Days must be between 20 and {MaxDays} (indicators need warm-up history).");
        }

        var source = request.Source.ToLowerInvariant();
        IReadOnlyList<Instrument> instruments;
        try
        {
            // Default: the markets selected on the Settings page (or the configured defaults).
            var selection = await catalog.GetSelectionAsync(cancellationToken);
            var symbols = request.Instruments is { Count: > 0 } requested ? requested : selection.Instruments ?? engineOptions.Instruments;
            instruments = symbols.Select(Instruments.Get).Distinct().ToList();
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }

        if (source == "simulated")
        {
            var unsupported = instruments.Where(i => !MarketSeriesGenerator.Supports(i)).Select(i => i.DisplayName).ToList();
            if (unsupported.Count == instruments.Count)
            {
                return (null, "None of these markets can be simulated offline; use source 'stored' (real Deriv candles).");
            }

            instruments = instruments.Where(MarketSeriesGenerator.Supports).ToList();
        }

        // P&L conversion needs prices for each traded currency against the account currency.
        instruments = instruments.Concat(Instruments.ConversionPairs(instruments, engineOptions.AccountCurrency)).Distinct().ToList();

        var seed = request.Seed ?? simulatedOptions.Seed;
        var endDate = request.EndDate ?? DateOnly.FromDateTime(clock.UtcNow);
        var end = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        Dictionary<Instrument, IReadOnlyList<Candle>> bars;
        switch (source)
        {
            case "simulated":
                bars = instruments.ToDictionary(i => i, i => MarketSeriesGenerator.Generate(i, seed, end, request.Days));
                break;
            case "stored":
                bars = await LoadStoredAsync(instruments, request.Days, cancellationToken);
                if (bars.Values.All(b => b.Count == 0))
                {
                    return (null, "No stored candles. Start the worker first or use source 'simulated'.");
                }

                break;
            default:
                return (null, "Source must be 'simulated' or 'stored'.");
        }

        var result = await Task.Run(() => engine.RunAsync(bars, cancellationToken), cancellationToken);

        var parameters = JsonSerializer.Serialize(new
        {
            Source = source,
            request.Days,
            Seed = source == "simulated" ? seed : (int?)null,
            EndDate = source == "simulated" ? endDate : (DateOnly?)null,
            Instruments = instruments.Select(i => i.Symbol)
        }, JsonDefaults.Options);
        var summary = JsonSerializer.Serialize(new
        {
            result.FromUtc,
            result.ToUtc,
            result.BarsProcessed,
            result.StartingBalance,
            result.EndingBalance,
            result.Metrics,
            result.ByStrategy,
            result.DecisionCounts,
            result.Costs,
            Trades = result.Trades.OrderByDescending(t => t.ClosedAtUtc).Take(200)
        }, JsonDefaults.Options);

        var entity = new BacktestRunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = clock.UtcNow,
            Source = source,
            Parameters = parameters,
            Summary = summary,
            TotalTrades = result.Metrics.TotalTrades,
            NetPnl = result.Metrics.NetPnl
        };

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.BacktestRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (ToDto(entity), null);
    }

    public async Task<IReadOnlyList<BacktestRunDto>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.BacktestRuns.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    private async Task<Dictionary<Instrument, IReadOnlyList<Candle>>> LoadStoredAsync(
        IReadOnlyList<Instrument> instruments, int days, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var result = new Dictionary<Instrument, IReadOnlyList<Candle>>();
        foreach (var instrument in instruments)
        {
            var rows = await db.Candles.AsNoTracking()
                .Where(c => c.Instrument == instrument.Symbol && c.TimeFrame == nameof(TimeFrame.M5))
                .OrderByDescending(c => c.OpenTimeUtc)
                .Take(days * 288)
                .ToListAsync(cancellationToken);
            result[instrument] = rows.OrderBy(c => c.OpenTimeUtc).Select(SimulatedMarketDataFeed.ToCandle).ToList();
        }

        return result;
    }

    private static BacktestRunDto ToDto(BacktestRunEntity e) => new(e.Id, e.CreatedAtUtc, e.Source, e.TotalTrades, e.NetPnl,
        JsonDocument.Parse(e.Parameters).RootElement.Clone(), JsonDocument.Parse(e.Summary).RootElement.Clone());
}
