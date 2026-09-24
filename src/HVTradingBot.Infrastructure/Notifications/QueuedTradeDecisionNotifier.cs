using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>
/// Queues trade decisions for email delivery so SMTP latency or failures never delay trading. The engine re-evaluates
/// every 5 minutes, so the same signal and status are emailed only once (remembered for the last 2,000 signals).
/// </summary>
public sealed class QueuedTradeDecisionNotifier(
    TradeDecisionNotificationQueue queue,
    IOptions<EmailNotificationOptions> options,
    ILogger<QueuedTradeDecisionNotifier> logger) : ITradeDecisionNotifier
{
    private const int RememberedSignals = 2000;
    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Lock _lock = new();

    public ValueTask NotifyAsync(TradeDecisionNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!options.Value.ShouldNotify(notification.Status) || IsRepeat(notification))
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

    private bool IsRepeat(TradeDecisionNotification notification)
    {
        if (string.IsNullOrEmpty(notification.DedupeKey))
        {
            return false;
        }

        var key = $"{notification.DedupeKey}|{notification.Status}";
        lock (_lock)
        {
            if (!_sent.Add(key))
            {
                return true;
            }

            _order.Enqueue(key);
            if (_order.Count > RememberedSignals)
            {
                _sent.Remove(_order.Dequeue());
            }

            return false;
        }
    }
}
