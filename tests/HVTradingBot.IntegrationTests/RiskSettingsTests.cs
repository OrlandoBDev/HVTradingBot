using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class RiskSettingsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly RiskOptions Configured = new() { MaxRiskPerTradePercent = 1, MaxDailyLossPercent = 3, MaxWeeklyLossPercent = 8, MaxOpenPositions = 2 };

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private RiskSettingsStore Store() => new(fixture.DbFactory, new Clock());

    private static RiskLimits Limits(decimal risk = 2, decimal daily = 5, decimal weekly = 12, decimal feeCap = 0.25m) =>
        new(risk, daily, weekly, 3, 2, 3, 240, 2, feeCap);

    [Fact]
    public async Task Configured_defaults_apply_until_limits_are_saved()
    {
        var source = new RiskOptionsSource(Configured, Store(), NullLogger<RiskOptionsSource>.Instance);

        var view = await source.RefreshAsync(CancellationToken.None);

        Assert.False(view.IsCustomized);
        Assert.Equal(1m, source.Current.MaxRiskPerTradePercent);
        Assert.NotSame(Configured, source.Current); // a snapshot, not the shared configuration object
    }

    [Fact]
    public async Task Saved_limits_replace_the_snapshot_and_reset_restores_defaults()
    {
        var store = Store();
        var source = new RiskOptionsSource(Configured, store, NullLogger<RiskOptionsSource>.Instance);
        await source.RefreshAsync(CancellationToken.None);
        var before = source.Current;

        await store.SaveAsync(Limits(), "test", CancellationToken.None);
        await source.RefreshAsync(CancellationToken.None);

        Assert.Equal(2m, source.Current.MaxRiskPerTradePercent);
        Assert.Equal(3, source.Current.MaxOpenPositions);
        Assert.Equal(1m, before.MaxRiskPerTradePercent); // earlier snapshots are never mutated
        Assert.Equal(Configured.MaxMarketDataAgeSeconds, source.Current.MaxMarketDataAgeSeconds); // non-editable values kept

        await store.SaveAsync(null, "test", CancellationToken.None);
        Assert.False((await source.RefreshAsync(CancellationToken.None)).IsCustomized);
        Assert.Equal(1m, source.Current.MaxRiskPerTradePercent);
    }

    [Fact]
    public async Task Invalid_stored_limits_are_ignored()
    {
        var store = Store();
        await store.SaveAsync(Limits(risk: 25), "test", CancellationToken.None);
        var source = new RiskOptionsSource(Configured, store, NullLogger<RiskOptionsSource>.Instance);

        await source.RefreshAsync(CancellationToken.None);

        Assert.Equal(1m, source.Current.MaxRiskPerTradePercent);
    }

    [Theory]
    [InlineData(5, 10, 20, 0.25, "MaxRiskPerTradePercent")]   // risk per trade above 3%
    [InlineData(2, 1, 20, 0.25, "MaxDailyLossPercent")]       // daily below per-trade risk
    [InlineData(1, 5, 3, 0.25, "MaxWeeklyLossPercent")]       // weekly below daily
    [InlineData(1, 3, 8, 0.9, "MaxCommissionShareOfRisk")]    // fee cap cannot be switched off
    public void Dashboard_limits_cannot_leave_the_safe_ranges(double risk, double daily, double weekly, double feeCap, string field) =>
        Assert.Contains(field, Limits((decimal)risk, (decimal)daily, (decimal)weekly, (decimal)feeCap).Validate().Keys);

    [Fact]
    public async Task Risk_manager_uses_the_new_limits_immediately()
    {
        var store = Store();
        var source = new RiskOptionsSource(Configured, store, NullLogger<RiskOptionsSource>.Instance);
        await source.RefreshAsync(CancellationToken.None);
        await store.SaveAsync(Limits(feeCap: 0.4m), "test", CancellationToken.None);
        await source.RefreshAsync(CancellationToken.None);

        Assert.Equal(0.4m, source.Current.MaxCommissionShareOfRisk);
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    }
}
