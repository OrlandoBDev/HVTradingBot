using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Domain.Risk;

public sealed record TradeProposal(
    Instrument Instrument,
    TradeSetup Setup,
    Quote Quote,
    decimal AverageSpread,
    string Strategy,
    int Score,
    string ClientOrderId);

public sealed record RiskCheck(string Rule, bool Passed, string Detail);

public sealed record RiskDecision(bool IsApproved, decimal Units, decimal RiskAmount, IReadOnlyList<RiskCheck> Checks)
{
    public string? RejectionReason => IsApproved
        ? null
        : string.Join("; ", Checks.Where(c => !c.Passed).Select(c => $"{c.Rule}: {c.Detail}"));
}

public interface IRiskManager
{
    Task<RiskDecision> EvaluateAsync(TradeProposal proposal, PortfolioState portfolio, CancellationToken cancellationToken);
}
