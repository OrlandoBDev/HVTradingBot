using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>Sends queued notifications in the background with retries, using the settings in force at send time.</summary>
public sealed class EmailNotificationDispatcher(
    TradeDecisionNotificationQueue queue,
    IEmailSender emailSender,
    IEmailSettingsProvider settingsProvider,
    ILogger<EmailNotificationDispatcher> logger,
    Func<bool, string?, CancellationToken, Task>? recordAttempt = null) : BackgroundService
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
        EmailSettings settings;
        try
        {
            settings = await settingsProvider.RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load email settings; using the last known settings");
            settings = settingsProvider.Current;
        }

        if (!settings.ShouldSend(notification.Kind))
        {
            return; // switched off since it was queued
        }

        var message = TradeDecisionEmailFormatter.Format(notification, settings.SubjectPrefix);
        var maxAttempts = Math.Max(1, settings.MaxSendAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await emailSender.SendAsync(message, settings, cancellationToken);
                logger.LogInformation("Sent {Kind} email for {Instrument} ({DecisionId})", notification.Kind, notification.Instrument, notification.DecisionId);
                await Record(true, null, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(ex, "Failed to send {Kind} email for {DecisionId} after {Attempts} attempts", notification.Kind, notification.DecisionId, attempt);
                    await Record(false, ex.Message, cancellationToken);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(ex, "{Kind} email for {DecisionId} failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}",
                    notification.Kind, notification.DecisionId, attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task Record(bool succeeded, string? error, CancellationToken cancellationToken)
    {
        if (recordAttempt is null)
        {
            return;
        }

        try
        {
            await recordAttempt(succeeded, error, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not record email delivery status");
        }
    }
}
