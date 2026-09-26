using Microsoft.Extensions.DependencyInjection;

namespace HVTradingBot.Dashboard;

public static class DashboardServiceCollectionExtensions
{
    /// <summary>Dashboard reads, actions and backtests (needs AddTradingCore).</summary>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.AddSingleton<DashboardQueries>();
        services.AddSingleton<DashboardActions>();
        services.AddSingleton<NewsQueries>();
        services.AddSingleton<BacktestService>();
        return services;
    }
}
