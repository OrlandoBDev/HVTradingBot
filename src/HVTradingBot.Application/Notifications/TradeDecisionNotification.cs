using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Application.Notifications;

/// <summary>A trade decision to report. <see cref="DedupeKey"/> identifies the signal so repeated evaluations notify once.</summary>
public sealed record TradeDecisionNotification(
    Guid DecisionId,
    string Instrument,
    string? Strategy,
    DecisionState Status,
    TradingMode Mode,
    DateTimeOffset DecidedAtUtc,
    IReadOnlyCollection<string> Reasons)
{
    public TradeSetup? Setup { get; init; }
    public int? Score { get; init; }
    public string? Regime { get; init; }
    public decimal? Quantity { get; init; }
    public string? BrokerOrderId { get; init; }
    public string? Broker { get; init; }
    public string? DedupeKey { get; init; }
}
