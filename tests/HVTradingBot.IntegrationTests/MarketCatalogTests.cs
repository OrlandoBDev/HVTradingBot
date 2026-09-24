using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MarketCatalogTests(PostgresFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        db.Markets.RemoveRange(db.Markets);
        db.MarketSelection.RemoveRange(db.MarketSelection);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MarketCatalogStore Store() => new(fixture.DbFactory, new Clock());

    private static MarketEntity Market(string brokerSymbol, string symbol, string assetClass, bool tradable) => new()
    {
        BrokerSymbol = brokerSymbol, Symbol = symbol, Name = symbol, Market = "m", Submarket = "s", AssetClass = assetClass,
        BaseCurrency = symbol, QuoteCurrency = "USD", PipSize = 0.01m, PriceDecimals = 2, Multipliers = tradable ? "[40,100]" : "[]",
        IsTradable = tradable, IsOpen = true
    };

    [Fact]
    public async Task Catalog_round_trips_and_registers_instruments()
    {
        var store = Store();
        await store.SaveCatalogAsync([Market("R_75", "R_75", "SyntheticIndex", true), Market("OTC_NDX", "OTC_NDX", "StockIndex", false)], CancellationToken.None);

        Assert.Equal(2, await store.LoadAndRegisterAsync(CancellationToken.None));
        Assert.True(Domain.MarketData.Instruments.TryGet("R_75", out var r75));
        Assert.True(r75.IsTradable);
        Assert.False(Domain.MarketData.Instruments.Get("OTC_NDX").IsTradable);
    }

    [Fact]
    public async Task Selection_is_versioned_and_starts_unapplied()
    {
        var store = Store();
        await store.SaveCatalogAsync([Market("R_75", "R_75", "SyntheticIndex", true)], CancellationToken.None);

        var saved = await store.SaveSelectionAsync(["EUR/USD", "r_75"], "test", CancellationToken.None);
        Assert.Equal(["EUR/USD", "R_75"], saved.Instruments);
        Assert.Equal(1, saved.Version);
        Assert.Equal(0, saved.AppliedVersion);

        await store.MarkAppliedAsync(1, CancellationToken.None);
        Assert.Equal(1, (await store.GetSelectionAsync(CancellationToken.None)).AppliedVersion);
    }

    [Fact]
    public async Task Unknown_or_empty_selections_are_rejected()
    {
        var store = Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveSelectionAsync([], "test", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveSelectionAsync(["NOPE"], "test", CancellationToken.None));
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    }
}
