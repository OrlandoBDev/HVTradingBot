using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>Requires an established level, a valid break, a retest of the level, and a confirming close.</summary>
public sealed class BreakoutRetestStrategy : StrategyBase
{
    public override string Name => "BreakoutRetest";

    public override IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } =
        [MarketRegime.TrendingBullish, MarketRegime.TrendingBearish, MarketRegime.Ranging, MarketRegime.HighVolatility];

    protected override StrategyResult Evaluate(MarketContext context, decimal atr)
    {
        var s = context.PrimaryStructure;
        var trendDirection = RegimeDirection(context.Regime);

        if (s is { BullishRetest: true, Resistance: { } resistance } && trendDirection != Direction.Short)
        {
            var stopDistance = context.Quote.Ask - (resistance - atr);
            return Trade(context, Direction.Long, stopDistance, stopDistance * 2.5m,
                $"Break and retest of resistance {resistance}.");
        }

        if (s is { BearishRetest: true, Support: { } support } && trendDirection != Direction.Long)
        {
            var stopDistance = (support + atr) - context.Quote.Bid;
            return Trade(context, Direction.Short, stopDistance, stopDistance * 2.5m,
                $"Break and retest of support {support}.");
        }

        return NoTrade(s.FalseBreakoutCandidate ? "False-breakout candidate; no confirmed retest." : "No break-and-retest pattern.");
    }
}
