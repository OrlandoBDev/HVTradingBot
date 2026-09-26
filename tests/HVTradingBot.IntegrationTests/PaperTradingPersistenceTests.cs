using Microsoft.EntityFrameworkCore;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Persistence.Stores;

namespace HVTradingBot.IntegrationTests;

public abstract class PaperTradingPersistenceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CurrencyConverter Converter = new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.1m });

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Candle Bar(DateTime open, decimal low, decimal high, decimal close) =>
        new(open, TimeFrame.M5, close, high, low, close, 0.0001m, 10);

    private static TradeOrder Order(string id = "EURUSD-T-L-1") =>
        new(id, Instruments.EurUsd, Direction.Long, 100_000m, 1.1m, 1.0980m, 1.1040m, 200m, "Test", 80, null, "corr");

    [Fact]
    public async Task Concurrent_submissions_with_same_client_order_id_create_exactly_one_position()
    {
        var brokers = Enumerable.Range(0, 4).Select(_ => fixture.NewBroker()).ToList();
        foreach (var b in brokers)
        {
            await b.ProcessBarAsync(Instruments.EurUsd, Bar(T0, 1.0995m, 1.1005m, 1.1m), Converter, CancellationToken.None);
        }

        var results = await Task.WhenAll(brokers.Select(b => b.PlaceOrderAsync(Order(), CancellationToken.None)));

        Assert.Single(results, r => r.Status == OrderStatus.Filled);
        Assert.All(results.Where(r => r.Status != OrderStatus.Filled), r => Assert.Equal(OrderStatus.Duplicate, r.Status));
        Assert.Single(await brokers[0].GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Open_positions_survive_a_restart_and_close_on_stop_loss()
    {
        var before = fixture.NewBroker();
        await before.ProcessBarAsync(Instruments.EurUsd, Bar(T0, 1.0995m, 1.1005m, 1.1m), Converter, CancellationToken.None);
        var fill = await before.PlaceOrderAsync(Order(), CancellationToken.None);
        Assert.Equal(OrderStatus.Filled, fill.Status);

        // "Restart": a brand-new broker instance with no in-memory state.
        var after = fixture.NewBroker();
        var restored = Assert.Single(await after.GetPositionsAsync(CancellationToken.None));
        Assert.Equal("EURUSD-T-L-1", restored.ClientOrderId);

        var closed = await after.ProcessBarAsync(Instruments.EurUsd, Bar(T0.AddMinutes(5), 1.0970m, 1.1001m, 1.0975m), Converter, CancellationToken.None);

        var trade = Assert.Single(closed);
        Assert.Equal(ExitReason.StopLoss, trade.Reason);
        Assert.True(trade.RealizedPnl < 0);
        Assert.Empty(await after.GetPositionsAsync(CancellationToken.None));
        var account = await after.GetAccountAsync(CancellationToken.None);
        Assert.Equal(account.StartingBalance + trade.RealizedPnl, account.Balance);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.True((await db.Positions.SingleAsync()).Commission > 0);
    }

    [Fact]
    public async Task Concurrent_state_updates_are_not_lost()
    {
        var store = new EfTradingStateStore(fixture.DbFactory);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            store.UpdateAsync(s => s with { ConsecutiveLosses = s.ConsecutiveLosses + 1 }, CancellationToken.None)));

        Assert.Equal(8, (await store.GetAsync(CancellationToken.None)).ConsecutiveLosses);
    }

    [Fact]
    public async Task Kill_switch_state_round_trips()
    {
        var store = new EfTradingStateStore(fixture.DbFactory);
        await store.UpdateAsync(s => s.WithKillSwitch(true, "test", T0), CancellationToken.None);

        var state = await store.GetAsync(CancellationToken.None);
        Assert.True(state.KillSwitchActive);
        Assert.Equal("test", state.KillSwitchReason);
        Assert.Equal(TradingMode.Paper, state.Mode);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class PaperTradingPersistenceTestsOnPostgres(PostgresFixture fixture) : PaperTradingPersistenceTests(fixture);

[Collection(SqliteCollection.Name)]
public sealed class PaperTradingPersistenceTestsOnSqlite(SqliteFixture fixture) : PaperTradingPersistenceTests(fixture);
