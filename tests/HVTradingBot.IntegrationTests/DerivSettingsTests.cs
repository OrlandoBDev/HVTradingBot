using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DerivSettingsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Token = "pat_1234567890abcdWXYZ";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Token_is_encrypted_at_rest_and_never_exposed_in_the_view()
    {
        var store = fixture.NewSettingsStore();

        var view = await store.SaveAsync("12345", Token, null, "test", CancellationToken.None);

        Assert.True(view.TokenConfigured);
        Assert.Equal("WXYZ", view.TokenHint);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var row = await db.BrokerSettings.SingleAsync();
        Assert.DoesNotContain(Token, row.DerivApiTokenProtected);
        Assert.Equal(Token, (await store.GetAsync(CancellationToken.None))!.ApiToken);
    }

    [Fact]
    public async Task Saving_without_a_token_keeps_the_stored_token_and_bumps_the_version()
    {
        var store = fixture.NewSettingsStore();
        await store.SaveAsync("12345", Token, null, "test", CancellationToken.None);

        await store.SaveAsync("67890", null, "DOT1", "test", CancellationToken.None);

        var credentials = await store.GetAsync(CancellationToken.None);
        Assert.Equal("67890", credentials!.AppId);
        Assert.Equal(Token, credentials.ApiToken);
        Assert.Equal("DOT1", credentials.AccountId);
        Assert.Equal(2, credentials.Version);
    }

    [Fact]
    public async Task First_save_requires_a_token()
    {
        var store = fixture.NewSettingsStore();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("12345", null, null, "test", CancellationToken.None));
    }

    [Fact]
    public async Task Environment_credentials_are_a_fallback_and_database_settings_win()
    {
        var store = fixture.NewSettingsStore(new DerivEnvironmentCredentials("env-app", "env-token-1234", null));
        Assert.Equal("env-app", (await store.GetAsync(CancellationToken.None))!.AppId);
        Assert.Equal("environment", (await store.GetViewAsync(CancellationToken.None)).Source);

        await store.SaveAsync("db-app", Token, null, "test", CancellationToken.None);
        Assert.Equal("db-app", (await store.GetAsync(CancellationToken.None))!.AppId);
    }

    [Fact]
    public async Task Cleared_settings_mean_not_configured()
    {
        var store = fixture.NewSettingsStore();
        await store.SaveAsync("12345", Token, null, "test", CancellationToken.None);

        await store.ClearAsync("test", CancellationToken.None);

        Assert.Null(await store.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Session_reports_not_configured_then_connects_after_settings_are_saved_and_reconnects_on_change()
    {
        var store = fixture.NewSettingsStore();
        var socket = new FakeDerivSocket();
        var clock = new MutableClock();
        var session = new DerivSession(new DerivOptions(), new DerivRestClient(new HttpClient(new FakeDerivRest()), new DerivOptions()), socket,
            store, store, clock, NullLogger<DerivSession>.Instance);

        await Assert.ThrowsAsync<DerivNotConfiguredException>(() => session.GetSocketAsync(CancellationToken.None));
        Assert.Equal(DerivConnectionState.NotConfigured, (await store.GetStatusAsync(CancellationToken.None))!.State);

        await store.SaveAsync("12345", Token, null, "test", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(6));
        await session.GetSocketAsync(CancellationToken.None);
        var status = await store.GetStatusAsync(CancellationToken.None);
        Assert.Equal(DerivConnectionState.Connected, status!.State);
        Assert.Equal(FakeDerivRest.DemoAccount, status.ConnectedAccountId);
        Assert.Equal(2, status.Accounts.Count);

        // Pinning a real account is refused and reported, without connecting.
        await store.SaveAsync("12345", null, "CR555", "test", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(6));
        var failure = await Assert.ThrowsAsync<BrokerUnavailableException>(() => session.GetSocketAsync(CancellationToken.None));
        Assert.Contains("only demo accounts", failure.Message);
        Assert.Equal(DerivConnectionState.Failed, (await store.GetStatusAsync(CancellationToken.None))!.State);
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; private set; } = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
