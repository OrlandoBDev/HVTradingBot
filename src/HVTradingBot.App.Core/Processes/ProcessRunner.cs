using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Processes;

/// <summary><see cref="IProcessRunner"/> on top of <see cref="Process"/>, streaming stdout and stderr line by line.</summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessCommand command, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (command.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var (key, value) in command.Environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        var output = new List<string>();
        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        logger.LogDebug("Starting {Command}", command);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // Returns after the process exited and both redirected streams reached end of file.
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                logger.LogInformation("Cancelled; killing {Command} (pid {Pid})", command, process.Id);
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        logger.LogDebug("{Command} exited with code {ExitCode}", command, process.ExitCode);
        lock (output)
        {
            return new ProcessResult(process.ExitCode, output.ToArray());
        }

        void OnLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (output)
            {
                output.Add(line);
            }

            onOutput?.Invoke(line);
        }
    }
}
