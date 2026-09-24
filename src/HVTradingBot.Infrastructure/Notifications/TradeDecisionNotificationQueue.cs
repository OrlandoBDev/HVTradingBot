using System.Threading.Channels;
using HVTradingBot.Application.Notifications;

namespace HVTradingBot.Infrastructure.Notifications;

public sealed class TradeDecisionNotificationQueue
{
    private const int Capacity = 500;

    private readonly Channel<TradeDecisionNotification> _channel =
        Channel.CreateBounded<TradeDecisionNotification>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool TryEnqueue(TradeDecisionNotification notification) => _channel.Writer.TryWrite(notification);

    public IAsyncEnumerable<TradeDecisionNotification> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
