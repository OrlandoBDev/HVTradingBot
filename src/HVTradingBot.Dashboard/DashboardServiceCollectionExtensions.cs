using Microsoft.Extensions.DependencyInjection;

namespace HVTradingBot.Dashboard;

public static class DashboardServiceCollectionExtensions
{
    /// <summary>Dashboard reads, actions and backtests (needs AddTradingCore).</summary>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.AddSingleton<DashboardQueries>();
        services.AddSingleton<DashboardActions>();
        services.AddSingleton<SignalDashboard>();
        services.AddSingleton<EvaluationService>();
        services.AddSingleton<Mt5SettingsActions>();
        services.AddSingleton<NewsQueries>();
        services.AddSingleton<BacktestService>();
        return services;
    }
}
