namespace HVTradingBot.Domain.Common;

public enum TradingMode
{
    Paper,
    Approval,
    Auto
}

public enum Direction
{
    Long,
    Short
}

public enum DecisionState
{
    NoTrade,
    Observe,
    Candidate,
    RejectedByRisk,
    ApprovalRequired,
    Approved,
    Executed,
    Expired
}
