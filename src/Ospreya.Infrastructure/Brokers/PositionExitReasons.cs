using Ospreya.Domain.Execution;
using Ospreya.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ospreya.Infrastructure.Brokers;

/// <summary>Relabels a closed position's exit reason (the broker reports the app's own closes as manual).</summary>
internal static class PositionExitReasons
{
    public static async Task SetAsync(IDbContextFactory<TradingDbContext> dbFactory, Guid positionId, ExitReason reason,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Positions.Where(p => p.Id == positionId && !p.IsOpen)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.ExitReason, reason.ToString()), cancellationToken);
    }
}
