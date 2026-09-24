namespace HVTradingBot.Domain.Trading;

public enum TradeDecisionStatus
{
    NoTrade = 0,
    Observe = 1,
    Candidate = 2,
    RejectedByRisk = 3,
    ApprovalRequired = 4,
    Approved = 5,
    Executed = 6,
    Expired = 7
}
