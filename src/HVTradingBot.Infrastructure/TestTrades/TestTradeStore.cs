using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.TestTrades;

/// <summary>Lifecycle of a dashboard test trade.</summary>
public static class TestTradeStatus
{
    public const string Pending = "Pending";   // requested, waiting for the worker
    public const string Opening = "Opening";   // worker is placing the order
    public const string Open = "Open";         // filled; held for HoldSeconds
    public const string Closing = "Closing";   // close sent to the broker; waiting for it to be recorded
    public const string Closed = "Closed";     // closed and recorded with its result
    public const string Failed = "Failed";     // not placed (risk rule, broker, market closed…) or could not be closed

    public static readonly string[] Active = [Pending, Opening, Open, Closing];
}

/// <summary>Test-trade requests: the API creates them, the worker carries them out (the API never talks to a broker).</summary>
public sealed class TestTradeStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    public const int DefaultHoldSeconds = 60;

    public async Task<TestTradeEntity?> CreateAsync(string instrument, string requestedBy, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.TestTrades.AnyAsync(t => TestTradeStatus.Active.Contains(t.Status), cancellationToken))
        {
            return null; // one at a time
        }

        var entity = new TestTradeEntity
        {
            Id = Guid.NewGuid(),
            Instrument = instrument,
            Status = TestTradeStatus.Pending,
            Message = "Waiting for the trading worker…",
            RequestedBy = requestedBy,
            RequestedAtUtc = clock.UtcNow,
            HoldSeconds = DefaultHoldSeconds
        };
        db.TestTrades.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<TestTradeEntity>> RecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TestTrades.AsNoTracking().OrderByDescending(t => t.RequestedAtUtc).Take(count).ToListAsync(cancellationToken);
    }

    public async Task<TestTradeEntity?> NextActiveAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TestTrades.AsNoTracking().Where(t => TestTradeStatus.Active.Contains(t.Status))
            .OrderBy(t => t.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(Guid id, Action<TestTradeEntity> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.TestTrades.SingleAsync(t => t.Id == id, cancellationToken);
        change(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The recorded position, once the broker close has been reconciled.</summary>
    public async Task<PositionEntity?> PositionAsync(Guid positionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == positionId, cancellationToken);
    }
}
