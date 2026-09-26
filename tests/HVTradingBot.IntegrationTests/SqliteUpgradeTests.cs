using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HVTradingBot.IntegrationTests;

/// <summary>
/// Installing a new version of the Android app upgrades the phone's database in place. Every migration since the first
/// release must keep the existing trades, settings and learning data.
/// </summary>
public sealed class SqliteUpgradeTests : IDisposable
{
    private const string FirstRelease = "20260926143935_InitialCreate";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hvtradingbot-upgrade-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Upgrading_a_first_release_database_keeps_its_data()
    {
        Directory.CreateDirectory(_folder);
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, DatabaseProvider.Sqlite, $"Data Source={Path.Combine(_folder, "trading.db")}");

        // The database as the first app version left it, with data in it.
        await using (var db = new TradingDbContext(options.Options))
        {
            await db.GetService<IMigrator>().MigrateAsync(FirstRelease);
            var position = new PositionEntity
            {
                Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Broker = "Deriv", BrokerAccountId = "VRTC1", BrokerContractId = "77",
                ClientOrderId = "EURUSD-1", Instrument = "EUR/USD", Direction = "Long", Units = 1000, EntryPrice = 1.1m, StopLoss = 1.09m,
                TakeProfit = 1.12m, InitialRiskAmount = 10, OpenedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                Strategy = "Trend Following", Score = 80, IsOpen = false, RealizedPnl = 12.34m,
                ClosedAtUtc = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc)
            };
            db.Positions.Add(position);
            db.AuditLogs.Add(new AuditLogEntity { TimestampUtc = DateTime.UtcNow, Actor = "android-app", Action = "KillSwitchActivated", Details = "test" });
            await db.SaveChangesAsync();
        }

        // The new version starts: it applies every newer migration.
        await using (var db = new TradingDbContext(options.Options))
        {
            await DependencyInjection.MigrateAsync(db, CancellationToken.None);

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var kept = await db.Positions.SingleAsync();
            Assert.Equal((12.34m, "77"), (kept.RealizedPnl, kept.BrokerContractId));
            Assert.Equal(1, await db.AuditLogs.CountAsync());
            Assert.Equal(0, await db.BrokerContracts.CountAsync()); // new table, empty until the first sync
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
