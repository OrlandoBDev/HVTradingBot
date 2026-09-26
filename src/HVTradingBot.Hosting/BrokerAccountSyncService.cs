using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Brokers.Deriv;

namespace HVTradingBot.Hosting;

/// <summary>
/// Keeps the dashboard's view of the whole broker account current: open contracts every 15 seconds, closed ones every
/// two minutes and right after a contract closes. Includes contracts opened outside this app. Deriv only; the paper
/// broker's trades are all the app's own.
/// </summary>
public sealed class BrokerAccountSyncService(
    IExecutionBroker broker,
    DerivAccountSync sync,
    ILogger<BrokerAccountSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan OpenInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (broker.Descriptor.Name != DerivBroker.BrokerName)
        {
            return;
        }

        var nextHistory = DateTime.MinValue;
        string? lastProblem = null;
        using var timer = new PeriodicTimer(OpenInterval);
        do
        {
            try
            {
                var closed = await sync.SyncOpenAsync(stoppingToken);
                if (closed || DateTime.UtcNow >= nextHistory)
                {
                    await sync.SyncHistoryAsync(stoppingToken);
                    nextHistory = DateTime.UtcNow + HistoryInterval;
                }

                lastProblem = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // No credentials yet, offline, or Deriv refused a request: trading is unaffected; try again shortly.
                if (ex.Message != lastProblem)
                {
                    logger.LogWarning("Broker account sync failed: {Message}", ex.Message);
                    lastProblem = ex.Message;
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
