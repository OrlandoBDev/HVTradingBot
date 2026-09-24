using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Execution;

public sealed class ExecutionCostOptions
{
    /// <summary>Slippage applied against the trader on every simulated market fill and stop exit.</summary>
    public decimal SlippagePips { get; set; } = 0.2m;

    /// <summary>Commission per 100,000 units per side, in account currency.</summary>
    public decimal CommissionPer100K { get; set; } = 3m;
}

/// <summary>
/// Fill and exit rules shared by the paper broker and the backtester, so both simulate trades identically.
/// Longs buy at the ask and exit at the bid; shorts sell at the bid and exit at the ask.
/// </summary>
public static class PaperExecutionModel
{
    public static decimal EntryFill(Direction direction, Quote quote, ExecutionCostOptions costs)
    {
        var slippage = quote.Instrument.FromPips(costs.SlippagePips);
        return quote.Instrument.RoundPrice(direction == Direction.Long ? quote.Ask + slippage : quote.Bid - slippage);
    }

    /// <summary>
    /// Checks a closed bar against the position's stop and target. If both were touched in the same bar the
    /// stop is assumed to have filled first (conservative).
    /// </summary>
    public static (ExitReason Reason, decimal Price)? CheckExit(OpenPosition position, Candle bar, ExecutionCostOptions costs)
    {
        var slippage = position.Instrument.FromPips(costs.SlippagePips);
        var half = bar.HalfSpread;

        if (position.Direction == Direction.Long)
        {
            var bidLow = bar.Low - half;
            var bidHigh = bar.High - half;
            var bidOpen = bar.Open - half;
            if (bidLow <= position.StopLoss)
            {
                // Gap through the stop fills at the worse open price.
                var price = Math.Min(position.StopLoss, bidOpen) - slippage;
                return (ExitReason.StopLoss, position.Instrument.RoundPrice(price));
            }

            if (bidHigh >= position.TakeProfit)
            {
                return (ExitReason.TakeProfit, position.TakeProfit);
            }
        }
        else
        {
            var askHigh = bar.High + half;
            var askLow = bar.Low + half;
            var askOpen = bar.Open + half;
            if (askHigh >= position.StopLoss)
            {
                var price = Math.Max(position.StopLoss, askOpen) + slippage;
                return (ExitReason.StopLoss, position.Instrument.RoundPrice(price));
            }

            if (askLow <= position.TakeProfit)
            {
                return (ExitReason.TakeProfit, position.TakeProfit);
            }
        }

        return null;
    }

    /// <summary>Profit in account currency, net of round-trip commission.</summary>
    public static decimal RealizedPnl(OpenPosition position, decimal exitPrice, decimal quoteToAccountRate, ExecutionCostOptions costs)
    {
        var gross = position.Direction.Sign() * (exitPrice - position.EntryPrice) * position.Units * quoteToAccountRate;
        var commission = 2m * costs.CommissionPer100K * position.Units / 100_000m;
        return Math.Round(gross - commission, 2, MidpointRounding.ToEven);
    }

    public static decimal UnrealizedPnl(OpenPosition position, Quote quote, decimal quoteToAccountRate)
    {
        var exit = position.Direction == Direction.Long ? quote.Bid : quote.Ask;
        return Math.Round(position.Direction.Sign() * (exit - position.EntryPrice) * position.Units * quoteToAccountRate, 2);
    }

    /// <summary>Updates maximum favourable / adverse excursion (in price units) using the bar's range.</summary>
    public static OpenPosition TrackExcursion(OpenPosition position, Candle bar)
    {
        var favorable = position.Direction == Direction.Long ? bar.High - position.EntryPrice : position.EntryPrice - bar.Low;
        var adverse = position.Direction == Direction.Long ? position.EntryPrice - bar.Low : bar.High - position.EntryPrice;
        return position with
        {
            MaxFavorableExcursion = Math.Max(position.MaxFavorableExcursion, Math.Max(0, favorable)),
            MaxAdverseExcursion = Math.Max(position.MaxAdverseExcursion, Math.Max(0, adverse))
        };
    }

    public static decimal RMultiple(OpenPosition position, decimal realizedPnl) =>
        position.InitialRiskAmount == 0 ? 0 : Math.Round(realizedPnl / position.InitialRiskAmount, 2);
}
