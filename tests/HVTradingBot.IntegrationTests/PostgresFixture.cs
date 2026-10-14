using HVTradingBot.Domain.Risk;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Brokers;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.AspNetCore.DataProtection;
using Testcontainers.PostgreSql;

namespace HVTradingBot.IntegrationTests;

/// <summary>A migrated, empty trading database shared by one test collection.</summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    /// <summary>Tables emptied between tests.</summary>
    protected static readonly string[] ResetTables =
    [
        "orders", "positions", "paper_accounts", "broker_accounts", "broker_settings", "broker_connection_status", "market_selection",
        "risk_settings", "notification_settings", "system_state", "audit_logs", "trade_decisions", "app_users", "broker_contracts", "markets",
        "signals", "signal_settings", "setup_outcomes"
    ];

    public IDbContextFactory<TradingDbContext> DbFactory { get; private set; } = null!;

    protected abstract DatabaseProvider Provider { get; }

    protected abstract Task<string> StartAsync();

    public virtual async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, Provider, await StartAsync());
        DbFactory = new PooledDbContextFactory<TradingDbContext>(options.Options);
        await using var db = await DbFactory.CreateDbContextAsync();
        await DependencyInjection.MigrateAsync(db, CancellationToken.None);
    }

    public abstract Task DisposeAsync();

    public PaperTradingBroker NewBroker() =>
        new(DbFactory, new ExecutionCostOptions(), new TradingEngineOptions(), new FixedRiskOptions(new RiskOptions()), new FixedClock(),
            NullLogger<PaperTradingBroker>.Instance);

    public abstract Task ResetAsync();

    /// <summary>Settings store with an ephemeral (in-memory) Data Protection key ring.</summary>
    public DerivSettingsStore NewSettingsStore(DerivEnvironmentCredentials? environment = null) =>
        new(DbFactory, DataProtectionProvider, environment ?? new DerivEnvironmentCredentials(null, null, null), new FixedClock());

    public IDataProtectionProvider DataProtectionProvider { get; } = new EphemeralDataProtectionProvider();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    }
}

/// <summary>Starts a disposable PostgreSQL container (requires Docker) and applies migrations.</summary>
public sealed class PostgresFixture : DatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    protected override DatabaseProvider Provider => DatabaseProvider.Postgres;

    protected override async Task<string> StartAsync()
    {
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public override async Task ResetAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var truncate = "TRUNCATE " + string.Join(", ", ResetTables) + ";";
        await db.Database.ExecuteSqlRawAsync(truncate);
    }
}

/// <summary>A SQLite database file in a temporary folder, migrated like the Android app's database.</summary>
public sealed class SqliteFixture : DatabaseFixture
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hvtradingbot-tests-" + Guid.NewGuid().ToString("N"));

    protected override DatabaseProvider Provider => DatabaseProvider.Sqlite;

    protected override Task<string> StartAsync()
    {
        Directory.CreateDirectory(_folder);
        return Task.FromResult($"Data Source={Path.Combine(_folder, "trading.db")}");
    }

    public override Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
        return Task.CompletedTask;
    }

    public override async Task ResetAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        foreach (var table in ResetTables)
        {
            var delete = "DELETE FROM " + table + ";";
            await db.Database.ExecuteSqlRawAsync(delete);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

[CollectionDefinition(Name)]
public sealed class SqliteCollection : ICollectionFixture<SqliteFixture>
{
    public const string Name = "sqlite";
}
