using System.Net;
using System.Text.Json;
using HVTradingBot.App.Core.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HVTradingBot.App.Tests;

public class HealthTests
{
    private static readonly Uri Api = new("http://localhost:5080/");

    private static string Ready(string overall, string workerStatus, string? workerDescription, string databaseStatus = "Healthy") =>
        JsonSerializer.Serialize(new
        {
            status = overall,
            checks = new object[]
            {
                new { name = "database", status = databaseStatus, description = (string?)null },
                new { name = "trading-worker", status = workerStatus, description = workerDescription }
            }
        });

    [Fact]
    public void Healthy_worker_is_running()
    {
        var status = ReadyResponseParser.Parse(Ready("Healthy", "Healthy", "Worker running, data fresh."));

        Assert.Equal(WorkerState.Running, status.State);
        Assert.True(status.DatabaseHealthy);
        Assert.Equal("Worker running, data fresh.", status.Detail);
    }

    [Fact]
    public void Degraded_worker_has_stale_data()
    {
        var status = ReadyResponseParser.Parse(Ready("Degraded", "Degraded", "Market data is stale; new trades are blocked."));

        Assert.Equal(WorkerState.DataStale, status.State);
        Assert.Equal("Worker running, data stale", status.Summary);
    }

    [Fact]
    public void Kill_switch_is_told_apart_from_stale_data()
    {
        Assert.Equal(WorkerState.KillSwitchActive,
            ReadyResponseParser.Parse(Ready("Degraded", "Degraded", "Kill switch active: daily loss limit")).State);
    }

    [Fact]
    public void Unhealthy_worker_is_offline()
    {
        var status = ReadyResponseParser.Parse(Ready("Unhealthy", "Unhealthy", "No worker heartbeat since 2026-09-25 10:00:00Z."));

        Assert.Equal(WorkerState.Offline, status.State);
        Assert.Equal("Worker offline", status.Summary);
    }

    [Fact]
    public void Database_failure_is_reported()
    {
        var status = ReadyResponseParser.Parse(Ready("Unhealthy", "Unhealthy", null, databaseStatus: "Unhealthy"));

        Assert.Equal(WorkerState.Offline, status.State);
        Assert.False(status.DatabaseHealthy);
        Assert.Equal("Database unavailable", status.Detail);
    }

    [Fact]
    public void Pascal_case_properties_are_accepted()
    {
        const string json = """{"Status":"Healthy","Checks":[{"Name":"trading-worker","Status":"Healthy","Description":"ok"}]}""";

        Assert.Equal(WorkerState.Running, ReadyResponseParser.Parse(json).State);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"status":"Healthy"}""")]
    [InlineData("""{"status":"Healthy","checks":[{"name":"database","status":"Healthy"}]}""")]
    public void Unexpected_bodies_are_rejected(string body)
    {
        Assert.ThrowsAny<JsonException>(() => ReadyResponseParser.Parse(body));
    }

    [Fact]
    public async Task Ready_body_is_read_even_with_503()
    {
        var handler = new FakeHttpHandler().Route("/health/ready", HttpStatusCode.ServiceUnavailable, Ready("Unhealthy", "Unhealthy", "No worker heartbeat"));

        var status = await Client(handler).GetWorkerStatusAsync(Api, CancellationToken.None);

        Assert.Equal(WorkerState.Offline, status.State);
    }

    [Fact]
    public async Task Refused_connection_means_api_unreachable()
    {
        var client = Client(new FakeHttpHandler());

        Assert.Equal(WorkerState.ApiUnreachable, (await client.GetWorkerStatusAsync(Api, CancellationToken.None)).State);
        Assert.False(await client.IsLiveAsync(Api, CancellationToken.None));
    }

    [Fact]
    public async Task Garbage_ready_body_means_api_unreachable()
    {
        var handler = new FakeHttpHandler().Route("/health/ready", HttpStatusCode.OK, "<html>");

        Assert.Equal(WorkerState.ApiUnreachable, (await Client(handler).GetWorkerStatusAsync(Api, CancellationToken.None)).State);
    }

    [Fact]
    public async Task Live_needs_a_success_status()
    {
        Assert.True(await Client(new FakeHttpHandler().Route("/health/live", HttpStatusCode.OK)).IsLiveAsync(Api, CancellationToken.None));
        Assert.False(await Client(new FakeHttpHandler().Route("/health/live", HttpStatusCode.BadGateway)).IsLiveAsync(Api, CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_a_status()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(new FakeHttpHandler().Route("/health/ready", HttpStatusCode.OK, "{}")).GetWorkerStatusAsync(Api, cts.Token));
    }

    [Fact]
    public async Task Monitor_polls_every_15_seconds()
    {
        var time = new FakeTimeProvider();
        var handler = new FakeHttpHandler().Route("/health/ready", HttpStatusCode.OK, Ready("Healthy", "Healthy", "ok"));
        var monitor = new HealthMonitor(Client(handler, time), time);
        var seen = 0;
        monitor.StatusChanged += (_, _) => Interlocked.Increment(ref seen);
        using var cts = new CancellationTokenSource();

        var run = monitor.RunAsync(Api, cts.Token);
        await WaitForAsync(() => Volatile.Read(ref seen) == 1);
        time.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(20);
        Assert.Equal(1, Volatile.Read(ref seen));
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => Volatile.Read(ref seen) == 2);
        time.Advance(TimeSpan.FromSeconds(15));
        await WaitForAsync(() => Volatile.Read(ref seen) == 3);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(WorkerState.Running, monitor.Current?.State);
    }

    private static HealthClient Client(FakeHttpHandler handler, TimeProvider? time = null) =>
        new(new HttpClient(handler), time ?? TimeProvider.System, NullLogger<HealthClient>.Instance);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
