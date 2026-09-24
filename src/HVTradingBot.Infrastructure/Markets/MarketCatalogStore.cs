using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Markets;

public sealed record MarketSelection(
    IReadOnlyList<string>? Instruments,
    int Version,
    int AppliedVersion,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    bool DerivedOnlyWhenForexClosed = true);

/// <summary>
/// The broker's market catalog and the user's market selection, both in PostgreSQL. Every process calls
/// <see cref="LoadAndRegisterAsync"/> so that instruments from the catalog resolve by symbol.
/// </summary>
public sealed class MarketCatalogStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    public const int MaxSelectedInstruments = 30;
    private const int SelectionId = 1;

    public async Task<IReadOnlyList<MarketEntity>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Markets.AsNoTracking().OrderBy(m => m.AssetClass).ThenBy(m => m.Symbol).ToListAsync(cancellationToken);
    }

    public async Task<int> LoadAndRegisterAsync(CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        foreach (var market in catalog)
        {
            Instruments.Register(ToInstrument(market));
        }

        return catalog.Count;
    }

    /// <summary>Replaces the catalog with the broker's current list and registers every instrument.</summary>
    public async Task SaveCatalogAsync(IReadOnlyList<MarketEntity> markets, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Markets.ToDictionaryAsync(m => m.BrokerSymbol, cancellationToken);
        foreach (var market in markets)
        {
            market.UpdatedAtUtc = clock.UtcNow;
            if (existing.Remove(market.BrokerSymbol, out var row))
            {
                db.Entry(row).CurrentValues.SetValues(market);
            }
            else
            {
                db.Markets.Add(market);
            }
        }

        // Markets the broker no longer lists stay in the catalog but can no longer be traded.
        foreach (var gone in existing.Values)
        {
            gone.IsTradable = false;
            gone.IsOpen = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var market in markets)
        {
            Instruments.Register(ToInstrument(market));
        }
    }

    public async Task<MarketSelection> GetSelectionAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.MarketSelection.AsNoTracking().SingleOrDefaultAsync(s => s.Id == SelectionId, cancellationToken);
        return row is null
            ? new MarketSelection(null, 0, 0, null, null)
            : new MarketSelection(JsonSerializer.Deserialize<List<string>>(row.Instruments), row.Version, row.AppliedVersion, row.UpdatedAtUtc,
                row.UpdatedBy, row.DerivedOnlyWhenForexClosed);
    }

    public async Task<MarketSelection> SaveSelectionAsync(IReadOnlyList<string> symbols, string actor, CancellationToken cancellationToken,
        bool derivedOnlyWhenForexClosed = true)
    {
        var distinct = symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0 || distinct.Count > MaxSelectedInstruments)
        {
            throw new ArgumentException($"Select between 1 and {MaxSelectedInstruments} markets.", nameof(symbols));
        }

        var unknown = distinct.Where(s => !Instruments.TryGet(s, out _)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown market(s): {string.Join(", ", unknown)}.", nameof(symbols));
        }

        var canonical = distinct.Select(s => Instruments.Get(s).Symbol).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.MarketSelection.SingleOrDefaultAsync(s => s.Id == SelectionId, cancellationToken);
        if (row is null)
        {
            row = new MarketSelectionEntity { Id = SelectionId, Instruments = "[]" };
            db.MarketSelection.Add(row);
        }

        row.Instruments = JsonSerializer.Serialize(canonical);
        row.DerivedOnlyWhenForexClosed = derivedOnlyWhenForexClosed;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return await GetSelectionAsync(cancellationToken);
    }

    public async Task MarkAppliedAsync(int version, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.MarketSelection.SingleOrDefaultAsync(s => s.Id == SelectionId, cancellationToken);
        if (row is not null && row.AppliedVersion != version)
        {
            row.AppliedVersion = version;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static Instrument ToInstrument(MarketEntity m)
    {
        var assetClass = Enum.Parse<AssetClass>(m.AssetClass);
        var instrument = assetClass == AssetClass.Forex
            ? Instrument.CurrencyPair(m.BaseCurrency, m.QuoteCurrency)
            : new Instrument(m.Symbol, m.BaseCurrency, m.QuoteCurrency, m.PipSize, m.PriceDecimals) { AssetClass = assetClass };
        return instrument with { Name = m.Name, BrokerSymbol = m.BrokerSymbol, IsTradable = m.IsTradable };
    }
}
