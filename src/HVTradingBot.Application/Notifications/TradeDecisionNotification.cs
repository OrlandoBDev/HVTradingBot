using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Notifications;

public sealed record TradeDecisionNotification(
    Guid DecisionId,
    string Instrument,
    string Strategy,
    TradeDecisionStatus Status,
    TradingMode Mode,
    DateTimeOffset DecidedAtUtc,
    IReadOnlyCollection<string> Reasons)
{
    public TradeProposal? Proposal { get; init; }
    public decimal? Quantity { get; init; }
    public string? BrokerOrderId { get; init; }
}
