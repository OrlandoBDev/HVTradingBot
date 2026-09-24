using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Application.Notifications;

/// <summary>
/// Something worth an email: a trade opened or closed, an order refused, or a kill-switch change.
/// <see cref="DedupeKey"/> identifies the event so the 5-minute re-evaluation does not send it twice.
/// </summary>
public sealed record TradeDecisionNotification(
    Guid DecisionId,
    string Instrument,
    string? Strategy,
    DecisionState Status,
    TradingMode Mode,
    DateTimeOffset DecidedAtUtc,
    IReadOnlyCollection<string> Reasons)
{
    public NotificationKind Kind { get; init; } = KindFor(Status);
    public TradeSetup? Setup { get; init; }
    public int? Score { get; init; }
    public string? Regime { get; init; }
    public decimal? Quantity { get; init; }
    public string? BrokerOrderId { get; init; }
    public string? Broker { get; init; }
    public string? DedupeKey { get; init; }

    // Trade closed
    public Direction? Direction { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public string? ExitReason { get; init; }
    public decimal? RealizedPnl { get; init; }
    public decimal? RMultiple { get; init; }
    public string? Currency { get; init; }

    // Kill switch
    public bool? KillSwitchActive { get; init; }

    public static NotificationKind KindFor(DecisionState status) => status switch
    {
        DecisionState.Executed => NotificationKind.TradeOpened,
        DecisionState.ApprovalRequired or DecisionState.Approved => NotificationKind.ApprovalRequired,
        _ => NotificationKind.OrderRejected
    };
}
