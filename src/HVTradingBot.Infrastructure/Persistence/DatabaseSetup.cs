using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HVTradingBot.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL for the API/worker stack (Docker, native runs); SQLite for the self-contained Android app, where the
/// engine and dashboard run in one process on the phone.
/// </summary>
public enum DatabaseProvider
{
    Postgres,
    Sqlite
}

public static class DatabaseSetup
{
    public const string ConnectionStringName = "TradingDb";
    public const string ProviderKey = "Database:Provider";

    /// <summary>SQLite migrations live in their own assembly because EF Core keeps one migration history per provider.</summary>
    public const string SqliteMigrationsAssembly = "HVTradingBot.Infrastructure.Sqlite";

    public static DatabaseProvider ProviderFrom(IConfiguration configuration) =>
        Enum.TryParse<DatabaseProvider>(configuration[ProviderKey], ignoreCase: true, out var provider) ? provider : DatabaseProvider.Postgres;

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        Configure(options, DatabaseProvider.Postgres, connectionString);

    public static void Configure(DbContextOptionsBuilder options, DatabaseProvider provider, string connectionString)
    {
        if (provider == DatabaseProvider.Sqlite)
        {
            options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
        }
        else
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3));
        }

        options.UseSnakeCaseNamingConvention();
    }

    /// <summary>True when saving failed because a unique index (e.g. the client order id) already holds the value.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        // SQLITE_CONSTRAINT_UNIQUE (2067) and SQLITE_CONSTRAINT_PRIMARYKEY (1555).
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 2067 or 1555,
        _ => false
    };
}
