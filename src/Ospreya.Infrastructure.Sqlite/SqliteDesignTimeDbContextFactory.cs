using Ospreya.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ospreya.Infrastructure.Sqlite;

/// <summary>
/// Used only by `dotnet ef` to generate the SQLite migrations:
/// dotnet ef migrations add Name -p src/Ospreya.Infrastructure.Sqlite -s src/Ospreya.Infrastructure.Sqlite -o Migrations
/// </summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TradingDbContext>
{
    public TradingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, DatabaseProvider.Sqlite, "Data Source=design_time.db");
        return new TradingDbContext(options.Options);
    }
}
