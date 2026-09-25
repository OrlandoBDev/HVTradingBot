using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Trades;
using Serilog.Context;

namespace HVTradingBot.Worker;

/// <summary>
/// Carries out "close this position" requests from the dashboard: closes through the engine (which reconciles at once,
/// records the result, updates loss limits and emails), or waits for the broker to confirm if that takes longer.
/// </summary>
public sealed class ClosePositionService(
    CloseRequestStore store,
    TradingEngine engine,
    IDecisionJournal journal,
    IClock clock,
    ILogger<ClosePositionService> logger) : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Poll);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!engine.IsReady)
            {
                continue;
            }

            try
            {
                foreach (var request in await store.ActiveAsync(stoppingToken))
                {
                    using var _ = LogContext.PushProperty(ObservabilityExtensions.CorrelationIdProperty, $"close-{request.Id:N}");
                    await AdvanceAsync(request, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Close request step failed");
            }
        }
    }

    private async Task AdvanceAsync(CloseRequestEntity request, CancellationToken ct)
    {
        var position = await store.PositionAsync(request.PositionId, ct);
        if (position is { IsOpen: false })
        {
            await Done(request, position.ExitPrice, position.RealizedPnl, position.ExitReason, ct);
            return;
        }

        if (request.Status == CloseRequestStatus.Closing)
        {
            if (clock.UtcNow - request.RequestedAtUtc > ConfirmTimeout)
            {
                await Fail(request, "The broker has not confirmed the close yet; it is reconciled automatically on each update.", ct);
            }

            return; // waiting for the broker to confirm (checked again next poll / next bar)
        }

        await store.UpdateAsync(request.Id, r => { r.Status = CloseRequestStatus.Closing; r.Message = "Closing at the broker…"; }, ct);
        logger.LogInformation("Closing position {PositionId} ({Instrument}) on request by {User}", request.PositionId, request.Instrument, request.RequestedBy);

        OrderResult result;
        ClosedPosition? closed;
        try
        {
            (result, closed) = await engine.ClosePositionAsync(request.PositionId, $"close-{request.Id:N}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Close failed for {PositionId}", request.PositionId);
            await Fail(request, $"Close failed: {ex.Message} The position keeps its stop loss and take profit.", ct);
            return;
        }

        if (result.Status == OrderStatus.Rejected)
        {
            await Fail(request, $"{result.RejectReason} The position keeps its stop loss and take profit.", ct);
            return;
        }

        await journal.RecordAuditAsync(request.RequestedBy, "PositionCloseRequested",
            $"{request.Instrument} position {request.PositionId} closed on request.", $"close-{request.Id:N}", ct);

        if (closed is not null)
        {
            await Done(request, closed.ExitPrice, closed.RealizedPnl, closed.Reason.ToString(), ct);
        }
        else
        {
            await store.UpdateAsync(request.Id, r => r.Message = "Sent to the broker; waiting for it to confirm the result…", ct);
        }
    }

    private Task Done(CloseRequestEntity request, decimal? exitPrice, decimal? pnl, string? reason, CancellationToken ct) =>
        store.UpdateAsync(request.Id, r =>
        {
            r.Status = CloseRequestStatus.Closed;
            r.CompletedAtUtc = clock.UtcNow;
            r.ExitPrice = exitPrice;
            r.RealizedPnl = pnl;
            r.Message = $"Closed{(reason is null ? "" : $" ({reason})")} at {exitPrice}, result {pnl:+0.00;-0.00}.";
        }, ct);

    private Task Fail(CloseRequestEntity request, string message, CancellationToken ct) =>
        store.UpdateAsync(request.Id, r => { r.Status = CloseRequestStatus.Failed; r.CompletedAtUtc = clock.UtcNow; r.Message = message; }, ct);
}
