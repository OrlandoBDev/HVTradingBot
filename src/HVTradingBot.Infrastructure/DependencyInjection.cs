using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Brokers;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HVTradingBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TradingDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:TradingDatabase is required.");

        services.AddDbContext<TradingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<PaperTradingBroker>();
        services.AddSingleton<IBroker>(sp => sp.GetRequiredService<PaperTradingBroker>());

        return services;
    }
}
