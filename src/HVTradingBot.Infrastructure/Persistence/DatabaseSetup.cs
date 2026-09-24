using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public const string ConnectionStringName = "TradingDb";

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention();
}
