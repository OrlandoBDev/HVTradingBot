using System.Text.Json;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

/// <summary>What changed in learning (the Learning page reloads when <see cref="Version"/> changes).</summary>
public sealed record LearningPulse(int Resolved, int Open, DateTime? LastResolvedUtc, string Version);

/// <summary>
/// Pushes the dashboard status every two seconds (what SignalR does for the web dashboard) and a learning pulse
/// whenever a setup is added or resolved, so the dashboard and the notification update without polling.
/// </summary>
public sealed class LiveUpdates(DashboardQueries queries, IDbContextFactory<TradingDbContext> dbFactory, ILogger<LiveUpdates> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public const string StatusEvent = "status";
    public const string LearningEvent = "learning";

    public SystemStatusDto? Status { get; private set; }

    public LearningPulse? Learning { get; private set; }

    /// <summary>(event name, JSON payload) for the dashboard.</summary>
    public event Action<string, string>? Published;

    public event Action<SystemStatusDto>? StatusChanged;

    public event Action<LearningPulse>? LearningChanged;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PublishOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Best effort, like the web dashboard's broadcaster: the next tick tries again.
                logger.LogWarning(ex, "Live update failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    internal async Task PublishOnceAsync(CancellationToken cancellationToken)
    {
        var status = await queries.GetStatusAsync(cancellationToken);
        Status = status;
        StatusChanged?.Invoke(status);
        Published?.Invoke(StatusEvent, JsonSerializer.Serialize(status, LocalApi.Json));

        var learning = await LearningPulseAsync(cancellationToken);
        if (learning.Version != Learning?.Version)
        {
            Learning = learning;
            LearningChanged?.Invoke(learning);
            Published?.Invoke(LearningEvent, JsonSerializer.Serialize(learning, LocalApi.Json));
        }
    }

    private async Task<LearningPulse> LearningPulseAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var total = await db.SetupOutcomes.CountAsync(cancellationToken);
        var resolved = await db.SetupOutcomes.CountAsync(o => o.ClosedAtUtc != null, cancellationToken);
        var last = await db.SetupOutcomes.MaxAsync(o => o.ClosedAtUtc, cancellationToken);
        return new LearningPulse(resolved, total - resolved, last, $"{total}-{resolved}-{last?.Ticks}");
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
