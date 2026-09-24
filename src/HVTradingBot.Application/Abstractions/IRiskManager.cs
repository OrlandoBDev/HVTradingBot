using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Abstractions;

public interface IRiskManager
{
    Task<RiskDecision> EvaluateAsync(
        TradeProposal proposal,
        PortfolioRiskSnapshot portfolio,
        CancellationToken cancellationToken);
}

public sealed record PortfolioRiskSnapshot(
    decimal Equity,
    decimal DailyProfitLoss,
    int OpenPositions,
    bool KillSwitchActive,
    DateTimeOffset MarketDataAsOfUtc);

public sealed record RiskDecision(
    bool Approved,
    decimal? ApprovedQuantity,
    IReadOnlyCollection<string> Reasons);
