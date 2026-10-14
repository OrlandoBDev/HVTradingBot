using HVTradingBot.Domain.Execution;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers;

/// <summary>
/// The orders table as an idempotency record for live brokers: a signal's client order id is claimed once before
/// anything is sent. A previous attempt that was rejected before anything was traded may be retried; a filled,
/// submitting or unknown order never is. (The same rules as <see cref="Deriv.DerivBroker"/>.)
/// </summary>
internal static class OrderLedger
{
    /// <summary>Claims the client order id for <paramref name="entity"/>; returns the duplicate result when it may not be sent.</summary>
    public static async Task<OrderResult?> TryInsertAsync(IDbContextFactory<TradingDbContext> dbFactory, OrderEntity entity, ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var clientOrderId = entity.ClientOrderId;
        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
        if (existing is null)
        {
            db.Orders.Add(entity);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateException ex) when (DatabaseSetup.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                existing = await db.Orders.AsNoTracking().SingleAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
            }
        }

        if (existing.Status == nameof(OrderStatus.Rejected))
        {
            var rejected = nameof(OrderStatus.Rejected);
            var claimed = await db.Orders
                .Where(o => o.Id == existing.Id && o.Status == rejected)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(o => o.Status, "Submitting")
                    .SetProperty(o => o.RejectReason, (string?)null)
                    .SetProperty(o => o.Broker, entity.Broker)
                    .SetProperty(o => o.BrokerAccountId, entity.BrokerAccountId)
                    .SetProperty(o => o.Units, entity.Units)
                    .SetProperty(o => o.RequestedPrice, entity.RequestedPrice)
                    .SetProperty(o => o.StopLoss, entity.StopLoss)
                    .SetProperty(o => o.TakeProfit, entity.TakeProfit)
                    .SetProperty(o => o.DecisionId, entity.DecisionId)
                    .SetProperty(o => o.CorrelationId, entity.CorrelationId)
                    .SetProperty(o => o.RecordedAtUtc, entity.RecordedAtUtc), cancellationToken);
            if (claimed == 1)
            {
                entity.Id = existing.Id;
                logger.LogInformation("Retrying previously rejected order {ClientOrderId}", clientOrderId);
                return null;
            }
        }

        logger.LogWarning("Duplicate submission blocked for {ClientOrderId} (existing status {Status})", clientOrderId, existing.Status);
        return new OrderResult(clientOrderId, OrderStatus.Duplicate, existing.Id, null, existing.FillPrice,
            $"Signal already submitted (status {existing.Status}); not sent again.");
    }

    public static async Task UpdateAsync(IDbContextFactory<TradingDbContext> dbFactory, Guid id, Action<OrderEntity> change,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.SingleAsync(o => o.Id == id, cancellationToken);
        change(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
