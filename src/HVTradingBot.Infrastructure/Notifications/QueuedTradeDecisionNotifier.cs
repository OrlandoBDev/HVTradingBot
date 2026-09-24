using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>
/// Queues trade decisions for email delivery so SMTP latency or failures never delay trading.
/// </summary>
public sealed class QueuedTradeDecisionNotifier(
    TradeDecisionNotificationQueue queue,
    IOptions<EmailNotificationOptions> options,
    ILogger<QueuedTradeDecisionNotifier> logger) : ITradeDecisionNotifier
{
    public ValueTask NotifyAsync(TradeDecisionNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!options.Value.ShouldNotify(notification.Status))
        {
            return ValueTask.CompletedTask;
        }

        if (!queue.TryEnqueue(notification))
        {
            logger.LogWarning(
                "Notification queue is full; dropped email for decision {DecisionId} ({Status} {Instrument}).",
                notification.DecisionId, notification.Status, notification.Instrument);
        }

        return ValueTask.CompletedTask;
    }
}
