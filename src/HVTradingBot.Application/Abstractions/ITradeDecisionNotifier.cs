using HVTradingBot.Application.Notifications;

namespace HVTradingBot.Application.Abstractions;

/// <summary>
/// Publishes trade decisions to notification channels. Implementations must never
/// block or fail the trading flow: delivery happens asynchronously and errors are logged.
/// </summary>
public interface ITradeDecisionNotifier
{
    ValueTask NotifyAsync(
        TradeDecisionNotification notification,
        CancellationToken cancellationToken);
}
