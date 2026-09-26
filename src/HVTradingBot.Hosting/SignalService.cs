using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Signals;
using Serilog.Context;

namespace HVTradingBot.Hosting;

/// <summary>
/// Looks after trade signals: expires the ones nobody acted on, keeps the signal settings current for the engine,
/// and places the ones the user accepted (through the same risk rules as the bot, with any risks the user accepted).
/// </summary>
public sealed class SignalService(
    SignalStore store,
    SignalSettingsSource settings,
    TradingEngine engine,
    IMarketDataFeed feed,
    ILogger<SignalService> logger) : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await settings.RefreshAsync(stoppingToken);
            if (await store.FailInterruptedAsync(stoppingToken) is > 0 and var interrupted)
            {
                logger.LogWarning("{Count} signal(s) were being placed when the engine stopped; marked failed", interrupted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Signal service start-up failed");
        }

        using var timer = new PeriodicTimer(Poll);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Signal step failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await settings.RefreshAsync(ct);
        await store.ExpireDueAsync(ct);
        if (!engine.IsReady)
        {
            return;
        }

        while (await store.ClaimNextAcceptedAsync(ct) is { } signal)
        {
            var correlationId = $"signal-{signal.Id:N}";
            using var _ = LogContext.PushProperty(ObservabilityExtensions.CorrelationIdProperty, correlationId);
            var request = new SignalRequest(signal.Id, SignalOrderId(signal), signal.Instrument, Enum.Parse<Direction>(signal.Direction),
                signal.Strategy, signal.Score, signal.Entry, signal.StopLoss, signal.TakeProfit, SignalStore.ReadAcceptedRules(signal),
                signal.DecidedBy ?? "user");
            SignalOutcome outcome;
            try
            {
                outcome = await engine.PlaceSignalTradeAsync(request, feed.Status, correlationId, ct, feed.LatestQuotes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Placing signal {SignalId} failed", signal.Id);
                outcome = new SignalOutcome(SignalOutcomeStatus.Failed, $"Placing failed: {ex.Message}", []);
            }

            await store.RecordOutcomeAsync(signal.Id, outcome, ct);
            logger.LogInformation("Signal {SignalId} on {Instrument}: {Status} — {Message}", signal.Id, signal.Instrument, outcome.Status,
                outcome.Message);
        }
    }

    private static string SignalOrderId(Infrastructure.Persistence.Entities.SignalEntity signal) => SignalOrders.Prefix + signal.SetupId;
}
