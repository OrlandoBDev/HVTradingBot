namespace HVTradingBot.App.Core.Processes;

/// <summary>A child process to start: executable, arguments (not shell-quoted) and extra environment variables.</summary>
public sealed record ProcessCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    /// <summary>Human-readable form for logs and the log view (not meant to be pasted into a shell).</summary>
    public override string ToString() =>
        string.Join(' ', new[] { FileName }.Concat(Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
}

/// <summary>Exit code and all output lines (stdout and stderr, in arrival order) of a finished process.</summary>
public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs child processes. Tests use a fake; the app uses <see cref="ProcessRunner"/>.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> to completion. Each stdout/stderr line is passed to <paramref name="onOutput"/>
    /// as it arrives (possibly from a thread-pool thread). Cancelling kills the process tree and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessCommand command, Action<string>? onOutput, CancellationToken cancellationToken);

    /// <summary>
    /// Starts <paramref name="command"/> without waiting or reading its output. On macOS the child keeps running after
    /// the app exits, so this is how work that must outlive the app (stop on quit) is started.
    /// </summary>
    void StartDetached(ProcessCommand command);
}
