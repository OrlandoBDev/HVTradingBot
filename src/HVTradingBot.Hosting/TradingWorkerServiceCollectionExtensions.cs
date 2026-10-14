using HVTradingBot.Infrastructure.Persistence;

namespace HVTradingBot.Hosting;

public static class TradingWorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the trading loop and its companions (heartbeat and settings watcher, test trades, close requests).
    /// <paramref name="instanceLock"/> guarantees a single trading loop per database.
    /// </summary>
    public static IServiceCollection AddTradingWorker(this IServiceCollection services, Func<IServiceProvider, IWorkerInstanceLock> instanceLock)
    {
        services.AddSingleton(instanceLock);
        services.AddHostedService<TradingWorker>();
        services.AddHostedService<BrokerSettingsWatcher>();
        services.AddHostedService<TestTradeService>();
        services.AddHostedService<ClosePositionService>();
        services.AddHostedService<BrokerAccountSyncService>();
        services.AddHostedService<SignalService>();
        return services;
    }
}
