using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<SetupOutcomeEntity> SetupOutcomes => Set<SetupOutcomeEntity>();
    public DbSet<MarketSelectionEntity> MarketSelection => Set<MarketSelectionEntity>();
    public DbSet<BrokerConnectionStatusEntity> BrokerConnectionStatus => Set<BrokerConnectionStatusEntity>();
    public DbSet<SystemStateEntity> SystemState => Set<SystemStateEntity>();
    public DbSet<MarketSnapshotEntity> MarketSnapshots => Set<MarketSnapshotEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<BacktestRunEntity> BacktestRuns => Set<BacktestRunEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Prices need up to 5 decimals; money needs 2. 28,10 covers both without losing precision.
        configurationBuilder.Properties<decimal>().HavePrecision(28, 10);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
            e.Property(x => x.Details).HasColumnType("jsonb");
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
            e.Property(x => x.Version).IsRowVersion();
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
        });

        modelBuilder.Entity<RiskSettingsEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Limits).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MarketEntity>(e =>
        {
            e.HasKey(x => x.BrokerSymbol);
            e.HasIndex(x => x.Symbol).IsUnique();
            e.Property(x => x.Multipliers).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MarketSelectionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Instruments).HasColumnType("jsonb");
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
            e.Property(x => x.AccountsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SystemStateEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.KillSwitchReason).HasMaxLength(1000);
        });

        modelBuilder.Entity<MarketSnapshotEntity>(e =>
        {
            e.HasKey(x => x.Instrument);
            e.Property(x => x.Indicators).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => x.TimestampUtc);
        });

        modelBuilder.Entity<BacktestRunEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Parameters).HasColumnType("jsonb");
            e.Property(x => x.Summary).HasColumnType("jsonb");
            e.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
