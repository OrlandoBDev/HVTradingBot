using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.IntegrationTests;

public abstract class CloseRequestTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CurrencyConverter Converter = new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.1m });

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM close_requests;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Candle Bar(DateTime open, decimal close = 1.1m) => new(open, TimeFrame.M5, close, close + 0.0005m, close - 0.0005m, close, 0.0001m, 10);

    [Fact]
    public async Task Paper_position_closes_on_request_and_duplicate_or_stale_requests_are_refused()
    {
        var broker = fixture.NewBroker();
        await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0), Converter, CancellationToken.None);
        var fill = await broker.PlaceOrderAsync(new TradeOrder("EURUSD-T-L-9", Instruments.EurUsd, Direction.Long, 10_000m, 1.1m, 1.09m, 1.12m,
            100m, "Test", 80, null, "c"), CancellationToken.None);
        var store = new CloseRequestStore(fixture.DbFactory, new Clock());

        var (request, problem) = await store.CreateAsync(fill.PositionId!.Value, "tester", CancellationToken.None);
        Assert.NotNull(request);
        Assert.Null(problem);
        Assert.Contains("already in progress", (await store.CreateAsync(fill.PositionId.Value, "tester", CancellationToken.None)).Problem);

        await broker.ClosePositionAsync(fill.PositionId.Value.ToString(), CancellationToken.None);
        // The next bar started before the position opened would not trigger its stop; the close request applies at once.
        var closed = Assert.Single(await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0), Converter, CancellationToken.None));
        Assert.Equal(ExitReason.Manual, closed.Reason);

        await store.UpdateAsync(request!.Id, r => r.Status = CloseRequestStatus.Closed, CancellationToken.None);
        Assert.Contains("no longer open", (await store.CreateAsync(fill.PositionId.Value, "tester", CancellationToken.None)).Problem);
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => T0.AddMinutes(6);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class CloseRequestTestsOnPostgres(PostgresFixture fixture) : CloseRequestTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class CloseRequestTestsOnSqlite(SqliteFixture fixture) : CloseRequestTests(fixture);
