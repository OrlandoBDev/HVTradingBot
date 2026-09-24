using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.Abstractions;

public sealed record BrokerAccount(string Currency, decimal Balance, decimal StartingBalance);

public sealed record BrokerOrder(
    Guid Id,
    string ClientOrderId,
    string Instrument,
    string Direction,
    decimal Units,
    decimal? FillPrice,
    OrderStatus Status,
    string? RejectReason,
    DateTime CreatedAtUtc);

/// <summary>
/// Broker abstraction from docs/ARCHITECTURE.md. Only the execution service may call order methods.
/// </summary>
public interface IBroker
{
    Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<OpenPosition>> GetPositionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Candle>> GetCandlesAsync(Instrument instrument, TimeFrame timeFrame, CancellationToken cancellationToken);

    /// <summary>Places an order. Must be idempotent on <see cref="TradeOrder.ClientOrderId"/>.</summary>
    Task<OrderResult> PlaceOrderAsync(TradeOrder order, CancellationToken cancellationToken);

    Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken);

    Task<OrderResult> ClosePositionAsync(string positionId, CancellationToken cancellationToken);
}

public sealed record BrokerDescriptor(string Name, string? AccountId, bool IsDemo);

/// <summary>
/// The broker the trading engine executes against: simulated (paper, backtest) or a real broker account (Deriv demo).
/// </summary>
public interface IExecutionBroker : IBroker
{
    BrokerDescriptor Descriptor { get; }

    /// <summary>
    /// Called with each closed bar. Updates the executable quote and MAE/MFE tracking, and returns positions that
    /// closed: simulated brokers fill stops/targets from the bar; live brokers reconcile with the broker's own state.
    /// </summary>
    Task<IReadOnlyList<ClosedPosition>> ProcessBarAsync(
        Instrument instrument,
        Candle bar,
        CurrencyConverter converter,
        CancellationToken cancellationToken);
}

/// <summary>
/// The broker cannot be used right now (not configured, credentials rejected, not reachable). Nothing was sent;
/// the trading loop pauses new activity and retries instead of stopping.
/// </summary>
public class BrokerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
