using HVTradingBot.Application.Learning;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence.Stores;

public sealed class EfVirtualTradeStore(IDbContextFactory<TradingDbContext> dbFactory) : IVirtualTradeStore
{
    private const string Open = "Open";

    public async Task AddAsync(IReadOnlyList<VirtualSetup> setups, CancellationToken cancellationToken)
    {
        if (setups.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = setups.Select(s => s.SetupId).ToList();
        var existing = (await db.SetupOutcomes.Where(o => ids.Contains(o.SetupId)).Select(o => o.SetupId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var s in setups.Where(s => !existing.Contains(s.SetupId)).DistinctBy(s => s.SetupId))
        {
            db.SetupOutcomes.Add(new SetupOutcomeEntity
            {
                Id = s.Id,
                SetupId = s.SetupId,
                DecisionId = s.DecisionId,
                Instrument = s.Instrument,
                AssetClass = s.Key.AssetClass.ToString(),
                Strategy = s.Key.Strategy,
                Regime = s.Key.Regime.ToString(),
                Direction = s.Direction.ToString(),
                DecisionState = s.DecisionState.ToString(),
                Score = s.Score,
                Entry = s.Entry,
                StopLoss = s.StopLoss,
                TakeProfit = s.TakeProfit,
                OpenedAtUtc = s.OpenedAtUtc,
                Status = Open
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent evaluation recorded the same setup first; the unique index keeps one.
        }
    }

    public async Task<IReadOnlyList<VirtualSetup>> GetOpenAsync(string instrument, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SetupOutcomes.AsNoTracking().Where(o => o.Instrument == instrument && o.Status == Open).ToListAsync(cancellationToken);
        return rows.Select(o => new VirtualSetup(o.Id, o.SetupId, o.DecisionId, Key(o), o.Instrument, Enum.Parse<Direction>(o.Direction), o.Score,
            o.Entry, o.StopLoss, o.TakeProfit, DateTime.SpecifyKind(o.OpenedAtUtc, DateTimeKind.Utc), Enum.Parse<DecisionState>(o.DecisionState))).ToList();
    }

    public async Task ResolveAsync(Guid id, VirtualOutcome outcome, decimal rMultiple, decimal exitPrice, DateTime closedAtUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.SetupOutcomes.Where(o => o.Id == id && o.Status == Open).ExecuteUpdateAsync(u => u
            .SetProperty(o => o.Status, outcome.ToString())
            .SetProperty(o => o.RMultiple, rMultiple)
            .SetProperty(o => o.ExitPrice, exitPrice)
            .SetProperty(o => o.ClosedAtUtc, closedAtUtc), cancellationToken);
    }

    public async Task<IReadOnlyList<SetupOutcome>> GetOutcomesAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SetupOutcomes.AsNoTracking()
            .Where(o => o.Status != Open && o.ClosedAtUtc >= sinceUtc)
            .Select(o => new { o.Strategy, o.Regime, o.AssetClass, o.RMultiple, o.ClosedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(o => new SetupOutcome(
            new SetupKey(o.Strategy, Enum.Parse<MarketRegime>(o.Regime), Enum.Parse<AssetClass>(o.AssetClass)),
            o.RMultiple ?? 0,
            DateTime.SpecifyKind(o.ClosedAtUtc!.Value, DateTimeKind.Utc))).ToList();
    }

    private static SetupKey Key(SetupOutcomeEntity o) =>
        new(o.Strategy, Enum.Parse<MarketRegime>(o.Regime), Enum.Parse<AssetClass>(o.AssetClass));
}
