using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Execution;

/// <summary>A market order with attached stop loss and take profit. <see cref="ClientOrderId"/> is the idempotency key.</summary>
public sealed record TradeOrder(
    string ClientOrderId,
    Instrument Instrument,
    Direction Direction,
    decimal Units,
    decimal RequestedPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal RiskAmount,
    string Strategy,
    int Score,
    Guid? DecisionId,
    string CorrelationId);

public enum OrderStatus
{
    Filled,
    Rejected,
    Duplicate,
    Unknown
}

public sealed record OrderResult(
    string ClientOrderId,
    OrderStatus Status,
    Guid? OrderId,
    Guid? PositionId,
    decimal? FillPrice,
    string? RejectReason)
{
    public static OrderResult Rejected(string clientOrderId, string reason) =>
        new(clientOrderId, OrderStatus.Rejected, null, null, null, reason);
}

public static class IdempotencyKey
{
    /// <summary>
    /// Deterministic key for one signal: the same strategy firing on the same instrument, direction and signal bar
    /// always yields the same key, so re-evaluations and retries can never open a second position.
    /// </summary>
    public static string For(Instrument instrument, string strategy, Direction direction, DateTime signalBarCloseUtc) =>
        $"{instrument.BaseCurrency}{instrument.QuoteCurrency}-{strategy}-{(direction == Direction.Long ? "L" : "S")}-{signalBarCloseUtc:yyyyMMddHHmm}";
}
