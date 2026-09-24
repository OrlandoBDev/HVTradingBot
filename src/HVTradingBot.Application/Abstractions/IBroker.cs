using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Abstractions;

public interface IBroker
{
    Task<IReadOnlyCollection<Candle>> GetCandlesAsync(
        string instrument,
        TimeFrame timeFrame,
        int count,
        CancellationToken cancellationToken);

    Task<BrokerExecutionResult> PlaceOrderAsync(
        BrokerOrderRequest order,
        CancellationToken cancellationToken);
}

public sealed record BrokerOrderRequest(
    Guid ClientOrderId,
    string Instrument,
    TradeDirection Direction,
    decimal Quantity,
    decimal? StopLoss,
    decimal? TakeProfit);

public sealed record BrokerExecutionResult(
    bool Accepted,
    string? BrokerOrderId,
    string? RejectionReason);
