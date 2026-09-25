using HVTradingBot.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class WorkerInstanceLockTests(PostgresFixture fixture)
{
    private WorkerInstanceLock NewLock() => new(fixture.ConnectionString, NullLogger<WorkerInstanceLock>.Instance);

    [Fact]
    public async Task Second_worker_cannot_take_the_lock_until_the_first_releases_it()
    {
        await using var first = NewLock();
        await using var second = NewLock();

        Assert.True(await first.TryAcquireAsync(CancellationToken.None));
        Assert.True(first.IsAcquired);
        Assert.True(await first.TryAcquireAsync(CancellationToken.None)); // repeated call by the holder is a no-op

        Assert.False(await second.TryAcquireAsync(CancellationToken.None));
        Assert.False(second.IsAcquired);

        await first.DisposeAsync();
        Assert.False(first.IsAcquired);

        Assert.True(await second.TryAcquireAsync(CancellationToken.None));
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public async Task Waiting_worker_takes_over_when_the_holder_stops()
    {
        await using var first = NewLock();
        await using var second = NewLock();
        Assert.True(await first.TryAcquireAsync(CancellationToken.None));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var waiting = second.AcquireAsync(TimeSpan.FromMilliseconds(100), timeout.Token);
        await Task.Delay(300, timeout.Token);
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await waiting;
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public async Task Lock_is_reported_lost_when_its_connection_is_terminated()
    {
        await using var holder = NewLock();
        Assert.True(await holder.TryAcquireAsync(CancellationToken.None));
        Assert.True(await holder.IsStillHeldAsync(CancellationToken.None));

        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var kill = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = 'HVTradingBot.Worker lock'", admin);
            await kill.ExecuteNonQueryAsync();
        }

        Assert.False(await holder.IsStillHeldAsync(CancellationToken.None));
        Assert.False(holder.IsAcquired);

        await using var next = NewLock();
        Assert.True(await next.TryAcquireAsync(CancellationToken.None));
    }
}
