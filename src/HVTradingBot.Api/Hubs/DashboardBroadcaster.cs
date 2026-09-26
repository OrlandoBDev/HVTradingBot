using HVTradingBot.Dashboard;
using Microsoft.AspNetCore.SignalR;

namespace HVTradingBot.Api.Hubs;

/// <summary>Pushes the system status to connected dashboards every two seconds.</summary>
public sealed class DashboardBroadcaster(
    IHubContext<DashboardHub> hub,
    DashboardQueries queries,
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
                var status = await queries.GetStatusAsync(stoppingToken);
                await hub.Clients.All.SendAsync(DashboardHub.StatusMessage, status, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Dashboard push is best effort; the API keeps serving and the error is logged.
                logger.LogWarning(ex, "Failed to broadcast dashboard status");
            }
        }
    }
}
