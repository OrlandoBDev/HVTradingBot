using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Markets;

namespace HVTradingBot.Worker;

/// <summary>
/// Every few seconds: records the worker heartbeat and market-data freshness (bars arrive only every 5 minutes),
/// applies Settings-page changes by touching the broker connection (which reconnects when stored settings changed),
/// restarts the worker when the market selection changes, and periodically refreshes the Deriv market catalog.
/// </summary>
public sealed class BrokerSettingsWatcher(
    IExecutionBroker broker,
    IMarketDataFeed feed,
    TradingEngine engine,
    ITradingStateStore stateStore,
    MarketCatalogStore catalog,
    DerivMarketDiscovery discovery,
    MarketDataOptions marketDataOptions,
    IDecisionJournal journal,
    IMarketSnapshotSink snapshots,
    IHostApplicationLifetime lifetime,
    ILogger<BrokerSettingsWatcher> logger) : BackgroundService
{
    /// <summary>Process exit code asking the supervisor (run.sh / Docker) to start the worker again.</summary>
    public const int RestartExitCode = 3;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private DateTime _lastCatalogRefresh = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!engine.IsReady)
            {
                // Starting up (loading history, waiting for the broker): keep liveness and live tick freshness visible.
                try
                {
                    await stateStore.UpdateAsync(s => s with
                    {
                        WorkerHeartbeatUtc = DateTime.UtcNow,
                        LastDataReceivedUtc = feed.Status.LastReceivedUtc ?? s.LastDataReceivedUtc
                    }, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Startup heartbeat failed");
                }

                continue;
            }

            try
            {
                await engine.RecordIdleAsync(feed.Status, stoppingToken);
                // Live prices for the dashboard between 5-minute bars.
                await snapshots.PublishQuotesAsync(feed.LatestQuotes, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Heartbeat update failed");
            }

            if (await SelectionChangedAsync(stoppingToken))
            {
                return;
            }

            await RefreshCatalogIfDueAsync(stoppingToken);

            if (broker.Descriptor.Name is "Paper" or "Backtest")
            {
                continue;
            }

            try
            {
                await broker.GetAccountAsync(stoppingToken);
                var descriptor = broker.Descriptor;
                var state = await stateStore.GetAsync(stoppingToken);
                if (state.BrokerAccountId != descriptor.AccountId || state.BrokerIsDemo != descriptor.IsDemo)
                {
                    await stateStore.UpdateAsync(s => s with { BrokerName = descriptor.Name, BrokerAccountId = descriptor.AccountId, BrokerIsDemo = descriptor.IsDemo },
                        stoppingToken);
                    logger.LogInformation("Active broker account is now {AccountId}", descriptor.AccountId);
                }
            }
            catch (BrokerUnavailableException ex)
            {
                logger.LogDebug("Broker settings check: {Message}", ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Broker settings check failed");
            }
        }
    }

    /// <summary>
    /// A new market selection needs fresh history and a rebuilt engine, so the worker restarts cleanly. Open positions
    /// stay protected meanwhile by their broker-side stop loss and take profit, and are reconciled on startup.
    /// </summary>
    private async Task<bool> SelectionChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var selection = await catalog.GetSelectionAsync(cancellationToken);
            if (selection.Version == selection.AppliedVersion)
            {
                return false;
            }

            logger.LogWarning("Market selection changed (version {Version}); restarting the worker to apply it", selection.Version);
            await journal.RecordAuditAsync(TradingEngine.SystemActor, "WorkerRestart",
                $"Applying market selection version {selection.Version}: {string.Join(", ", selection.Instruments ?? [])}", null, cancellationToken);
            Environment.ExitCode = RestartExitCode;
            lifetime.StopApplication();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Market selection check failed");
            return false;
        }
    }

    private async Task RefreshCatalogIfDueAsync(CancellationToken cancellationToken)
    {
        if (marketDataOptions.Provider != MarketDataProvider.Deriv
            || DateTime.UtcNow - _lastCatalogRefresh < TimeSpan.FromHours(marketDataOptions.CatalogRefreshHours))
        {
            return;
        }

        _lastCatalogRefresh = DateTime.UtcNow;
        try
        {
            await discovery.RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DerivConnectionException or DerivApiException)
        {
            logger.LogWarning("Market catalog refresh failed: {Message}", ex.Message);
        }
    }
}
