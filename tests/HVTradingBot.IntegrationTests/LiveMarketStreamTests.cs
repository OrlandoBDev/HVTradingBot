using HVTradingBot.Api.Hubs;
using HVTradingBot.Api.Services;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class LiveMarketStreamTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE candles, market_snapshots;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private LiveMarketStream Stream(params string[] instruments) =>
        new(fixture.DbFactory, new MarketCatalogStore(fixture.DbFactory, new Clock()), new TradingEngineOptions { Instruments = [.. instruments] });

    private async Task AddBarsAsync(string instrument, params DateTime[] opens)
    {
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        db.Candles.AddRange(opens.Select(o => new CandleEntity
        {
            Instrument = instrument, TimeFrame = "M5", OpenTimeUtc = o, Open = 1.1m, High = 1.2m, Low = 1.0m, Close = 1.15m,
            Spread = 0.0001m, Volume = 7
        }));
        await db.SaveChangesAsync();
    }

    private async Task SetQuoteAsync(string instrument, DateTime time, decimal bid, decimal ask)
    {
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var row = await db.MarketSnapshots.SingleOrDefaultAsync(s => s.Instrument == instrument);
        if (row is null)
        {
            row = new MarketSnapshotEntity { Instrument = instrument };
            db.MarketSnapshots.Add(row);
        }

        row.MarketTimeUtc = time;
        row.Bid = bid;
        row.Ask = ask;
        row.SpreadPips = 1.5m;
        row.UpdatedAtUtc = time;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Stored_history_is_skipped_and_only_newly_closed_bars_are_returned_once()
    {
        await AddBarsAsync("AAA", T0, T0.AddMinutes(5));
        var stream = Stream("AAA", "BBB");

        Assert.Empty((await stream.NextAsync(CancellationToken.None)).Bars);

        await AddBarsAsync("AAA", T0.AddMinutes(10));
        await AddBarsAsync("BBB", T0.AddMinutes(10)); // BBB's first bars are its history load, not a live bar
        var bars = (await stream.NextAsync(CancellationToken.None)).Bars;

        var bar = Assert.Single(bars);
        Assert.Equal(new LiveBarDto("AAA", "M5", T0.AddMinutes(10), 1.1m, 1.2m, 1.0m, 1.15m, 7), bar);
        Assert.Empty((await stream.NextAsync(CancellationToken.None)).Bars);

        await AddBarsAsync("AAA", T0.AddMinutes(15));
        await AddBarsAsync("BBB", T0.AddMinutes(15));
        bars = (await stream.NextAsync(CancellationToken.None)).Bars;
        Assert.Equal(["AAA", "BBB"], bars.Select(b => b.Instrument));
        Assert.All(bars, b => Assert.Equal(T0.AddMinutes(15), b.OpenTimeUtc));
    }

    [Fact]
    public async Task Quotes_are_returned_first_time_and_then_only_when_they_change()
    {
        await SetQuoteAsync("AAA", T0, 1.1000m, 1.1002m);
        await SetQuoteAsync("BBB", T0, 2.0000m, 2.0004m);
        var stream = Stream("AAA", "BBB");

        var first = (await stream.NextAsync(CancellationToken.None)).Ticks;
        Assert.Equal(["AAA", "BBB"], first.Select(t => t.Instrument));
        Assert.Empty((await stream.NextAsync(CancellationToken.None)).Ticks);

        await SetQuoteAsync("BBB", T0.AddSeconds(5), 2.0001m, 2.0005m);
        var tick = Assert.Single((await stream.NextAsync(CancellationToken.None)).Ticks);
        Assert.Equal(new LiveTickDto("BBB", T0.AddSeconds(5), 2.0001m, 2.0005m, 1.5m), tick);
    }

    [Fact]
    public async Task Markets_that_are_not_selected_are_not_streamed()
    {
        await AddBarsAsync("AAA", T0);
        await AddBarsAsync("ZZZ", T0);
        await SetQuoteAsync("ZZZ", T0, 1m, 1.1m);
        var stream = Stream("AAA");
        await stream.NextAsync(CancellationToken.None);

        await AddBarsAsync("ZZZ", T0.AddMinutes(5));
        await SetQuoteAsync("ZZZ", T0.AddSeconds(5), 1.01m, 1.11m);
        var update = await stream.NextAsync(CancellationToken.None);

        Assert.Empty(update.Bars);
        Assert.Empty(update.Ticks);
    }

    [Fact]
    public async Task Broadcaster_pushes_status_every_time_and_bars_and_ticks_only_when_new()
    {
        var keys = Directory.CreateTempSubdirectory("hv-keys-");
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:TradingDb", fixture.ConnectionString);
            b.UseSetting("DataProtection:KeysPath", keys.FullName);
            b.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            b.ConfigureServices(s => s.RemoveAll<IHostedService>());
        });
        var services = app.Services;
        var instrument = services.GetRequiredService<TradingEngineOptions>().Instruments[0];
        await AddBarsAsync(instrument, T0);
        await SetQuoteAsync(instrument, T0, 1.1000m, 1.1002m);

        var hub = new RecordingHubContext();
        var broadcaster = new DashboardBroadcaster(hub, services.GetRequiredService<DashboardQueries>(),
            services.GetRequiredService<LiveMarketStream>(), NullLogger<DashboardBroadcaster>.Instance);

        await broadcaster.BroadcastOnceAsync(CancellationToken.None);
        Assert.Equal([DashboardHub.StatusMessage, DashboardHub.TicksMessage], hub.Sent.Select(m => m.Method));

        hub.Sent.Clear();
        await broadcaster.BroadcastOnceAsync(CancellationToken.None);
        Assert.Equal([DashboardHub.StatusMessage], hub.Sent.Select(m => m.Method));

        hub.Sent.Clear();
        await AddBarsAsync(instrument, T0.AddMinutes(5));
        await broadcaster.BroadcastOnceAsync(CancellationToken.None);
        Assert.Equal([DashboardHub.StatusMessage, DashboardHub.BarMessage], hub.Sent.Select(m => m.Method));
        var bar = Assert.IsType<LiveBarDto>(hub.Sent[1].Args[0]);
        Assert.Equal(T0.AddMinutes(5), bar.OpenTimeUtc);
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => T0;
    }

    /// <summary>Records what the broadcaster sends to all clients.</summary>
    private sealed class RecordingHubContext : IHubContext<DashboardHub>
    {
        private readonly RecordingClients _clients = new();

        public List<(string Method, object?[] Args)> Sent => _clients.Sent;
        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients : IHubClients, IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];

        public IClientProxy All => this;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
