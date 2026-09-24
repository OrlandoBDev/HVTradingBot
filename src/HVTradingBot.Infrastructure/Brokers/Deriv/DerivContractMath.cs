using HVTradingBot.Domain.Common;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

public sealed record MultiplierContractPlan(
    string ContractType,
    int Multiplier,
    decimal Stake,
    decimal Notional,
    decimal StopLossAmount,
    decimal TakeProfitAmount);

/// <summary>
/// Maps a risk-sized position onto a Deriv multiplier contract. P&amp;L of a multiplier contract is
/// stake × multiplier × (price change / entry price) − commission, i.e. a position with notional = stake × multiplier
/// in the account currency. Stop loss and take profit are expressed as money amounts (commission included).
/// </summary>
public static class DerivContractMath
{
    public static (MultiplierContractPlan? Plan, string? Rejection) Plan(
        Direction direction,
        decimal notional,
        decimal entry,
        decimal stopLoss,
        decimal takeProfit,
        IReadOnlyList<int> allowedMultipliers,
        decimal minStake,
        decimal maxStake,
        decimal commissionRate)
    {
        if (notional <= 0 || entry <= 0)
        {
            return (null, "Position notional must be positive.");
        }

        if (allowedMultipliers.Count == 0)
        {
            return (null, "Instrument offers no multiplier contracts.");
        }

        var stopFraction = Math.Abs(entry - stopLoss) / entry;
        var targetFraction = Math.Abs(takeProfit - entry) / entry;
        var commission = notional * commissionRate;
        var stopLossAmount = Math.Round(notional * stopFraction + commission, 2, MidpointRounding.AwayFromZero);
        var takeProfitAmount = Math.Round(notional * targetFraction - commission, 2, MidpointRounding.ToZero);
        if (takeProfitAmount <= 0)
        {
            return (null, "Take profit does not cover the broker commission.");
        }

        // Lowest leverage whose stake fits the broker limit; higher multipliers only shrink the stake, which moves
        // the automatic stop-out (loss = stake) closer to the entry.
        foreach (var multiplier in allowedMultipliers.Order())
        {
            var stake = Math.Round(notional / multiplier, 2, MidpointRounding.AwayFromZero);
            if (stake > maxStake)
            {
                continue;
            }

            if (stake < minStake)
            {
                return (null, $"Stake {stake:F2} is below the broker minimum {minStake:F2}.");
            }

            if (stopLossAmount >= stake)
            {
                return (null, $"Stop loss {stopLossAmount:F2} would exceed the contract stake {stake:F2} (x{multiplier}); stop is too wide for a multiplier contract.");
            }

            var contractType = direction == Direction.Long ? "MULTUP" : "MULTDOWN";
            return (new MultiplierContractPlan(contractType, multiplier, stake, notional, stopLossAmount, takeProfitAmount), null);
        }

        return (null, $"Required stake exceeds the broker maximum {maxStake:F2} at every multiplier ({string.Join("/", allowedMultipliers)}); reduce risk per trade.");
    }

    /// <summary>
    /// Recomputes the stop-loss and take-profit amounts with the commission Deriv actually quoted (it can be far above
    /// the proportional estimate on small positions), and rejects trades where commission dominates the risk budget.
    /// </summary>
    public static (MultiplierContractPlan? Plan, string? Rejection) WithQuotedCommission(
        MultiplierContractPlan plan, decimal entry, decimal stopLoss, decimal takeProfit, decimal commission, decimal maxCommissionShareOfRisk)
    {
        var stopLossAmount = Math.Round(plan.Notional * Math.Abs(entry - stopLoss) / entry + commission, 2, MidpointRounding.AwayFromZero);
        var takeProfitAmount = Math.Round(plan.Notional * Math.Abs(takeProfit - entry) / entry - commission, 2, MidpointRounding.ToZero);

        if (stopLossAmount > 0 && commission / stopLossAmount > maxCommissionShareOfRisk)
        {
            return (null, $"Broker commission {commission:F2} would be {commission / stopLossAmount:P0} of the {stopLossAmount:F2} at risk " +
                          $"(limit {maxCommissionShareOfRisk:P0}); the position is too small for its fees.");
        }

        if (takeProfitAmount <= 0)
        {
            return (null, "Take profit does not cover the broker commission.");
        }

        if (stopLossAmount >= plan.Stake)
        {
            return (null, $"Stop loss {stopLossAmount:F2} would exceed the contract stake {plan.Stake:F2}.");
        }

        return (plan with { StopLossAmount = stopLossAmount, TakeProfitAmount = takeProfitAmount }, null);
    }

    /// <summary>True when the broker-computed stop price is close enough to the strategy's stop.</summary>
    public static bool StopWithinTolerance(decimal entry, decimal requestedStop, decimal brokerStop, decimal tolerance) =>
        Math.Abs(brokerStop - requestedStop) <= Math.Abs(entry - requestedStop) * tolerance;

    /// <summary>
    /// Classifies why the broker closed a contract: near/through the take-profit level, near/through the stop level
    /// (including stop-out), otherwise a manual or external close.
    /// </summary>
    public static ExitClassification ClassifyExit(Direction direction, decimal exitPrice, decimal stopLoss, decimal takeProfit)
    {
        var tolerance = Math.Abs(takeProfit - stopLoss) * 0.05m;
        var sign = direction.Sign();
        if (sign * (exitPrice - takeProfit) >= -tolerance)
        {
            return ExitClassification.TakeProfit;
        }

        return sign * (exitPrice - stopLoss) <= tolerance ? ExitClassification.StopLoss : ExitClassification.Manual;
    }
}

public enum ExitClassification
{
    StopLoss,
    TakeProfit,
    Manual
}
