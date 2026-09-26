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
    string ClientOrderId)
{
    /// <summary>
    /// A test trade requested from the dashboard. It may use a Derived market even when every Derived slot is taken
    /// (it is closed after a minute); every other rule applies as usual.
    /// </summary>
    public bool IsTestTrade { get; init; }
}

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
