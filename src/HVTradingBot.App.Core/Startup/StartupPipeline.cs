using HVTradingBot.App.Core.Configuration;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Health;
using HVTradingBot.App.Core.Processes;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Startup;

/// <summary>Timeouts of the startup sequence; the defaults are those in docs/MACOS_APP.md.</summary>
public sealed record StartupOptions(string RepositoryPath)
{
    public TimeSpan DockerStartTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan DockerPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ApiLiveTimeout { get; init; } = TimeSpan.FromSeconds(180);

    public TimeSpan ApiPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The app's own <c>PATH</c>; the child processes get the Docker directories in front of it.</summary>
    public string? InheritedPath { get; init; } = Environment.GetEnvironmentVariable("PATH");

    /// <summary>The user's home, where the shared key folder lives (<c>${HOME}</c> in <c>docker-compose.yml</c>).</summary>
    public string HomeDirectory { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

/// <summary>Outcome of <see cref="StartupPipeline.RunAsync"/>.</summary>
/// <param name="Commands">Compose commands for the controls; null if Docker was not found.</param>
/// <param name="ApiBaseUri">Where the dashboard is served; null before the repository step succeeded.</param>
/// <param name="Worker">The first <c>/health/ready</c> result; null unless the stack started.</param>
public sealed record StartupResult(bool Succeeded, ComposeCommands? Commands, Uri? ApiBaseUri, WorkerStatus? Worker)
{
    public StartupStep? FailedStep { get; init; }
}

/// <summary>
/// The app's startup sequence (docs/MACOS_APP.md, "Startup sequence"): each step's state is shown on the startup
/// screen, and <c>docker compose</c> output is streamed through <see cref="OutputReceived"/>.
/// </summary>
public sealed class StartupPipeline(
    DockerLocator locator,
    IProcessRunner runner,
    RepositoryEnvironment repository,
    ConflictDetector conflicts,
    HealthClient health,
    TimeProvider time,
    ILogger<StartupPipeline> logger)
{
    private readonly StartupStep[] steps =
    [
        new(StartupStepId.FindDocker, "Find Docker"),
        new(StartupStepId.DockerRunning, "Docker running"),
        new(StartupStepId.Repository, "Repository, .env and key folder"),
        new(StartupStepId.Conflicts, "No conflicting native run"),
        new(StartupStepId.StartStack, "Start containers"),
        new(StartupStepId.WaitForApi, "Wait for the API"),
        new(StartupStepId.WatchWorker, "Check the worker")
    ];

    /// <summary>The seven steps in order.</summary>
    public IReadOnlyList<StartupStep> Steps => steps;

    /// <summary>Command lines and <c>docker compose</c> output for the log view (may be raised off the UI thread).</summary>
    public event EventHandler<string>? OutputReceived;

    /// <summary>Runs the sequence; stops at the first failed step. Safe to call again for a retry.</summary>
    public async Task<StartupResult> RunAsync(StartupOptions options, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            step.Reset();
        }

        ComposeCommands? commands = null;
        Uri? apiBaseUri = null;
        StartupStep? current = null;
        try
        {
            current = Begin(StartupStepId.FindDocker, "Looking for docker");
            var docker = locator.Locate(options.InheritedPath);
            if (docker is null)
            {
                return Fail(current, "Docker Desktop is not installed. Install it, start it once, then retry.", DockerLocator.InstallUrl);
            }

            commands = new ComposeCommands(docker, options.RepositoryPath);
            current.Update(StepState.Succeeded, docker.DockerPath);

            current = Begin(StartupStepId.DockerRunning, "Checking the Docker daemon");
            if (!await EnsureDockerRunningAsync(commands, current, options, cancellationToken))
            {
                return Fail(current, $"Docker did not start within {options.DockerStartTimeout.TotalSeconds:0} s. Start Docker Desktop and retry.");
            }

            current = Begin(StartupStepId.Repository, options.RepositoryPath);
            if (!repository.HasComposeFile(options.RepositoryPath))
            {
                return Fail(current, $"{options.RepositoryPath} has no {RepositoryEnvironment.ComposeFileName}. " +
                                     "Choose the HVTradingBot checkout in Settings.");
            }

            var created = repository.EnsureEnvFile(options.RepositoryPath);
            repository.EnsureKeysDirectory(options.HomeDirectory);
            var apiPort = repository.ReadApiPort(options.RepositoryPath);
            apiBaseUri = new Uri($"http://localhost:{apiPort}/");
            current.Update(StepState.Succeeded, (created ? "Created .env. " : "") + $"Dashboard port {apiPort}");

            current = Begin(StartupStepId.Conflicts, "Looking for native API/worker processes");
            if (await conflicts.FindConflictAsync(commands, apiPort, cancellationToken) is { } conflict)
            {
                return Fail(current, conflict);
            }

            current.Update(StepState.Succeeded, null);

            current = Begin(StartupStepId.StartStack, "docker compose up (the first build takes a few minutes)");
            var up = await RunStreamingAsync(commands.Up(), cancellationToken);
            if (!up.Succeeded)
            {
                return Fail(current, $"docker compose up failed with exit code {up.ExitCode}: {up.Output.LastOrDefault()}");
            }

            current.Update(StepState.Succeeded, null);

            current = Begin(StartupStepId.WaitForApi, apiBaseUri.ToString());
            if (!await WaitUntilAsync(() => health.IsLiveAsync(apiBaseUri, cancellationToken), options.ApiLiveTimeout, options.ApiPollInterval, cancellationToken))
            {
                return Fail(current, $"The API did not answer within {options.ApiLiveTimeout.TotalSeconds:0} s. Check the api log.");
            }

            current.Update(StepState.Succeeded, null);

            // Step 7 keeps running in HealthMonitor; here only the first result is shown. An offline or stale worker is
            // reported but does not fail startup: the dashboard shows the details.
            current = Begin(StartupStepId.WatchWorker, "Reading /health/ready");
            var worker = await health.GetWorkerStatusAsync(apiBaseUri, cancellationToken);
            current.Update(StepState.Succeeded, worker.Summary);
            logger.LogInformation("Stack started; {WorkerState}: {Detail}", worker.State, worker.Detail);
            return new StartupResult(true, commands, apiBaseUri, worker);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && current is not null)
        {
            logger.LogError(ex, "Startup step {Step} failed", current.Id);
            return Fail(current, ex.Message);
        }
        catch (OperationCanceledException) when (current is not null)
        {
            current.Update(StepState.Failed, "Cancelled");
            throw;
        }

        StartupResult Fail(StartupStep step, string message, string? helpUrl = null)
        {
            logger.LogWarning("Startup stopped at {Step}: {Reason}", step.Id, message);
            step.Update(StepState.Failed, message, helpUrl);
            return new StartupResult(false, commands, apiBaseUri, null) { FailedStep = step };
        }
    }

    private StartupStep Begin(StartupStepId id, string? message)
    {
        var step = steps[(int)id];
        step.Update(StepState.Running, message);
        logger.LogInformation("Startup step {Step}: {Message}", id, message);
        return step;
    }

    private async Task<bool> EnsureDockerRunningAsync(ComposeCommands commands, StartupStep step, StartupOptions options, CancellationToken cancellationToken)
    {
        if ((await runner.RunAsync(commands.Info(), null, cancellationToken)).Succeeded)
        {
            step.Update(StepState.Succeeded, null);
            return true;
        }

        step.Update(StepState.Running, "Starting Docker Desktop");
        var open = await RunStreamingAsync(commands.OpenDockerDesktop(), cancellationToken);
        if (!open.Succeeded)
        {
            throw new InvalidOperationException($"open -a Docker failed with exit code {open.ExitCode}: {open.Output.LastOrDefault()}");
        }

        var started = await WaitUntilAsync(
            async () => (await runner.RunAsync(commands.Info(), null, cancellationToken)).Succeeded,
            options.DockerStartTimeout, options.DockerPollInterval, cancellationToken);
        if (started)
        {
            step.Update(StepState.Succeeded, "Docker Desktop started");
        }

        return started;
    }

    private async Task<ProcessResult> RunStreamingAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        Emit("$ " + command);
        var result = await runner.RunAsync(command, Emit, cancellationToken);
        if (!result.Succeeded)
        {
            Emit($"(exit code {result.ExitCode})");
        }

        return result;
    }

    private void Emit(string line) => OutputReceived?.Invoke(this, line);

    /// <summary>Evaluates <paramref name="condition"/> until it is true or <paramref name="timeout"/> elapsed.</summary>
    private async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan interval, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + timeout;
        while (true)
        {
            if (await condition())
            {
                return true;
            }

            if (time.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(interval, time, cancellationToken);
        }
    }
}
