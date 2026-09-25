using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.App.Tests;

public class ConflictDetectorTests
{
    private static readonly ComposeCommands Commands = new(new DockerInstallation("/usr/local/bin/docker", "/usr/local/bin"), "/repo");

    private static ConflictDetector Detector(FakeProcessRunner runner, params int[] portsInUse) =>
        new(runner, new FakePortProbe(portsInUse), NullLogger<ConflictDetector>.Instance);

    [Fact]
    public async Task Nothing_running_is_no_conflict()
    {
        var runner = new FakeProcessRunner().On("-Ao", 0);

        Assert.Null(await Detector(runner).FindConflictAsync(Commands, 5080, CancellationToken.None));
        Assert.Single(runner.Calls); // the port is free, so docker ps is not needed
    }

    [Fact]
    public async Task Native_worker_is_a_conflict()
    {
        var runner = new FakeProcessRunner().On("-Ao", 0, "/Users/me/HVTradingBot/src/HVTradingBot.Worker/bin/Release/net10.0/HVTradingBot.Worker");

        var conflict = await Detector(runner).FindConflictAsync(Commands, 5080, CancellationToken.None);

        Assert.NotNull(conflict);
        Assert.Contains("already running outside Docker", conflict);
        Assert.Contains("net10.0/HVTradingBot.Worker", conflict);
    }

    [Fact]
    public async Task Port_held_by_the_api_container_is_fine()
    {
        var runner = new FakeProcessRunner().On("-Ao", 0).On("ps --filter publish=5080", 0, "hvtradingbot/api");

        Assert.Null(await Detector(runner, 5080).FindConflictAsync(Commands, 5080, CancellationToken.None));
    }

    [Fact]
    public async Task Port_held_by_another_program_is_a_conflict()
    {
        var runner = new FakeProcessRunner().On("-Ao", 0).On("ps --filter publish=6000", 0);

        var conflict = await Detector(runner, 6000).FindConflictAsync(Commands, 6000, CancellationToken.None);

        Assert.Equal("Port 6000 (API_PORT in .env) is already in use by another program. Stop it, or change API_PORT in .env.", conflict);
    }

    [Theory]
    [InlineData("otherproject/web", "the container otherproject/web")]
    [InlineData("/", "a Docker container")]
    public async Task Port_held_by_another_container_is_a_conflict(string psLine, string owner)
    {
        var runner = new FakeProcessRunner().On("-Ao", 0).On("ps --filter", 0, psLine);

        var conflict = await Detector(runner, 5080).FindConflictAsync(Commands, 5080, CancellationToken.None);

        Assert.Contains($"in use by {owner}.", conflict);
    }

    [Fact]
    public async Task Failing_ps_is_an_error_not_a_pass()
    {
        var runner = new FakeProcessRunner().On("-Ao", 1, "ps: illegal option");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Detector(runner).FindConflictAsync(Commands, 5080, CancellationToken.None));
    }

    [Theory]
    [InlineData("/Users/me/HVTradingBot/src/HVTradingBot.Worker/bin/Release/net10.0/HVTradingBot.Worker", true)]
    [InlineData("/Users/me/HVTradingBot/src/HVTradingBot.Api/bin/Release/net10.0/HVTradingBot.Api --urls http://127.0.0.1:5080", true)]
    [InlineData("dotnet HVTradingBot.Worker.dll", true)]
    [InlineData("dotnet exec /x/HVTradingBot.Api.dll --urls x", true)]
    [InlineData("tail -f /Users/me/HVTradingBot/src/HVTradingBot.Worker/obj/x", false)]
    [InlineData("/Applications/HVTradingBot.app/Contents/MacOS/HVTradingBot", false)]
    [InlineData("dotnet test tests/HVTradingBot.App.Tests", false)]
    [InlineData("HVTradingBot.Worker", true)]
    [InlineData("/x/MyHVTradingBot.Worker", false)]
    public void Native_process_pattern_matches_only_the_api_and_worker_executables(string commandLine, bool matches)
    {
        Assert.Equal(matches, Regex.IsMatch(commandLine, ConflictDetector.NativeProcessPattern));
    }

    [Fact]
    public void Run_sh_dev_processes_are_not_conflicts()
    {
        var native = ConflictDetector.NativeProcesses(
        [
            "/sbin/launchd",
            "  /Users/me/HVTradingBot/src/HVTradingBot.Api/bin/Debug/net10.0/HVTradingBot.Api --urls http://127.0.0.1:5081 --HVTradingBot:Instance=dev",
            "/Users/me/HVTradingBot/src/HVTradingBot.Worker/bin/Debug/net10.0/HVTradingBot.Worker --HVTradingBot:Instance=dev",
            "/Users/me/HVTradingBot/src/HVTradingBot.Worker/bin/Release/net10.0/HVTradingBot.Worker",
            "ps -Ao args="
        ]);

        Assert.Equal(["/Users/me/HVTradingBot/src/HVTradingBot.Worker/bin/Release/net10.0/HVTradingBot.Worker"], native);
    }

    [Fact]
    public async Task Only_dev_processes_running_is_no_conflict()
    {
        var runner = new FakeProcessRunner().On("-Ao", 0, "/r/HVTradingBot.Worker --HVTradingBot:Instance=dev", "/r/HVTradingBot.Api --HVTradingBot:Instance=dev");

        Assert.Null(await Detector(runner).FindConflictAsync(Commands, 5080, CancellationToken.None));
        Assert.Equal("/bin/ps", runner.Calls[0].FileName);
        Assert.Equal(["-Ao", "args="], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Tcp_probe_sees_a_listener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var probe = new TcpPortProbe(TimeProvider.System);

        Assert.True(await probe.IsInUseAsync(port, CancellationToken.None));
        listener.Stop();
        Assert.False(await probe.IsInUseAsync(port, CancellationToken.None));
    }
}
