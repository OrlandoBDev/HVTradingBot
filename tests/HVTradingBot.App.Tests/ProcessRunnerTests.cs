using System.Diagnostics;
using HVTradingBot.App.Core.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.App.Tests;

/// <summary>Runs real child processes through /bin/sh (Linux and macOS).</summary>
public class ProcessRunnerTests
{
    private readonly ProcessRunner runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task Output_of_stdout_and_stderr_is_streamed_and_collected()
    {
        var streamed = new List<string>();

        var result = await runner.RunAsync(
            new ProcessCommand("/bin/sh", ["-c", "echo one; echo two >&2; exit 3"]),
            line => { lock (streamed) { streamed.Add(line); } },
            CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Equal(["one", "two"], result.Output.Order());
        Assert.Equal(["one", "two"], streamed.Order());
    }

    [Fact]
    public async Task Environment_and_working_directory_are_passed_to_the_child()
    {
        var directory = Directory.CreateTempSubdirectory("hv-app-").FullName;

        var result = await runner.RunAsync(
            new ProcessCommand("/bin/sh", ["-c", "echo \"$PATH\"; pwd"], directory, new Dictionary<string, string> { ["PATH"] = "/custom/bin:/usr/bin:/bin" }),
            null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("/custom/bin:/usr/bin:/bin", result.Output[0]);
        Assert.Equal(new DirectoryInfo(directory).FullName.TrimEnd('/'), result.Output[1].TrimEnd('/'));
    }

    [Fact]
    public async Task Cancelling_kills_the_process()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(new ProcessCommand("/bin/sh", ["-c", "sleep 30"]), null, cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Detached_process_runs_without_being_awaited()
    {
        var marker = Path.Combine(Directory.CreateTempSubdirectory("hv-app-").FullName, "stopped");

        runner.StartDetached(new ProcessCommand("/bin/sh", ["-c", $"echo done > '{marker}'"]));

        for (var i = 0; i < 200 && !File.Exists(marker); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void Output_log_keeps_the_last_lines()
    {
        var log = new OutputLog(capacity: 3);

        foreach (var line in new[] { "1", "2", "3", "4" })
        {
            log.Append(line);
        }

        Assert.Equal("2\n3\n4", log.Text);
        Assert.Equal(4, log.Version);
        log.Clear();
        Assert.Equal("", log.Text);
        Assert.Equal(5, log.Version);
    }
}
