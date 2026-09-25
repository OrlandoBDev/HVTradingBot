using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.App.Tests;

public class DockerTests
{
    private static readonly DockerInstallation Docker = new("/opt/homebrew/bin/docker", "/opt/homebrew/bin:/usr/bin");
    private static readonly ComposeCommands Commands = new(Docker, "/Users/me/HVTradingBot");

    [Fact]
    public void Docker_is_found_in_the_first_directory_that_has_it()
    {
        var files = new FakeFileSystem();
        files.Files["/opt/homebrew/bin/docker"] = "";
        files.Files["/Applications/Docker.app/Contents/Resources/bin/docker"] = "";

        var docker = new DockerLocator(files).Locate("/usr/bin:/bin");

        Assert.Equal("/opt/homebrew/bin/docker", docker?.DockerPath);
    }

    [Fact]
    public void Docker_app_bundle_is_the_last_resort()
    {
        var files = new FakeFileSystem();
        files.Files["/Applications/Docker.app/Contents/Resources/bin/docker"] = "";

        Assert.Equal("/Applications/Docker.app/Contents/Resources/bin/docker", new DockerLocator(files).Locate(null)?.DockerPath);
    }

    [Fact]
    public void Missing_docker_gives_null()
    {
        Assert.Null(new DockerLocator(new FakeFileSystem()).Locate("/usr/local/bin"));
    }

    [Fact]
    public void Child_path_has_all_docker_directories_first_without_duplicates()
    {
        var path = DockerLocator.BuildPath("/usr/bin:/bin:/opt/homebrew/bin:/Users/me/.dotnet:");

        Assert.Equal(
            "/usr/local/bin:/opt/homebrew/bin:/Applications/Docker.app/Contents/Resources/bin:/usr/bin:/bin:/usr/sbin:/sbin:/Users/me/.dotnet",
            path);
    }

    [Fact]
    public void Every_docker_command_gets_the_child_path()
    {
        foreach (var command in new[] { Commands.Info(), Commands.Up(), Commands.Stop(), Commands.OpenDockerDesktop() })
        {
            Assert.Equal("/opt/homebrew/bin:/usr/bin", command.Environment?["PATH"]);
        }
    }

    [Fact]
    public void Compose_commands_target_the_repository()
    {
        Assert.Equal("/opt/homebrew/bin/docker", Commands.Up().FileName);
        Assert.Equal(["compose", "--project-directory", "/Users/me/HVTradingBot", "up", "-d", "--build"], Commands.Up().Arguments);
        Assert.Equal(["compose", "--project-directory", "/Users/me/HVTradingBot", "stop"], Commands.Stop().Arguments);
        Assert.Equal(["compose", "--project-directory", "/Users/me/HVTradingBot", "start"], Commands.Start().Arguments);
        Assert.Equal(["compose", "--project-directory", "/Users/me/HVTradingBot", "restart", "worker"], Commands.RestartWorker().Arguments);
        Assert.Equal(["compose", "--project-directory", "/Users/me/HVTradingBot", "logs", "--no-color", "--tail", "200", "api"],
            Commands.Logs("api").Arguments);
        Assert.Equal("/Users/me/HVTradingBot", Commands.Up().WorkingDirectory);
    }

    [Fact]
    public void Logs_are_only_for_api_and_worker()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Commands.Logs("postgres"));
    }

    [Fact]
    public void Docker_desktop_is_opened_with_open()
    {
        var open = Commands.OpenDockerDesktop();

        Assert.Equal("/usr/bin/open", open.FileName);
        Assert.Equal(["-a", "Docker"], open.Arguments);
    }

    [Fact]
    public async Task Controls_run_the_matching_compose_command()
    {
        var runner = new FakeProcessRunner().On("compose", 0, "done");
        var controller = new StackController(runner, Commands, NullLogger<StackController>.Instance);

        await controller.StopAsync(CancellationToken.None);
        await controller.StartAsync(CancellationToken.None);
        await controller.RestartWorkerAsync(CancellationToken.None);
        var logs = await controller.LogsAsync("worker", CancellationToken.None);

        Assert.Equal(["stop", "start", "restart worker", "logs --no-color --tail 200 worker"],
            runner.Calls.Select(c => string.Join(' ', c.Arguments.Skip(3))));
        Assert.Equal(["done"], logs.Output);
    }

    [Fact]
    public void Stop_on_quit_does_not_wait_for_compose()
    {
        var runner = new FakeProcessRunner();

        new StackController(runner, Commands, NullLogger<StackController>.Instance).StopInBackground();

        Assert.Empty(runner.Calls);
        Assert.Equal(Commands.Stop(), Assert.Single(runner.Detached), CommandComparer.Instance);
    }

    [Fact]
    public void Command_text_quotes_arguments_with_spaces()
    {
        Assert.Equal("docker compose --project-directory \"/Users/me/My Repo\" stop",
            new ProcessCommand("docker", ["compose", "--project-directory", "/Users/me/My Repo", "stop"]).ToString());
    }
}

internal sealed class CommandComparer : IEqualityComparer<ProcessCommand>
{
    public static readonly CommandComparer Instance = new();

    public bool Equals(ProcessCommand? x, ProcessCommand? y) =>
        x is not null && y is not null && x.ToString() == y.ToString() && x.WorkingDirectory == y.WorkingDirectory;

    public int GetHashCode(ProcessCommand obj) => obj.ToString().GetHashCode(StringComparison.Ordinal);
}
