using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class EmailSettingsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string AppPassword = "abcd efgh ijkl mnop";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private EmailSettingsStore Store(EmailNotificationOptions? fallback = null) =>
        new(fixture.DbFactory, fixture.DataProtectionProvider, Options.Create(fallback ?? new EmailNotificationOptions()), new Clock(),
            NullLogger<EmailSettingsStore>.Instance);

    private static EmailSettingsInput Input(string? password = AppPassword, bool enabled = true) =>
        new(enabled, "smtp.gmail.com", 587, "bot@gmail.com", password, null, "Bot", ["me@example.com", "ME@example.com", " "], true, true, false, true);

    [Fact]
    public async Task Password_is_encrypted_at_rest_and_never_in_the_view_hint_beyond_last_four()
    {
        var store = Store();
        var view = await store.SaveAsync(Input(), "test", CancellationToken.None);

        Assert.True(view.PasswordConfigured);
        Assert.Equal("mnop", view.PasswordHint);
        Assert.Equal(["me@example.com"], view.Settings.ToAddresses); // de-duplicated, blanks removed
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var row = await db.NotificationSettings.SingleAsync();
        Assert.DoesNotContain(AppPassword, row.PasswordProtected);
        Assert.Equal(AppPassword, (await store.RefreshAsync(CancellationToken.None)).Password);
    }

    [Fact]
    public async Task Saving_without_a_password_keeps_the_stored_one()
    {
        var store = Store();
        await store.SaveAsync(Input(), "test", CancellationToken.None);

        await store.SaveAsync(Input(password: null) with { SmtpPort = 465 }, "test", CancellationToken.None);

        var settings = await store.RefreshAsync(CancellationToken.None);
        Assert.Equal(AppPassword, settings.Password);
        Assert.Equal(465, settings.SmtpPort);
    }

    [Fact]
    public async Task Environment_settings_are_the_fallback_until_something_is_saved()
    {
        var store = Store(new EmailNotificationOptions { Enabled = true, Username = "env@gmail.com", Password = "envpass1234", ToAddresses = ["x@y.com"] });
        Assert.Equal("environment", (await store.GetViewAsync(CancellationToken.None)).Source);
        Assert.True((await store.RefreshAsync(CancellationToken.None)).Enabled);

        await store.SaveAsync(Input(enabled: false), "test", CancellationToken.None);
        Assert.False((await store.RefreshAsync(CancellationToken.None)).Enabled);
    }

    [Fact]
    public async Task Delivery_results_are_recorded()
    {
        var store = Store();
        await store.SaveAsync(Input(), "test", CancellationToken.None);

        await store.RecordAttemptAsync(false, "535 Authentication failed", CancellationToken.None);

        var view = await store.GetViewAsync(CancellationToken.None);
        Assert.False(view.LastAttemptSucceeded);
        Assert.Equal("535 Authentication failed", view.LastError);
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    }
}
