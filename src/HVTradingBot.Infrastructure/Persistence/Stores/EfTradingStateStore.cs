using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence.Stores;

/// <summary>
/// Single-row system state shared by the worker and the API. Updates take a row lock (SELECT ... FOR UPDATE)
/// inside a transaction, so concurrent changes (e.g. a kill-switch toggle during a trading cycle) are serialized
/// and never overwrite each other.
/// </summary>
public sealed class EfTradingStateStore(IDbContextFactory<TradingDbContext> dbFactory) : ITradingStateStore
{
    private const int StateId = 1;

    public async Task<TradingSystemState> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SystemState.AsNoTracking().SingleOrDefaultAsync(s => s.Id == StateId, cancellationToken);
        return entity is null ? new TradingSystemState() : ToModel(entity);
    }

    public async Task<TradingSystemState> UpdateAsync(Func<TradingSystemState, TradingSystemState> update, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO system_state (id, mode, kill_switch_active, consecutive_losses, daily_realized_pnl, weekly_realized_pnl) VALUES ({StateId}, {nameof(TradingMode.Paper)}, false, 0, 0, 0) ON CONFLICT (id) DO NOTHING",
                ct);
            var entity = await db.SystemState
                .FromSql($"SELECT *, xmin FROM system_state WHERE id = {StateId} FOR UPDATE")
                .SingleAsync(ct);

            var updated = update(ToModel(entity));
            Apply(updated, entity);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return updated;
        }, cancellationToken);
    }

    private static TradingSystemState ToModel(SystemStateEntity e) => new()
    {
        Mode = Enum.Parse<TradingMode>(e.Mode),
        KillSwitchActive = e.KillSwitchActive,
        KillSwitchReason = e.KillSwitchReason,
        KillSwitchChangedUtc = e.KillSwitchChangedUtc,
        ConsecutiveLosses = e.ConsecutiveLosses,
        CooldownUntilUtc = e.CooldownUntilUtc,
        PnlDay = e.PnlDay,
        DailyRealizedPnl = e.DailyRealizedPnl,
        PnlWeekStart = e.PnlWeekStart,
        WeeklyRealizedPnl = e.WeeklyRealizedPnl,
        LastBarTimeUtc = e.LastBarTimeUtc,
        LastDataReceivedUtc = e.LastDataReceivedUtc,
        WorkerHeartbeatUtc = e.WorkerHeartbeatUtc,
        BrokerName = e.BrokerName,
        BrokerAccountId = e.BrokerAccountId,
        BrokerIsDemo = e.BrokerIsDemo,
        MarketDataSource = e.MarketDataSource
    };

    private static void Apply(TradingSystemState s, SystemStateEntity e)
    {
        e.Mode = s.Mode.ToString();
        e.KillSwitchActive = s.KillSwitchActive;
        e.KillSwitchReason = s.KillSwitchReason;
        e.KillSwitchChangedUtc = s.KillSwitchChangedUtc;
        e.ConsecutiveLosses = s.ConsecutiveLosses;
        e.CooldownUntilUtc = s.CooldownUntilUtc;
        e.PnlDay = s.PnlDay;
        e.DailyRealizedPnl = s.DailyRealizedPnl;
        e.PnlWeekStart = s.PnlWeekStart;
        e.WeeklyRealizedPnl = s.WeeklyRealizedPnl;
        e.LastBarTimeUtc = s.LastBarTimeUtc;
        e.LastDataReceivedUtc = s.LastDataReceivedUtc;
        e.WorkerHeartbeatUtc = s.WorkerHeartbeatUtc;
        e.BrokerName = s.BrokerName;
        e.BrokerAccountId = s.BrokerAccountId;
        e.BrokerIsDemo = s.BrokerIsDemo;
        e.MarketDataSource = s.MarketDataSource;
    }
}
