using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;

namespace HVTradingBot.Infrastructure.Notifications;

public sealed class NullTradeDecisionNotifier : ITradeDecisionNotifier
{
    public ValueTask NotifyAsync(TradeDecisionNotification notification, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
