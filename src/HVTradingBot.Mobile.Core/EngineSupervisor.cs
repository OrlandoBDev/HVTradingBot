using HVTradingBot.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Restarting
}

/// <summary>
/// Plays the part Docker and run.sh play for the worker process: runs the trading host and starts a fresh one whenever
/// it stops on its own (a new market selection, a lost connection, repeated failures), until the app stops it.
/// </summary>
public sealed class EngineSupervisor(Func<IHost> createHost, ILogger<EngineSupervisor> logger)
{
    /// <summary>A host that ran at least this long starts over with the shortest restart delay.</summary>
    private static readonly TimeSpan HealthyRun = TimeSpan.FromMinutes(5);

    internal TimeSpan MinRestartDelay { get; init; } = TimeSpan.FromSeconds(5);

    internal TimeSpan MaxRestartDelay { get; init; } = TimeSpan.FromMinutes(1);

    public EngineState State { get; private set; } = EngineState.Stopped;

    /// <summary>Why the engine last stopped on its own, if it failed.</summary>
    public string? LastError { get; private set; }

    public int Restarts { get; private set; }

    public event Action? Changed;

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var delay = MinRestartDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            SetState(EngineState.Starting);
            IHost? host = null;
            try
            {
                host = createHost();
                await host.StartAsync(stoppingToken);
                SetState(EngineState.Running);
                LastError = null;
                // Returns when the host stops itself (StopApplication) or the app stops the engine.
                await host.WaitForShutdownAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                logger.LogError(ex, "Trading engine failed");
                if (host is not null)
                {
                    await StopQuietlyAsync(host);
                }
            }
            finally
            {
                host?.Dispose();
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // The worker asks for a restart with exit code 3 (e.g. new market selection): come back at once.
            var requested = Environment.ExitCode == BrokerSettingsWatcher.RestartExitCode;
            Environment.ExitCode = 0;
            if (requested || DateTime.UtcNow - started >= HealthyRun)
            {
                delay = MinRestartDelay;
            }

            var wait = requested ? TimeSpan.FromSeconds(1) : delay;
            Restarts++;
            SetState(EngineState.Restarting);
            logger.LogWarning("Trading engine stopped; starting it again in {Seconds:0} s", wait.TotalSeconds);
            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRestartDelay.Ticks));
        }

        SetState(EngineState.Stopped);
    }

    private async Task StopQuietlyAsync(IHost host)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.StopAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stopping the failed engine host");
        }
    }

    private void SetState(EngineState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
