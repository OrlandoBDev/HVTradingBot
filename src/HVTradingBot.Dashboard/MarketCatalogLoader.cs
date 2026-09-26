using HVTradingBot.Infrastructure.Markets;

namespace HVTradingBot.Dashboard;

/// <summary>Keeps the API's instrument registry in sync with the market catalog the worker discovers.</summary>
public sealed class MarketCatalogLoader(MarketCatalogStore catalog, ILogger<MarketCatalogLoader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try
            {
                await catalog.LoadAndRegisterAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Loading the market catalog failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
