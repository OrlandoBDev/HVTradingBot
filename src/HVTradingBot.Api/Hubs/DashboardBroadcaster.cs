using HVTradingBot.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace HVTradingBot.Api.Hubs;

/// <summary>Pushes the system status, new candles and changed quotes to connected dashboards every two seconds.</summary>
public sealed class DashboardBroadcaster(
    IHubContext<DashboardHub> hub,
    DashboardQueries queries,
    LiveMarketStream market,
    ILogger<DashboardBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await BroadcastOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>One push. Status and market data fail independently so one broken query doesn't silence the other.</summary>
    public async Task BroadcastOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await queries.GetStatusAsync(cancellationToken);
            await hub.Clients.All.SendAsync(DashboardHub.StatusMessage, status, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dashboard push is best effort; the API keeps serving and the error is logged.
            logger.LogWarning(ex, "Failed to broadcast dashboard status");
        }

        try
        {
            var update = await market.NextAsync(cancellationToken);
            foreach (var bar in update.Bars)
            {
                await hub.Clients.All.SendAsync(DashboardHub.BarMessage, bar, cancellationToken);
            }

            if (update.Ticks.Count > 0)
            {
                await hub.Clients.All.SendAsync(DashboardHub.TicksMessage, update.Ticks, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to broadcast live market data");
        }
    }
}
