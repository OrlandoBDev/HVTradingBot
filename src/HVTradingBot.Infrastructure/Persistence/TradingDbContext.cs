using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence;

public sealed class TradingDbContext(DbContextOptions<TradingDbContext> options) : DbContext(options)
{
    public DbSet<MarketCandleEntity> MarketCandles => Set<MarketCandleEntity>();
    public DbSet<TradeDecisionEntity> TradeDecisions => Set<TradeDecisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketCandleEntity>(e =>
        {
            e.ToTable("market_candles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Instrument).HasMaxLength(32).IsRequired();
            e.Property(x => x.TimeFrame).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.Instrument, x.TimeFrame, x.OpenTimeUtc }).IsUnique();
        });

        modelBuilder.Entity<TradeDecisionEntity>(e =>
        {
            e.ToTable("trade_decisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Instrument).HasMaxLength(32).IsRequired();
            e.Property(x => x.Strategy).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.Instrument, x.CreatedAtUtc });
        });
    }
}
