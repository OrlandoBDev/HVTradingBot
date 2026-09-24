using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.TestTrades;
using Serilog.Context;

namespace HVTradingBot.Worker;

/// <summary>
/// Carries out test trades requested from the dashboard: place through the engine, hold for a minute, close at the
/// broker, and wait until the close has been reconciled (it then appears in trade history and is emailed).
/// </summary>
public sealed class TestTradeService(
    TestTradeStore store,
    TradingEngine engine,
    IMarketDataFeed feed,
    IClock clock,
    ILogger<TestTradeService> logger) : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CloseRecordTimeout = TimeSpan.FromMinutes(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Poll);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (engine.IsReady && await store.NextActiveAsync(stoppingToken) is { } trade)
                {
                    using var _ = LogContext.PushProperty(ObservabilityExtensions.CorrelationIdProperty, $"test-{trade.Id:N}");
                    await AdvanceAsync(trade, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Test trade step failed");
            }
        }
    }

    private async Task AdvanceAsync(TestTradeEntity trade, CancellationToken ct)
    {
        switch (trade.Status)
        {
            case TestTradeStatus.Pending:
                await OpenAsync(trade, ct);
                break;

            case TestTradeStatus.Open when trade.OpenedAtUtc is { } opened && clock.UtcNow - opened >= TimeSpan.FromSeconds(trade.HoldSeconds):
                await CloseAsync(trade, ct);
                break;

            case TestTradeStatus.Closing:
                await AwaitRecordedCloseAsync(trade, ct);
                break;

            case TestTradeStatus.Opening:
                // The worker restarted mid-placement: the broker is authoritative, so check for a recorded position.
                await store.UpdateAsync(trade.Id, t =>
                {
                    t.Status = TestTradeStatus.Failed;
                    t.Message = "The worker restarted while placing the order; check Trades and the Audit log for its outcome.";
                }, ct);
                break;
        }
    }

    private async Task OpenAsync(TestTradeEntity trade, CancellationToken ct)
    {
        await store.UpdateAsync(trade.Id, t => { t.Status = TestTradeStatus.Opening; t.Message = "Placing the order…"; }, ct);
        if (!Instruments.TryGet(trade.Instrument, out var instrument))
        {
            await Fail(trade, $"Unknown market {trade.Instrument}.", ct);
            return;
        }

        logger.LogInformation("Placing test trade on {Instrument} requested by {User}", instrument.Symbol, trade.RequestedBy);
        var outcome = await engine.PlaceTestTradeAsync(instrument, feed.Status, trade.RequestedBy, $"test-{trade.Id:N}", ct, feed.LatestQuotes);
        if (!outcome.Filled)
        {
            await store.UpdateAsync(trade.Id, t =>
            {
                t.Status = TestTradeStatus.Failed;
                t.Message = $"Not placed: {outcome.Message}";
                t.ClientOrderId = outcome.ClientOrderId;
            }, ct);
            return;
        }

        await store.UpdateAsync(trade.Id, t =>
        {
            t.Status = TestTradeStatus.Open;
            t.Message = $"{outcome.Message} Closing in {t.HoldSeconds} seconds.";
            t.ClientOrderId = outcome.ClientOrderId;
            t.PositionId = outcome.PositionId;
            t.FillPrice = outcome.FillPrice;
            t.OpenedAtUtc = clock.UtcNow;
        }, ct);
    }

    private async Task CloseAsync(TestTradeEntity trade, CancellationToken ct)
    {
        if (trade.PositionId is not { } positionId)
        {
            await Fail(trade, "No position to close.", ct);
            return;
        }

        var position = await store.PositionAsync(positionId, ct);
        if (position is { IsOpen: false })
        {
            await Closed(trade, position.ExitPrice, position.RealizedPnl, position.ExitReason, ct); // already hit stop/target
            return;
        }

        Domain.Execution.OrderResult result;
        Domain.Execution.ClosedPosition? closedNow;
        try
        {
            (result, closedNow) = await engine.ClosePositionAsync(positionId, $"test-{trade.Id:N}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Test trade close failed");
            await Fail(trade, $"Close failed: {ex.Message} The position keeps its stop loss and take profit.", ct);
            return;
        }

        if (result.Status == Domain.Execution.OrderStatus.Rejected)
        {
            await Fail(trade, $"Close refused: {result.RejectReason} The position keeps its stop loss and take profit.", ct);
            return;
        }

        if (closedNow is not null)
        {
            await Closed(trade, closedNow.ExitPrice, closedNow.RealizedPnl, closedNow.Reason.ToString(), ct);
            return;
        }

        await store.UpdateAsync(trade.Id, t =>
        {
            t.Status = TestTradeStatus.Closing;
            t.CloseRequestedAtUtc = clock.UtcNow;
            t.Message = "Closed at the broker. Waiting for the broker to confirm the result…";
        }, ct);
    }

    private async Task AwaitRecordedCloseAsync(TestTradeEntity trade, CancellationToken ct)
    {
        var position = trade.PositionId is { } id ? await store.PositionAsync(id, ct) : null;
        if (position is { IsOpen: false })
        {
            await Closed(trade, position.ExitPrice, position.RealizedPnl, position.ExitReason, ct);
        }
        else if (trade.CloseRequestedAtUtc is { } asked && clock.UtcNow - asked > CloseRecordTimeout)
        {
            await Fail(trade, "The close was sent but not yet confirmed by the broker; the position is reconciled automatically.", ct);
        }
    }

    private Task Closed(TestTradeEntity trade, decimal? exitPrice, decimal? pnl, string? reason, CancellationToken ct) =>
        store.UpdateAsync(trade.Id, t =>
        {
            t.Status = TestTradeStatus.Closed;
            t.ClosedAtUtc = clock.UtcNow;
            t.ExitPrice = exitPrice;
            t.RealizedPnl = pnl;
            t.Message = $"Done: closed{(reason is null ? "" : $" ({reason})")} at {exitPrice}, result {pnl:+0.00;-0.00}. " +
                        "See Trades › History, the Decisions journal and your email.";
        }, ct);

    private Task Fail(TestTradeEntity trade, string message, CancellationToken ct) =>
        store.UpdateAsync(trade.Id, t => { t.Status = TestTradeStatus.Failed; t.Message = message; }, ct);
}
