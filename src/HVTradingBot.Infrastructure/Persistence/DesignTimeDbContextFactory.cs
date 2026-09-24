using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HVTradingBot.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to generate migrations; no database connection is opened.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TradingDbContext>
{
    public TradingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>();
        DatabaseSetup.Configure(options, "Host=localhost;Database=design_time");
        return new TradingDbContext(options.Options);
    }
}
