namespace HVTradingBot.App.Core.Health;

/// <summary>Polls <c>/health/ready</c> every <see cref="Interval"/> (startup step 7) and reports each result.</summary>
public sealed class HealthMonitor(HealthClient health, TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>Raised after every poll, on a thread-pool thread.</summary>
    public event EventHandler<WorkerStatus>? StatusChanged;

    public WorkerStatus? Current { get; private set; }

    /// <summary>Polls immediately, then every <see cref="Interval"/>, until cancelled.</summary>
    public async Task RunAsync(Uri apiBaseUri, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            await PollOnceAsync(apiBaseUri, cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    public async Task<WorkerStatus> PollOnceAsync(Uri apiBaseUri, CancellationToken cancellationToken)
    {
        var status = await health.GetWorkerStatusAsync(apiBaseUri, cancellationToken);
        Current = status;
        StatusChanged?.Invoke(this, status);
        return status;
    }
}
