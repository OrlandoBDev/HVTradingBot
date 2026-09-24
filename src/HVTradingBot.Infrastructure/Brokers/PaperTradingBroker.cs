using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Infrastructure.Brokers;

public sealed class PaperTradingBroker : IBroker
{
    public Task<IReadOnlyCollection<Candle>> GetCandlesAsync(
        string instrument,
        TimeFrame timeFrame,
        int count,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Candle> candles = Array.Empty<Candle>();
        return Task.FromResult(candles);
    }

    public Task<BrokerExecutionResult> PlaceOrderAsync(
        BrokerOrderRequest order,
        CancellationToken cancellationToken)
    {
        var result = new BrokerExecutionResult(
            Accepted: true,
            BrokerOrderId: $"PAPER-{order.ClientOrderId:N}",
            RejectionReason: null);

        return Task.FromResult(result);
    }
}
