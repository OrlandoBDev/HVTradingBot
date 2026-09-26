using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.TestTrades;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.IntegrationTests;

public abstract class TestTradeStoreTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM test_trades;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_one_test_trade_runs_at_a_time()
    {
        var store = new TestTradeStore(fixture.DbFactory, new Clock());

        var first = await store.CreateAsync("EUR/USD", "tester", CancellationToken.None);
        var second = await store.CreateAsync("GBP/USD", "tester", CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(second);

        await store.UpdateAsync(first!.Id, t => t.Status = TestTradeStatus.Closed, CancellationToken.None);
        Assert.NotNull(await store.CreateAsync("GBP/USD", "tester", CancellationToken.None));
        Assert.Equal("GBP/USD", (await store.NextActiveAsync(CancellationToken.None))!.Instrument);
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

[Collection(PostgresCollection.Name)]
public sealed class TestTradeStoreTestsOnPostgres(PostgresFixture fixture) : TestTradeStoreTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class TestTradeStoreTestsOnSqlite(SqliteFixture fixture) : TestTradeStoreTests(fixture);
