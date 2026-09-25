using System.Net;
using HVTradingBot.App.Core.Configuration;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Health;
using HVTradingBot.App.Core.Processes;
using HVTradingBot.App.Core.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HVTradingBot.App.Tests;

public class StartupPipelineTests
{
    private const string Repo = "/Users/me/HVTradingBot";
    private const string Docker = "/usr/local/bin/docker";
    private const string ReadyBody = """{"status":"Healthy","checks":[{"name":"database","status":"Healthy","description":null},{"name":"trading-worker","status":"Healthy","description":"Worker running, data fresh."}]}""";

    private readonly FakeFileSystem files = new();
    private readonly FakeProcessRunner runner = new();
    private readonly FakeHttpHandler http = new();
    private readonly FakeTimeProvider time = new();
    private int[] portsInUse = [];

    public StartupPipelineTests()
    {
        files.Files[Docker] = "";
        files.Files[$"{Repo}/docker-compose.yml"] = "name: hvtradingbot";
        files.Files[$"{Repo}/.env.example"] = "POSTGRES_PASSWORD=change-me\nAPI_PORT=5080\n";
        http.Route("/health/live", HttpStatusCode.OK).Route("/health/ready", HttpStatusCode.OK, ReadyBody);
    }

    private StartupPipeline Create() => new(
        new DockerLocator(files),
        runner,
        new RepositoryEnvironment(files, NullLogger<RepositoryEnvironment>.Instance),
        new ConflictDetector(runner, new FakePortProbe(portsInUse), NullLogger<ConflictDetector>.Instance),
        new HealthClient(new HttpClient(http), time, NullLogger<HealthClient>.Instance),
        time,
        NullLogger<StartupPipeline>.Instance);

    private static StartupOptions Options => new(Repo) { InheritedPath = "/usr/bin:/bin", HomeDirectory = "/Users/me" };

    private FakeProcessRunner HappyDocker() => runner
        .On("info", 0, "28.1.1")
        .On("-Ao", 0)
        .On("compose --project-directory /Users/me/HVTradingBot up -d --build", 0, "Building api", "Container hvtradingbot-api-1 Started");

    [Fact]
    public async Task All_seven_steps_succeed_on_a_running_docker()
    {
        HappyDocker();
        var pipeline = Create();
        var output = new List<string>();
        pipeline.OutputReceived += (_, line) => output.Add(line);

        var result = await pipeline.RunAsync(Options, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri("http://localhost:5080/"), result.ApiBaseUri);
        Assert.Equal(WorkerState.Running, result.Worker?.State);
        Assert.Equal(7, pipeline.Steps.Count);
        Assert.All(pipeline.Steps, s => Assert.Equal(StepState.Succeeded, s.State));
        Assert.Equal("Worker running, data fresh", pipeline.Steps[^1].Message);
        Assert.Contains("Building api", output);
        Assert.StartsWith("$ /usr/local/bin/docker compose", output[0]);
        Assert.Contains($"{Repo}/.env", files.PrivateFiles);
        Assert.Contains("/Users/me/Library/Application Support/HVTradingBot/keys", files.Directories);
        Assert.All(runner.Calls, c => Assert.StartsWith("/usr/local/bin:/opt/homebrew/bin:", c.Environment!["PATH"]));
    }

    [Fact]
    public void Steps_are_in_the_documented_order()
    {
        Assert.Equal(
            [StartupStepId.FindDocker, StartupStepId.DockerRunning, StartupStepId.Repository, StartupStepId.Conflicts,
             StartupStepId.StartStack, StartupStepId.WaitForApi, StartupStepId.WatchWorker],
            Create().Steps.Select(s => s.Id));
    }

    [Fact]
    public async Task Missing_docker_links_to_the_installer_and_runs_nothing()
    {
        files.Files.Remove(Docker);
        var pipeline = Create();

        var result = await pipeline.RunAsync(Options, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(StartupStepId.FindDocker, result.FailedStep?.Id);
        Assert.Equal(DockerLocator.InstallUrl, result.FailedStep?.HelpUrl);
        Assert.Empty(runner.Calls);
        Assert.All(pipeline.Steps.Skip(1), s => Assert.Equal(StepState.Pending, s.State));
    }

    [Fact]
    public async Task Stopped_docker_is_opened_and_waited_for()
    {
        var infoCalls = 0;
        runner
            .On(c => c.Arguments[0] == "info", _ => new ProcessResult(++infoCalls < 4 ? 1 : 0, []))
            .On(c => c.FileName == "/usr/bin/open", _ => new ProcessResult(0, []));
        HappyDocker();

        var result = await time.DriveAsync(Create().RunAsync(Options, CancellationToken.None), TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.Contains(runner.Calls, c => c.FileName == "/usr/bin/open" && c.Arguments.SequenceEqual(["-a", "Docker"]));
        Assert.Equal(4, infoCalls);
    }

    [Fact]
    public async Task Docker_that_never_starts_fails_after_120_seconds()
    {
        runner.On("info", 1, "Cannot connect to the Docker daemon").On(c => c.FileName == "/usr/bin/open", _ => new ProcessResult(0, []));
        var start = time.GetUtcNow();

        var result = await time.DriveAsync(Create().RunAsync(Options, CancellationToken.None), TimeSpan.FromSeconds(5));

        Assert.Equal(StartupStepId.DockerRunning, result.FailedStep?.Id);
        Assert.Contains("120 s", result.FailedStep?.Message);
        Assert.InRange(time.GetUtcNow() - start, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(135));
    }

    [Fact]
    public async Task Folder_without_compose_file_fails_the_repository_step()
    {
        files.Files.Remove($"{Repo}/docker-compose.yml");
        HappyDocker();

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.Equal(StartupStepId.Repository, result.FailedStep?.Id);
        Assert.Contains("docker-compose.yml", result.FailedStep?.Message);
        Assert.Empty(files.Directories);
    }

    [Fact]
    public async Task Missing_env_example_fails_the_repository_step_with_the_reason()
    {
        files.Files.Remove($"{Repo}/.env.example");
        HappyDocker();

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.Equal(StartupStepId.Repository, result.FailedStep?.Id);
        Assert.Contains(".env.example is missing", result.FailedStep?.Message);
    }

    [Fact]
    public async Task Api_port_from_env_is_used()
    {
        files.Files[$"{Repo}/.env"] = "POSTGRES_PASSWORD=x\nAPI_PORT=6123\n";
        HappyDocker();

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.Equal(new Uri("http://localhost:6123/"), result.ApiBaseUri);
        Assert.All(http.Requests, r => Assert.Equal(6123, r.Port));
    }

    [Fact]
    public async Task Native_run_stops_startup_before_compose()
    {
        runner.On("info", 0).On("-Ao", 0, "dotnet HVTradingBot.Worker.dll");

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.Equal(StartupStepId.Conflicts, result.FailedStep?.Id);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("up"));
    }

    [Fact]
    public async Task Running_stack_on_the_port_is_not_a_conflict()
    {
        portsInUse = [5080];
        runner.On("ps --filter publish=5080", 0, "hvtradingbot/api");
        HappyDocker();

        Assert.True((await Create().RunAsync(Options, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Failed_compose_up_shows_the_last_output_line()
    {
        runner.On("info", 0).On("-Ao", 0).On("compose", 17, "failed to solve: npm ci exited with 1");

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.Equal(StartupStepId.StartStack, result.FailedStep?.Id);
        Assert.Equal("docker compose up failed with exit code 17: failed to solve: npm ci exited with 1", result.FailedStep?.Message);
    }

    [Fact]
    public async Task Api_is_polled_until_live()
    {
        HappyDocker();
        var liveCalls = 0;
        http.Route("/health/live", () => new HttpResponseMessage(++liveCalls < 5 ? HttpStatusCode.BadGateway : HttpStatusCode.OK));

        var result = await time.DriveAsync(Create().RunAsync(Options, CancellationToken.None), TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.Equal(5, liveCalls);
    }

    [Fact]
    public async Task Api_that_never_answers_fails_after_180_seconds()
    {
        HappyDocker();
        http.Routes.Remove("/health/live");
        var start = time.GetUtcNow();

        var result = await time.DriveAsync(Create().RunAsync(Options, CancellationToken.None), TimeSpan.FromSeconds(5));

        Assert.Equal(StartupStepId.WaitForApi, result.FailedStep?.Id);
        Assert.InRange(time.GetUtcNow() - start, TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(195));
    }

    [Fact]
    public async Task Offline_worker_does_not_fail_startup()
    {
        HappyDocker();
        http.Route("/health/ready", HttpStatusCode.ServiceUnavailable,
            """{"status":"Unhealthy","checks":[{"name":"trading-worker","status":"Unhealthy","description":"No worker heartbeat"}]}""");

        var result = await Create().RunAsync(Options, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(WorkerState.Offline, result.Worker?.State);
    }

    [Fact]
    public async Task Retry_resets_the_steps()
    {
        files.Files.Remove($"{Repo}/docker-compose.yml");
        HappyDocker();
        var pipeline = Create();
        await pipeline.RunAsync(Options, CancellationToken.None);
        var states = new List<StepState>();
        pipeline.Steps[2].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartupStep.State))
            {
                states.Add(pipeline.Steps[2].State);
            }
        };

        files.Files[$"{Repo}/docker-compose.yml"] = "";
        var result = await pipeline.RunAsync(Options, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([StepState.Pending, StepState.Running, StepState.Succeeded], states);
        Assert.Null(pipeline.Steps[2].HelpUrl);
    }

    [Fact]
    public async Task Cancellation_propagates_and_marks_the_step()
    {
        using var cts = new CancellationTokenSource();
        runner.On("info", 0).On("-Ao", 0).On(c => c.Arguments.Contains("up"), _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var pipeline = Create();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(Options, cts.Token));
        Assert.Equal(StepState.Failed, pipeline.Steps[(int)StartupStepId.StartStack].State);
        Assert.Equal("Cancelled", pipeline.Steps[(int)StartupStepId.StartStack].Message);
    }
}
