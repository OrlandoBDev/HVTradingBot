using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace HVTradingBot.Worker;

/// <summary>
/// Hosts the paper-trading loop: migrate, warm up, reconcile, then process each new bar.
/// Repeated consecutive failures stop the host so the supervisor (Docker / run script) can restart it cleanly.
/// </summary>
public sealed class TradingWorker(
    IServiceProvider services,
    IMarketDataFeed feed,
    TradingEngine engine,
    IExecutionBroker broker,
    IDecisionJournal journal,
    ITradingStateStore stateStore,
    IDbContextFactory<TradingDbContext> dbFactory,
    MarketDataOptions marketDataOptions,
    MarketCatalogStore catalog,
    DerivMarketDiscovery discovery,
    TradingUniverse universe,
    TradingEngineOptions engineOptions,
    RiskOptionsSource riskSource,
    IHostApplicationLifetime lifetime,
    ILogger<TradingWorker> logger) : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await services.MigrateDatabaseAsync(stoppingToken);
            await EnsureSingleMarketDataSourceAsync(stoppingToken);
            await riskSource.RefreshAsync(stoppingToken);
            var selectionVersion = await ConfigureUniverseAsync(stoppingToken);
            var history = await feed.LoadHistoryAsync(stoppingToken);
            await stateStore.UpdateAsync(s => s with { MarketDataSource = marketDataOptions.Provider.ToString() }, stoppingToken);
            await WaitForBrokerAsync(stoppingToken);
            await engine.InitializeAsync(history, stoppingToken);
            await catalog.MarkAppliedAsync(selectionVersion, stoppingToken);
            await journal.RecordAuditAsync(TradingEngine.SystemActor, "WorkerStarted", "Paper trading loop started.", null, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Trading worker failed to start");
            lifetime.StopApplication();
            throw;
        }

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            using var _ = LogContext.PushProperty(ObservabilityExtensions.CorrelationIdProperty, correlationId);
            try
            {
                var bars = await feed.NextBarsAsync(stoppingToken);
                await engine.ProcessBarsAsync(bars, feed.Status, correlationId, stoppingToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (BrokerUnavailableException ex)
            {
                // Settings removed or credentials rejected: no orders can be sent. Keep the heartbeat and retry next bar.
                logger.LogWarning("Broker unavailable, skipping this cycle: {Message}", ex.Message);
                await engine.RecordIdleAsync(feed.Status, stoppingToken);
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "Trading cycle failed ({Failures}/{Max})", failures, MaxConsecutiveFailures);
                if (failures >= MaxConsecutiveFailures)
                {
                    logger.LogCritical("Stopping worker after {Failures} consecutive failures", failures);
                    lifetime.StopApplication();
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(2 * failures), stoppingToken);
            }
        }

        logger.LogInformation("Trading worker stopped");
    }

    /// <summary>
    /// Loads the broker's market catalog (Deriv) and applies the market selection from the Settings page, falling back
    /// to Trading:Instruments. Returns the applied selection version.
    /// </summary>
    private async Task<int> ConfigureUniverseAsync(CancellationToken cancellationToken)
    {
        if (marketDataOptions.Provider == MarketDataProvider.Deriv)
        {
            try
            {
                await discovery.RefreshAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DerivConnectionException or DerivApiException)
            {
                logger.LogWarning("Could not refresh the Deriv market catalog ({Message}); using the stored catalog", ex.Message);
            }
        }

        await catalog.LoadAndRegisterAsync(cancellationToken);
        var selection = await catalog.GetSelectionAsync(cancellationToken);
        var requested = selection.Instruments ?? engineOptions.Instruments;

        var selected = new List<Instrument>();
        foreach (var symbol in requested)
        {
            if (!Instruments.TryGet(symbol, out var instrument))
            {
                logger.LogWarning("Selected market {Symbol} is not in the catalog; skipped", symbol);
                continue;
            }

            if (marketDataOptions.Provider == MarketDataProvider.Simulated && !MarketSeriesGenerator.Supports(instrument))
            {
                logger.LogWarning("Market {Symbol} cannot be simulated offline; skipped", symbol);
                continue;
            }

            selected.Add(instrument);
        }

        if (selected.Count == 0)
        {
            logger.LogWarning("No usable market selected; falling back to the default Forex majors");
            selected.AddRange(Instruments.Defaults);
        }

        universe.Configure(selected, engineOptions.AccountCurrency, selection.DerivedOnlyWhenForexClosed);
        logger.LogInformation("Markets: Derived {DerivedMode}; trading {Traded}; conversion only {Conversion}",
            selection.DerivedOnlyWhenForexClosed ? "only while Forex is closed" : "always",
            string.Join(", ", universe.Traded.Select(i => i.DisplayName)),
            string.Join(", ", universe.Data.Except(universe.Traded).Select(i => i.Symbol)));
        return selection.Version;
    }

    /// <summary>
    /// Waits until the broker accepts a connection (e.g. Deriv credentials entered on the Settings page), keeping the
    /// worker heartbeat alive so the dashboard shows the worker as running.
    /// </summary>
    private async Task WaitForBrokerAsync(CancellationToken cancellationToken)
    {
        var lastMessage = "";
        while (true)
        {
            try
            {
                await broker.GetAccountAsync(cancellationToken);
                return;
            }
            catch (BrokerUnavailableException ex)
            {
                if (ex.Message != lastMessage)
                {
                    logger.LogWarning("Waiting for broker: {Message}", ex.Message);
                    lastMessage = ex.Message;
                }
            }

            await stateStore.UpdateAsync(s => s with
            {
                WorkerHeartbeatUtc = DateTime.UtcNow,
                LastDataReceivedUtc = feed.Status.LastReceivedUtc,
                LastBarTimeUtc = feed.Status.LastBarTimeUtc
            }, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    /// <summary>
    /// Real (Deriv) and synthetic (Simulated) prices must never share a database: candles would interleave and
    /// backtests on stored data would be meaningless.
    /// </summary>
    private async Task EnsureSingleMarketDataSourceAsync(CancellationToken cancellationToken)
    {
        var configured = marketDataOptions.Provider.ToString();
        var stored = (await stateStore.GetAsync(cancellationToken)).MarketDataSource;
        if (stored is null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            // Databases created before the Deriv integration only ever held simulated data.
            stored = await db.Candles.AnyAsync(cancellationToken) ? nameof(MarketDataProvider.Simulated) : null;
        }

        if (stored is not null && stored != configured)
        {
            throw new InvalidOperationException(
                $"The database contains {stored} market data but MarketData:Provider is {configured}. " +
                $"Run './run.sh reset' to start a fresh database (this deletes local trading history), or set MarketData:Provider back to {stored}.");
        }
    }
}
