using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Execution;

public enum ExitReason
{
    StopLoss,
    TakeProfit,
    Manual,
    KillSwitch
}

public sealed record OpenPosition(
    Guid Id,
    string ClientOrderId,
    Instrument Instrument,
    Direction Direction,
    decimal Units,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal InitialRiskAmount,
    DateTime OpenedAtUtc,
    string Strategy,
    int Score,
    decimal MaxFavorableExcursion,
    decimal MaxAdverseExcursion);

public sealed record ClosedPosition(
    OpenPosition Position,
    DateTime ClosedAtUtc,
    decimal ExitPrice,
    ExitReason Reason,
    decimal RealizedPnl,
    decimal RMultiple);
