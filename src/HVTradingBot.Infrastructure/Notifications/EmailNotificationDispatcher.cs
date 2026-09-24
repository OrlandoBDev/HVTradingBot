using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure.Notifications;

public sealed class EmailNotificationDispatcher(
    TradeDecisionNotificationQueue queue,
    IEmailSender emailSender,
    IOptions<EmailNotificationOptions> options,
    ILogger<EmailNotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notification in queue.ReadAllAsync(stoppingToken))
            {
                await SendAsync(notification, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task SendAsync(TradeDecisionNotification notification, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var message = TradeDecisionEmailFormatter.Format(notification, settings.SubjectPrefix);
        var maxAttempts = Math.Max(1, settings.MaxSendAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await emailSender.SendAsync(message, cancellationToken);
                logger.LogInformation(
                    "Sent trade decision email for {DecisionId} ({Status} {Instrument}).",
                    notification.DecisionId, notification.Status, notification.Instrument);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(ex,
                        "Failed to send trade decision email for {DecisionId} after {Attempts} attempts.",
                        notification.DecisionId, attempt);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(ex,
                    "Trade decision email for {DecisionId} failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}.",
                    notification.DecisionId, attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
