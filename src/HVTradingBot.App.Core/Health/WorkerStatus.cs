namespace HVTradingBot.App.Core.Health;

public enum WorkerState
{
    /// <summary>Worker heartbeat recent and market data fresh.</summary>
    Running,

    /// <summary>Worker alive but market data is older than the risk limit; new trades are blocked.</summary>
    DataStale,

    /// <summary>Worker alive, data fresh, but the kill switch blocks trading.</summary>
    KillSwitchActive,

    /// <summary>No worker heartbeat (stopped, crashed, restarting or database unavailable).</summary>
    Offline,

    /// <summary>The API did not answer, so the worker state is unknown.</summary>
    ApiUnreachable
}

/// <summary>What the status bar shows, derived from <c>/health/ready</c>.</summary>
/// <param name="Detail">The health check's description, or why the API could not be read.</param>
/// <param name="DatabaseHealthy">False when the API reports the database check as not healthy.</param>
public sealed record WorkerStatus(WorkerState State, string Detail, bool DatabaseHealthy)
{
    public string Summary => State switch
    {
        WorkerState.Running => "Worker running, data fresh",
        WorkerState.DataStale => "Worker running, data stale",
        WorkerState.KillSwitchActive => "Kill switch active",
        WorkerState.Offline => "Worker offline",
        WorkerState.ApiUnreachable => "API not reachable",
        _ => State.ToString()
    };
}
