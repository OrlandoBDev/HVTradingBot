using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.UnitTests;

public sealed class TradeDecisionNotificationTests
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 24, 13, 30, 0, TimeSpan.Zero);

    private static readonly EmailSettings Enabled = EmailSettings.Disabled with
    {
        Enabled = true, Username = "bot@gmail.com", Password = "app-password", ToAddresses = ["trader@example.com"]
    };

    [Fact]
    public void Trade_opened_email_includes_the_setup()
    {
        var notification = CreateNotification(DecisionState.Executed) with { Quantity = 1000m, BrokerOrderId = "PAPER-123" };

        var message = TradeDecisionEmailFormatter.Format(notification, "[HVTradingBot]");

        Assert.Equal("[HVTradingBot] Trade opened: BUY EUR/USD @ 1.105 (Breakout)", message.Subject);
        Assert.Contains("Trading mode: PAPER", message.Body);
        Assert.Contains("Stop loss: 1.1", message.Body);
        Assert.Contains("Take profit: 1.115", message.Body);
        Assert.Contains("Quantity: 1000", message.Body);
        Assert.Contains("Score: 82", message.Body);
        Assert.Contains("Broker: Deriv (demo) DOT1", message.Body);
        Assert.Contains("- Trend aligned", message.Body);
    }

    [Fact]
    public void Trade_closed_email_reports_the_result()
    {
        var closed = CreateNotification(DecisionState.Executed) with
        {
            Kind = NotificationKind.TradeClosed, Setup = null, Direction = Direction.Short, EntryPrice = 1.105m, ExitPrice = 1.095m,
            ExitReason = "TakeProfit", RealizedPnl = 24.5m, RMultiple = 2.04m, Currency = "USD"
        };

        var message = TradeDecisionEmailFormatter.Format(closed, "[HV]");

        Assert.Equal("[HV] Trade closed: SELL EUR/USD TakeProfit +24.50 USD (+2.0R)", message.Subject);
        Assert.Contains("Profit / loss: +24.50 USD", message.Body);
        Assert.Contains("Closed by: TakeProfit", message.Body);
    }

    [Fact]
    public void Kill_switch_email_explains_the_state()
    {
        var message = TradeDecisionEmailFormatter.Format(
            CreateNotification(DecisionState.NoTrade) with { Kind = NotificationKind.KillSwitch, KillSwitchActive = true, Setup = null }, "[HV]");
        Assert.Equal("[HV] Kill switch ACTIVATED", message.Subject);
    }

    [Theory]
    [InlineData(NotificationKind.TradeOpened, true)]
    [InlineData(NotificationKind.TradeClosed, true)]
    [InlineData(NotificationKind.OrderRejected, false)]
    [InlineData(NotificationKind.KillSwitch, true)]
    public void Default_events_are_opened_closed_and_kill_switch(NotificationKind kind, bool expected) =>
        Assert.Equal(expected, Enabled.ShouldSend(kind));

    [Fact]
    public void Nothing_is_sent_while_disabled() =>
        Assert.False(EmailSettings.Disabled.ShouldSend(NotificationKind.TradeOpened));

    [Fact]
    public async Task Notifier_queues_only_enabled_events_once()
    {
        var queue = new TradeDecisionNotificationQueue();
        var notifier = new QueuedTradeDecisionNotifier(queue, new FixedSettings(Enabled), NullLogger<QueuedTradeDecisionNotifier>.Instance);

        var opened = CreateNotification(DecisionState.Executed) with { DedupeKey = "EURUSD-Breakout-L-202609241300" };
        await notifier.NotifyAsync(CreateNotification(DecisionState.RejectedByRisk), CancellationToken.None); // switched off by default
        await notifier.NotifyAsync(opened, CancellationToken.None);
        await notifier.NotifyAsync(opened with { DecisionId = Guid.NewGuid() }, CancellationToken.None); // same signal re-evaluated
        await notifier.NotifyAsync(opened with { Kind = NotificationKind.TradeClosed, DedupeKey = "closed-EURUSD" }, CancellationToken.None);

        Assert.Equal([NotificationKind.TradeOpened, NotificationKind.TradeClosed], await Drain(queue));
    }

    [Fact]
    public async Task Dispatcher_sends_with_current_settings_and_records_the_result()
    {
        var sender = new RecordingEmailSender();
        var attempts = new List<bool>();
        var dispatcher = new EmailNotificationDispatcher(new TradeDecisionNotificationQueue(), sender, new FixedSettings(Enabled with { MaxSendAttempts = 1 }),
            NullLogger<EmailNotificationDispatcher>.Instance, (ok, _, _) => { attempts.Add(ok); return Task.CompletedTask; });

        await dispatcher.SendAsync(CreateNotification(DecisionState.Executed), CancellationToken.None);

        var (message, settings) = Assert.Single(sender.Sent);
        Assert.StartsWith("[HVTradingBot] Trade opened:", message.Subject);
        Assert.Equal(["trader@example.com"], settings.ToAddresses);
        Assert.Equal([true], attempts);
    }

    [Fact]
    public async Task Dispatcher_skips_events_switched_off_after_queueing()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = new EmailNotificationDispatcher(new TradeDecisionNotificationQueue(), sender,
            new FixedSettings(Enabled with { OnTradeOpened = false }), NullLogger<EmailNotificationDispatcher>.Instance);

        await dispatcher.SendAsync(CreateNotification(DecisionState.Executed), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Dispatcher_swallows_send_failures_and_records_them()
    {
        var sender = new RecordingEmailSender { Fail = true };
        string? recorded = null;
        var dispatcher = new EmailNotificationDispatcher(new TradeDecisionNotificationQueue(), sender, new FixedSettings(Enabled with { MaxSendAttempts = 1 }),
            NullLogger<EmailNotificationDispatcher>.Instance, (_, error, _) => { recorded = error; return Task.CompletedTask; });

        await dispatcher.SendAsync(CreateNotification(DecisionState.Executed), CancellationToken.None);

        Assert.Equal(1, sender.Attempts);
        Assert.Equal("SMTP unavailable", recorded);
    }

    [Fact]
    public void Worker_registration_provides_notifier_and_dispatcher()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddTradeDecisionNotifications(configuration);
        services.AddTradeDecisionNotifications(configuration); // idempotent

        Assert.Single(services, d => d.ServiceType == typeof(EmailSettingsStore));
        Assert.Contains(services, d => d.ServiceType == typeof(Application.Abstractions.ITradeDecisionNotifier) && d.ImplementationType == typeof(QueuedTradeDecisionNotifier));
    }

    [Theory]
    [InlineData(true, "bot@gmail.com", "pw", "", "toAddresses")]
    [InlineData(true, "", "pw", "a@b.com", "username")]
    [InlineData(true, "bot@gmail.com", null, "a@b.com", "password")]
    [InlineData(false, "", null, "not-an-email", "toAddresses")]
    public void Settings_validation(bool enabled, string username, string? password, string recipients, string field)
    {
        var input = new EmailSettingsInput(enabled, "smtp.gmail.com", 587, username, password, null, null,
            recipients.Split(',', StringSplitOptions.RemoveEmptyEntries), true, true, false, true);
        Assert.Contains(field, EmailSettingsStore.Validate(input, passwordStored: false).Keys);
    }

    [Fact]
    public void Stored_password_satisfies_validation()
    {
        var input = new EmailSettingsInput(true, "smtp.gmail.com", 587, "bot@gmail.com", null, null, null, ["a@b.com"], true, true, false, true);
        Assert.Empty(EmailSettingsStore.Validate(input, passwordStored: true));
    }

    private static async Task<List<NotificationKind>> Drain(TradeDecisionNotificationQueue queue)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var received = new List<NotificationKind>();
        try
        {
            await foreach (var n in queue.ReadAllAsync(cts.Token))
            {
                received.Add(n.Kind);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return received;
    }

    private static TradeDecisionNotification CreateNotification(DecisionState status) =>
        new(Guid.NewGuid(), "EUR/USD", "Breakout", status, TradingMode.Paper, DecidedAt, ["Trend aligned"])
        {
            Setup = new TradeSetup(Direction.Long, 1.105m, 1.100m, 1.115m),
            Score = 82,
            Broker = "Deriv (demo) DOT1"
        };

    private sealed class FixedSettings(EmailSettings settings) : IEmailSettingsProvider
    {
        public EmailSettings Current => settings;

        public Task<EmailSettings> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(EmailMessage Message, EmailSettings Settings)> Sent { get; } = [];
        public int Attempts { get; private set; }
        public bool Fail { get; init; }

        public Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Fail)
            {
                throw new InvalidOperationException("SMTP unavailable");
            }

            Sent.Add((message, settings));
            return Task.CompletedTask;
        }
    }
}
