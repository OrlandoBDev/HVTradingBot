using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Domain.Trading;
using HVTradingBot.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVTradingBot.UnitTests;

public sealed class TradeDecisionNotificationTests
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 24, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Format_IncludesDecisionAndProposalDetails()
    {
        var notification = CreateNotification(TradeDecisionStatus.Executed) with
        {
            Quantity = 1000m,
            BrokerOrderId = "PAPER-123"
        };

        var message = TradeDecisionEmailFormatter.Format(notification, "[HVTradingBot]");

        Assert.Equal("[HVTradingBot] Executed: BUY EUR_USD (Breakout)", message.Subject);
        Assert.Contains("Trading mode: PAPER", message.Body);
        Assert.Contains("Entry: 1.105", message.Body);
        Assert.Contains("Stop loss: 1.1", message.Body);
        Assert.Contains("Take profit: 1.115", message.Body);
        Assert.Contains("Quantity: 1000", message.Body);
        Assert.Contains("Broker order id: PAPER-123", message.Body);
        Assert.Contains("- Trend aligned", message.Body);
        Assert.Contains("Decided at (UTC): 2026-09-24 13:30:00", message.Body);
    }

    [Fact]
    public void Format_WithoutProposal_OmitsDirection()
    {
        var notification = CreateNotification(TradeDecisionStatus.RejectedByRisk) with { Proposal = null };

        var message = TradeDecisionEmailFormatter.Format(notification, "[Bot]");

        Assert.Equal("[Bot] RejectedByRisk: EUR_USD (Breakout)", message.Subject);
        Assert.DoesNotContain("Stop loss", message.Body);
    }

    [Theory]
    [InlineData(TradeDecisionStatus.Executed, true)]
    [InlineData(TradeDecisionStatus.ApprovalRequired, true)]
    [InlineData(TradeDecisionStatus.NoTrade, false)]
    [InlineData(TradeDecisionStatus.Observe, false)]
    public void ShouldNotify_UsesDefaultStatusesWhenNoneConfigured(TradeDecisionStatus status, bool expected)
    {
        Assert.Equal(expected, new EmailNotificationOptions().ShouldNotify(status));
    }

    [Fact]
    public void ShouldNotify_UsesConfiguredStatuses()
    {
        var options = new EmailNotificationOptions { NotifyOnStatuses = [TradeDecisionStatus.Executed] };

        Assert.True(options.ShouldNotify(TradeDecisionStatus.Executed));
        Assert.False(options.ShouldNotify(TradeDecisionStatus.Candidate));
    }

    [Fact]
    public async Task Notifier_QueuesOnlyMatchingDecisions()
    {
        var queue = new TradeDecisionNotificationQueue();
        var notifier = new QueuedTradeDecisionNotifier(
            queue,
            Options.Create(new EmailNotificationOptions { Enabled = true }),
            NullLogger<QueuedTradeDecisionNotifier>.Instance);

        await notifier.NotifyAsync(CreateNotification(TradeDecisionStatus.NoTrade), CancellationToken.None);
        await notifier.NotifyAsync(CreateNotification(TradeDecisionStatus.Executed), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(TradeDecisionStatus.Executed, reader.Current.Status);
    }

    [Fact]
    public async Task Dispatcher_SendsFormattedEmail()
    {
        var sender = new RecordingEmailSender();
        var dispatcher = CreateDispatcher(sender, maxAttempts: 1);

        await dispatcher.SendAsync(CreateNotification(TradeDecisionStatus.Approved), CancellationToken.None);

        var message = Assert.Single(sender.Sent);
        Assert.StartsWith("[HVTradingBot] Approved:", message.Subject);
    }

    [Fact]
    public async Task Dispatcher_SwallowsSendFailures()
    {
        var sender = new RecordingEmailSender { Fail = true };
        var dispatcher = CreateDispatcher(sender, maxAttempts: 1);

        await dispatcher.SendAsync(CreateNotification(TradeDecisionStatus.Approved), CancellationToken.None);

        Assert.Equal(1, sender.Attempts);
    }

    [Fact]
    public void Registration_WhenDisabled_UsesNullNotifier()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:Email:Enabled"] = "false"
        });

        Assert.IsType<NullTradeDecisionNotifier>(provider.GetRequiredService<ITradeDecisionNotifier>());
    }

    [Fact]
    public void Registration_WhenEnabledWithoutCredentials_FailsValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:ToAddresses:0"] = "trader@example.com"
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmailNotificationOptions>>().Value);
        Assert.Contains(ex.Failures, f => f.Contains("App Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_WhenEnabled_UsesQueuedNotifier()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:Username"] = "bot@gmail.com",
            ["Notifications:Email:Password"] = "app-password",
            ["Notifications:Email:FromAddress"] = "",
            ["Notifications:Email:ToAddresses:0"] = "trader@example.com"
        });

        Assert.IsType<QueuedTradeDecisionNotifier>(provider.GetRequiredService<ITradeDecisionNotifier>());
        Assert.Equal("smtp.gmail.com", provider.GetRequiredService<IOptions<EmailNotificationOptions>>().Value.SmtpHost);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradeDecisionNotifications(configuration);
        return services.BuildServiceProvider();
    }

    private static EmailNotificationDispatcher CreateDispatcher(IEmailSender sender, int maxAttempts) =>
        new(new TradeDecisionNotificationQueue(),
            sender,
            Options.Create(new EmailNotificationOptions { Enabled = true, MaxSendAttempts = maxAttempts }),
            NullLogger<EmailNotificationDispatcher>.Instance);

    private static TradeDecisionNotification CreateNotification(TradeDecisionStatus status) =>
        new(Guid.NewGuid(), "EUR_USD", "Breakout", status, TradingMode.Paper, DecidedAt, ["Trend aligned"])
        {
            Proposal = new TradeProposal(Guid.NewGuid(), "EUR_USD", TradeDirection.Buy, "Breakout",
                1.105m, 1.100m, 1.115m, 0.82m, DecidedAt, DecidedAt.AddMinutes(15))
        };

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public int Attempts { get; private set; }
        public bool Fail { get; init; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Fail)
            {
                throw new InvalidOperationException("SMTP unavailable");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
