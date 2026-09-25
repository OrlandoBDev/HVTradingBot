using HVTradingBot.Domain.Risk;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Infrastructure.Brokers;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.AspNetCore.DataProtection;
using Testcontainers.PostgreSql;

namespace HVTradingBot.IntegrationTests;

/// <summary>Starts a disposable PostgreSQL container (requires Docker) and applies migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public IDbContextFactory<TradingDbContext> DbFactory { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, _container.GetConnectionString());
        DbFactory = new PooledDbContextFactory<TradingDbContext>(options.Options);
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public PaperTradingBroker NewBroker() =>
        new(DbFactory, new ExecutionCostOptions(), new TradingEngineOptions(), new FixedRiskOptions(new RiskOptions()), new FixedClock(),
            NullLogger<PaperTradingBroker>.Instance);

    public async Task ResetAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE orders, positions, paper_accounts, broker_accounts, broker_settings, broker_connection_status, market_selection, risk_settings, notification_settings, system_state, audit_logs, trade_decisions, app_users;");
    }

    /// <summary>Settings store with an ephemeral (in-memory) Data Protection key ring.</summary>
    public DerivSettingsStore NewSettingsStore(DerivEnvironmentCredentials? environment = null) =>
        new(DbFactory, DataProtectionProvider, environment ?? new DerivEnvironmentCredentials(null, null, null), new FixedClock());

    public IDataProtectionProvider DataProtectionProvider { get; } = new EphemeralDataProtectionProvider();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
