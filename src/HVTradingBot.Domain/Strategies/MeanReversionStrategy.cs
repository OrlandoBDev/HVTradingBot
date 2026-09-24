using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

/// <summary>Fades Bollinger-band extremes, only in ranging / low-volatility conditions. Targets the middle band.</summary>
public sealed class MeanReversionStrategy : StrategyBase
{
    public override string Name => "MeanReversion";

    public override IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } =
        [MarketRegime.Ranging, MarketRegime.LowVolatility];

    protected override StrategyResult Evaluate(MarketContext context, decimal atr)
    {
        var p = context.Primary;
        if (p is not { Rsi: { } rsi, BollingerUpper: { } upper, BollingerLower: { } lower, BollingerMiddle: { } middle })
        {
            return NoTrade("Indicators not warmed up.");
        }

        var last = context.PrimaryCandles[^1];

        if (last.Low <= lower && rsi < 32 && last.Close > last.Open)
        {
            var stopDistance = context.Quote.Ask - (last.Low - atr * 0.5m);
            var targetDistance = middle - context.Quote.Ask;
            return targetDistance > 0
                ? Trade(context, Direction.Long, stopDistance, targetDistance, $"Rejected lower band; RSI {rsi:F1}.")
                : NoTrade("Price already at or above the mean.");
        }

        if (last.High >= upper && rsi > 68 && last.Close < last.Open)
        {
            var stopDistance = (last.High + atr * 0.5m) - context.Quote.Bid;
            var targetDistance = context.Quote.Bid - middle;
            return targetDistance > 0
                ? Trade(context, Direction.Short, stopDistance, targetDistance, $"Rejected upper band; RSI {rsi:F1}.")
                : NoTrade("Price already at or below the mean.");
        }

        return NoTrade("No band extreme with confirmation.");
    }
}
