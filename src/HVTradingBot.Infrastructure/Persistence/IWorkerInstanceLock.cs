namespace HVTradingBot.Infrastructure.Persistence;

/// <summary>Guarantees that only one trading loop runs per database, so no order is ever placed twice.</summary>
public interface IWorkerInstanceLock
{
    /// <summary>True once this process holds the lock.</summary>
    bool IsAcquired { get; }

    /// <summary>Waits (retrying every <paramref name="retryInterval"/>) until the lock is held.</summary>
    Task AcquireAsync(TimeSpan retryInterval, CancellationToken cancellationToken);

    /// <summary>False when the lock was lost; the loop must then stop trading and restart.</summary>
    Task<bool> IsStillHeldAsync(CancellationToken cancellationToken);
}

/// <summary>
/// For the Android app: the database file is private to the app and the app runs exactly one engine host, so holding
/// the lock only means "this host is the one running".
/// </summary>
public sealed class InProcessWorkerLock : IWorkerInstanceLock
{
    public bool IsAcquired { get; private set; }

    public Task AcquireAsync(TimeSpan retryInterval, CancellationToken cancellationToken)
    {
        IsAcquired = true;
        return Task.CompletedTask;
    }

    public Task<bool> IsStillHeldAsync(CancellationToken cancellationToken) => Task.FromResult(IsAcquired);
}
