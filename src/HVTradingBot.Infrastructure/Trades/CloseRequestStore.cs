using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Trades;

public static class CloseRequestStatus
{
    public const string Pending = "Pending";   // requested, waiting for the worker
    public const string Closing = "Closing";   // sent to the broker, waiting for the result to be recorded
    public const string Closed = "Closed";     // closed and recorded
    public const string Failed = "Failed";     // refused or not confirmed; the position keeps its stop loss and take profit

    public static readonly string[] Active = [Pending, Closing];
}

/// <summary>Close requests: the API creates them, the worker (the only process that talks to the broker) carries them out.</summary>
public sealed class CloseRequestStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    public async Task<(CloseRequestEntity? Request, string? Problem)> CreateAsync(Guid positionId, string requestedBy, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        if (position is null || !position.IsOpen)
        {
            return (null, "This position is no longer open.");
        }

        if (await db.CloseRequests.AnyAsync(r => r.PositionId == positionId && CloseRequestStatus.Active.Contains(r.Status), cancellationToken))
        {
            return (null, "A close for this position is already in progress.");
        }

        var request = new CloseRequestEntity
        {
            Id = Guid.NewGuid(),
            PositionId = positionId,
            Instrument = position.Instrument,
            Status = CloseRequestStatus.Pending,
            Message = "Waiting for the trading worker…",
            RequestedBy = requestedBy,
            RequestedAtUtc = clock.UtcNow
        };
        db.CloseRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return (request, null);
    }

    public async Task<IReadOnlyList<CloseRequestEntity>> ActiveAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CloseRequests.AsNoTracking().Where(r => CloseRequestStatus.Active.Contains(r.Status))
            .OrderBy(r => r.RequestedAtUtc).ToListAsync(cancellationToken);
    }

    /// <summary>Latest request per position (for showing "closing…" or a failure next to open positions).</summary>
    public async Task<IReadOnlyDictionary<Guid, CloseRequestEntity>> LatestForAsync(IReadOnlyCollection<Guid> positionIds, CancellationToken cancellationToken)
    {
        if (positionIds.Count == 0)
        {
            return new Dictionary<Guid, CloseRequestEntity>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.CloseRequests.AsNoTracking().Where(r => positionIds.Contains(r.PositionId)).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.PositionId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RequestedAtUtc).First());
    }

    public async Task UpdateAsync(Guid id, Action<CloseRequestEntity> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CloseRequests.SingleAsync(r => r.Id == id, cancellationToken);
        change(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PositionEntity?> PositionAsync(Guid positionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == positionId, cancellationToken);
    }
}
