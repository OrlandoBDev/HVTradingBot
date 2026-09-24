using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>A concrete trade idea: where to enter, where it is wrong (stop), and where to take profit.</summary>
public sealed record TradeSetup(Direction Direction, decimal Entry, decimal StopLoss, decimal TakeProfit)
{
    public decimal RiskDistance => Math.Abs(Entry - StopLoss);

    public decimal RewardDistance => Math.Abs(TakeProfit - Entry);

    public decimal RewardToRisk => RiskDistance == 0 ? 0 : RewardDistance / RiskDistance;

    public bool IsValid =>
        RiskDistance > 0
        && (Direction == Direction.Long
            ? StopLoss < Entry && TakeProfit > Entry
            : StopLoss > Entry && TakeProfit < Entry);
}

/// <summary>Outcome of one strategy evaluation. <see cref="Setup"/> is null for NO_TRADE.</summary>
public sealed record StrategyResult(
    string Strategy,
    TradeSetup? Setup,
    string Reason,
    IReadOnlyCollection<MarketRegime> CompatibleRegimes)
{
    public bool IsNoTrade => Setup is null;

    public static StrategyResult NoTrade(string strategy, string reason, IReadOnlyCollection<MarketRegime> regimes) =>
        new(strategy, null, reason, regimes);

    public static StrategyResult Trade(string strategy, TradeSetup setup, string reason, IReadOnlyCollection<MarketRegime> regimes)
    {
        if (!setup.IsValid)
        {
            throw new ArgumentException($"{strategy} produced an invalid setup: {setup}.", nameof(setup));
        }

        return new StrategyResult(strategy, setup, reason, regimes);
    }
}
