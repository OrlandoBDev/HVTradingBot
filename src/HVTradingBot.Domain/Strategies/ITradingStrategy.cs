using HVTradingBot.Domain.Analysis;

namespace HVTradingBot.Domain.Strategies;

/// <summary>
/// A strategy returns either a structured candidate or NO_TRADE. Implementations must be deterministic and must only
/// read closed bars from the context, so that backtests and runtime share the exact same logic.
/// </summary>
public interface ITradingStrategy
{
    string Name { get; }

    IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; }

    Task<StrategyResult> EvaluateAsync(MarketContext context, CancellationToken cancellationToken);
}
