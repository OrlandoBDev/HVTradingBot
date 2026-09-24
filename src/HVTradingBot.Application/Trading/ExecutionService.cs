using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Risk;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Application.Trading;

/// <summary>
/// The only component allowed to call broker order methods. Re-runs the full risk check immediately before
/// submitting (the second of the two required checks) and relies on the broker's idempotency for retries.
/// </summary>
public sealed class ExecutionService(IExecutionBroker broker, IRiskManager riskManager, ILogger<ExecutionService> logger)
{
    public async Task<(RiskDecision FinalRisk, OrderResult Result)> ExecuteAsync(
        TradeProposal proposal,
        Func<CancellationToken, Task<PortfolioState>> portfolioProvider,
        Guid decisionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var portfolio = await portfolioProvider(cancellationToken);
        var finalRisk = await riskManager.EvaluateAsync(proposal, portfolio, cancellationToken);
        if (!finalRisk.IsApproved)
        {
            logger.LogWarning("Final risk check rejected {ClientOrderId}: {Reason}", proposal.ClientOrderId, finalRisk.RejectionReason);
            return (finalRisk, OrderResult.Rejected(proposal.ClientOrderId, $"Final risk check: {finalRisk.RejectionReason}"));
        }

        var order = new TradeOrder(
            proposal.ClientOrderId,
            proposal.Instrument,
            proposal.Setup.Direction,
            finalRisk.Units,
            proposal.Setup.Entry,
            proposal.Setup.StopLoss,
            proposal.Setup.TakeProfit,
            finalRisk.RiskAmount,
            proposal.Strategy,
            proposal.Score,
            decisionId,
            correlationId);

        var result = await broker.PlaceOrderAsync(order, cancellationToken);
        logger.LogInformation(
            "Order {ClientOrderId} {Instrument} {Direction} {Units} -> {Status} at {FillPrice} {RejectReason}",
            order.ClientOrderId, order.Instrument.Symbol, order.Direction, order.Units, result.Status, result.FillPrice, result.RejectReason);
        return (finalRisk, result);
    }
}
