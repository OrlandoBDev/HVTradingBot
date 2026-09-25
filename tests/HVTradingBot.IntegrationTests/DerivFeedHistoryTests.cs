using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DerivFeedHistoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 3, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE candles;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static long Epoch(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();

    [Fact]
    public async Task Restart_reuses_stored_history_and_downloads_only_the_gap()
    {
        // Stored: 10 days of 5m bars ending one hour ago.
        var from = Now.AddDays(-10);
        var lastStored = Now.AddHours(-1).AddMinutes(-5);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            for (var t = TimeFrame.M5.BarStart(from); t <= lastStored; t = t.AddMinutes(5))
            {
                db.Candles.Add(new CandleEntity { Instrument = "EUR/USD", TimeFrame = "M5", OpenTimeUtc = t, Open = 1.1m, High = 1.1m, Low = 1.1m, Close = 1.1m, Spread = 0.0001m });
            }

            await db.SaveChangesAsync();
        }

        // Deriv returns the last hour of candles for any history request.
        var socket = new FakeDerivSocket();
        socket.Handlers["ticks"] = _ => new { tick = new { symbol = "frxEURUSD", bid = 1.10000m, ask = 1.10010m, epoch = Epoch(Now) } };
        socket.Handlers["ticks_history"] = _ => new
        {
            candles = Enumerable.Range(0, 12).Select(i => Now.AddHours(-1).AddMinutes(5 * i))
                .Select(t => new { epoch = Epoch(TimeFrame.M5.BarStart(t)), open = 1.2m, high = 1.2m, low = 1.2m, close = 1.2m }).ToArray()
        };

        var feed = new DerivMarketDataFeed(fixture.DbFactory, new DerivOptions(), new MarketDataOptions { WarmupDays = 10 },
            TradingUniverse.From([Instruments.EurUsd], "USD"), socket, new FixedClock(), NullLogger<DerivMarketDataFeed>.Instance);

        var history = await feed.LoadHistoryAsync(CancellationToken.None);

        var bars = history[Instruments.EurUsd];
        Assert.Equal(1, socket.Count("ticks_history"));                  // one small request, not a 10-day download
        Assert.Equal(1.2m, bars[^1].Close);                               // gap bars appended after the stored ones
        Assert.True(bars.Zip(bars.Skip(1)).All(p => p.Second.OpenTimeUtc > p.First.OpenTimeUtc));
        Assert.True(bars[^1].CloseTimeUtc <= Now);                        // still-forming bar excluded
    }

    [Fact]
    public async Task Tick_subscription_timeout_resubscribes_on_the_next_attempt()
    {
        var socket = new FakeDerivSocket();
        var ticks = 0;
        socket.Handlers["ticks"] = _ => ++ticks == 1
            ? throw new DerivConnectionException("No response from Deriv within 15s (ticks).")
            : new { tick = new { symbol = "frxEURUSD", bid = 1.10000m, ask = 1.10010m, epoch = Epoch(Now) } };
        socket.Handlers["ticks_history"] = _ => new
        {
            candles = new[] { new { epoch = Epoch(TimeFrame.M5.BarStart(Now.AddMinutes(-10))), open = 1.1m, high = 1.1m, low = 1.1m, close = 1.1m } }
        };

        var feed = new DerivMarketDataFeed(fixture.DbFactory, new DerivOptions(), new MarketDataOptions { WarmupDays = 1 },
            TradingUniverse.From([Instruments.EurUsd], "USD"), socket, new FixedClock(), NullLogger<DerivMarketDataFeed>.Instance);

        await Assert.ThrowsAsync<DerivConnectionException>(() => feed.LoadHistoryAsync(CancellationToken.None));
        await feed.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(2, socket.Count("ticks")); // the retry subscribed again instead of reusing the half-set-up connection
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
