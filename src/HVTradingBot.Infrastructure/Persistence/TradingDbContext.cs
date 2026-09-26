using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HVTradingBot.Infrastructure.Persistence;

public sealed class TradingDbContext(DbContextOptions<TradingDbContext> options) : DbContext(options)
{
    public DbSet<CandleEntity> Candles => Set<CandleEntity>();
    public DbSet<TradeDecisionEntity> TradeDecisions => Set<TradeDecisionEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<PositionEntity> Positions => Set<PositionEntity>();
    public DbSet<PaperAccountEntity> PaperAccounts => Set<PaperAccountEntity>();
    public DbSet<BrokerAccountEntity> BrokerAccounts => Set<BrokerAccountEntity>();
    public DbSet<BrokerSettingsEntity> BrokerSettings => Set<BrokerSettingsEntity>();
    public DbSet<MarketEntity> Markets => Set<MarketEntity>();
    public DbSet<RiskSettingsEntity> RiskSettings => Set<RiskSettingsEntity>();
    public DbSet<NotificationSettingsEntity> NotificationSettings => Set<NotificationSettingsEntity>();
    public DbSet<TestTradeEntity> TestTrades => Set<TestTradeEntity>();
    public DbSet<CloseRequestEntity> CloseRequests => Set<CloseRequestEntity>();
    public DbSet<SetupOutcomeEntity> SetupOutcomes => Set<SetupOutcomeEntity>();
    public DbSet<MarketSelectionEntity> MarketSelection => Set<MarketSelectionEntity>();
    public DbSet<BrokerConnectionStatusEntity> BrokerConnectionStatus => Set<BrokerConnectionStatusEntity>();
    public DbSet<SystemStateEntity> SystemState => Set<SystemStateEntity>();
    public DbSet<MarketSnapshotEntity> MarketSnapshots => Set<MarketSnapshotEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();
    public DbSet<BacktestRunEntity> BacktestRuns => Set<BacktestRunEntity>();
    public DbSet<BrokerContractEntity> BrokerContracts => Set<BrokerContractEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Prices need up to 5 decimals; money needs 2. 28,10 covers both without losing precision.
        configurationBuilder.Properties<decimal>().HavePrecision(28, 10);

        if (IsSqlite)
        {
            // SQLite has no decimal type: EF would store text, which cannot be summed, compared or sorted in SQL. A REAL
            // keeps 15 significant digits, plenty for prices (5 decimals) and account amounts.
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
            // SQLite returns DateTime without a kind; every timestamp in this model is UTC.
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        }
    }

    private bool IsSqlite => Database.IsSqlite();

    /// <summary>JSON columns are jsonb in PostgreSQL and plain text in SQLite.</summary>
    private string? JsonColumnType => IsSqlite ? null : "jsonb";

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var json = JsonColumnType;
        modelBuilder.Entity<CandleEntity>(e =>
        {
            e.HasKey(x => new { x.Instrument, x.TimeFrame, x.OpenTimeUtc });
            e.Property(x => x.Instrument).HasMaxLength(16);
            e.Property(x => x.TimeFrame).HasMaxLength(8);
        });

        modelBuilder.Entity<TradeDecisionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reasons).HasMaxLength(4000);
            e.Property(x => x.Details).HasColumnType(json);
            e.HasIndex(x => x.MarketTimeUtc);
            e.HasIndex(x => new { x.Instrument, x.MarketTimeUtc });
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.ClientOrderId);
        });

        modelBuilder.Entity<OrderEntity>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BrokerContractId);
            // Idempotency: the database guarantees one order per client order id.
            e.HasIndex(x => x.ClientOrderId).IsUnique();
            e.Property(x => x.RejectReason).HasMaxLength(2000);
        });

        modelBuilder.Entity<PositionEntity>(e =>
        {
            e.ToTable("positions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BrokerContractId);
            e.HasIndex(x => x.ClientOrderId).IsUnique();
            e.HasIndex(x => x.IsOpen);
            e.HasIndex(x => x.ClosedAtUtc);
        });

        modelBuilder.Entity<PaperAccountEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            RowVersion(e.Property(x => x.Version));
        });

        modelBuilder.Entity<BrokerAccountEntity>(e =>
        {
            e.HasKey(x => x.AccountKey);
        });

        modelBuilder.Entity<SetupOutcomeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SetupId).IsUnique();
            e.HasIndex(x => new { x.Instrument, x.Status });
            e.HasIndex(x => x.ClosedAtUtc);
            e.Property(x => x.NewsCondition).HasMaxLength(24);
            e.Property(x => x.TrendAlignment).HasMaxLength(16);
        });

        modelBuilder.Entity<CloseRequestEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Instrument).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.PositionId, x.Status });
        });

        modelBuilder.Entity<TestTradeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Instrument).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.HasIndex(x => x.RequestedAtUtc);
        });

        modelBuilder.Entity<NotificationSettingsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SmtpHost).HasMaxLength(255);
            e.Property(x => x.Username).HasMaxLength(320);
            e.Property(x => x.PasswordProtected).HasMaxLength(4000);
            e.Property(x => x.PasswordHint).HasMaxLength(8);
            e.Property(x => x.FromAddress).HasMaxLength(320);
            e.Property(x => x.FromName).HasMaxLength(100);
            e.Property(x => x.ToAddresses).HasColumnType(json);
            e.Property(x => x.LastError).HasMaxLength(1000);
        });

        modelBuilder.Entity<RiskSettingsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Limits).HasColumnType(json);
        });

        modelBuilder.Entity<MarketEntity>(e =>
        {
            e.HasKey(x => x.BrokerSymbol);
            e.HasIndex(x => x.Symbol).IsUnique();
            e.Property(x => x.Multipliers).HasColumnType(json);
        });

        modelBuilder.Entity<MarketSelectionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Instruments).HasColumnType(json);
            e.Property(x => x.DerivedOnlyWhenForexClosed).HasDefaultValue(true);
        });

        modelBuilder.Entity<BrokerSettingsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DerivAppId).HasMaxLength(64);
            e.Property(x => x.DerivApiTokenProtected).HasMaxLength(4000);
            e.Property(x => x.DerivApiTokenHint).HasMaxLength(8);
            e.Property(x => x.DerivAccountId).HasMaxLength(32);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<BrokerConnectionStatusEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.AccountsJson).HasColumnType(json);
        });

        modelBuilder.Entity<SystemStateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            RowVersion(e.Property(x => x.Version));
            e.Property(x => x.KillSwitchReason).HasMaxLength(1000);
        });

        modelBuilder.Entity<MarketSnapshotEntity>(e =>
        {
            e.HasKey(x => x.Instrument);
            e.Property(x => x.Indicators).HasColumnType(json);
        });

        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => x.TimestampUtc);
        });

        modelBuilder.Entity<BrokerContractEntity>(e =>
        {
            e.HasKey(x => x.ContractId);
            e.Property(x => x.ContractId).HasMaxLength(32);
            e.Property(x => x.Broker).HasMaxLength(32);
            e.Property(x => x.BrokerAccountId).HasMaxLength(32);
            e.Property(x => x.Symbol).HasMaxLength(64);
            e.Property(x => x.ContractType).HasMaxLength(32);
            e.Property(x => x.Direction).HasMaxLength(8);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => new { x.BrokerAccountId, x.IsOpen });
            e.HasIndex(x => new { x.BrokerAccountId, x.SellTimeUtc });
        });

        modelBuilder.Entity<BacktestRunEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Parameters).HasColumnType(json);
            e.Property(x => x.Summary).HasColumnType(json);
            e.HasIndex(x => x.CreatedAtUtc);
        });
    }

    /// <summary>
    /// PostgreSQL maps the version to xmin, which the server bumps on every update. SQLite has no such column; the
    /// app is a single process there and serializes these rows in code, so the version is only a plain token.
    /// </summary>
    private void RowVersion(PropertyBuilder<uint> property)
    {
        if (IsSqlite)
        {
            property.IsConcurrencyToken();
        }
        else
        {
            property.IsRowVersion();
        }
    }
}
