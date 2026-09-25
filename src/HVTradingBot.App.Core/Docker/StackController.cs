using HVTradingBot.App.Core.Processes;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Docker;

/// <summary>The app's controls: stop trading, start, restart the worker and show logs.</summary>
public sealed class StackController(IProcessRunner runner, ComposeCommands commands, ILogger<StackController> logger)
{
    public Task<ProcessResult> StopAsync(CancellationToken cancellationToken) => RunAsync(commands.Stop(), cancellationToken);

    public Task<ProcessResult> StartAsync(CancellationToken cancellationToken) => RunAsync(commands.Start(), cancellationToken);

    public Task<ProcessResult> RestartWorkerAsync(CancellationToken cancellationToken) =>
        RunAsync(commands.RestartWorker(), cancellationToken);

    public Task<ProcessResult> LogsAsync(string service, CancellationToken cancellationToken) =>
        RunAsync(commands.Logs(service), cancellationToken);

    private async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running {Command}", command);
        var result = await runner.RunAsync(command, null, cancellationToken);
        if (!result.Succeeded)
        {
            logger.LogWarning("{Command} failed with exit code {ExitCode}: {LastLine}",
                command, result.ExitCode, result.Output.LastOrDefault());
        }

        return result;
    }
}
