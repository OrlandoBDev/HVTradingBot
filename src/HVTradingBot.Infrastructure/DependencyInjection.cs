using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.Brokers;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Infrastructure.Notifications;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Stores;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers configuration, persistence and the shared trading core used by both the API and the worker.</summary>
    public static IServiceCollection AddTradingCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<TradingEngineOptions>(configuration, TradingEngineOptions.SectionName);
        services.AddSingleton<IValidateOptions<TradingEngineOptions>, TradingOptionsValidator>();
        services.AddValidatedOptions<RiskOptions>(configuration, "Risk");
        services.AddValidatedOptions<ScoringOptions>(configuration, "Scoring");
        services.AddValidatedOptions<RegimeOptions>(configuration, "Regime");
        services.AddValidatedOptions<ExecutionCostOptions>(configuration, "ExecutionCosts");
        services.AddValidatedOptions<SimulatedMarketOptions>(configuration, SimulatedMarketOptions.SectionName);

        var connectionString = configuration.GetConnectionString(DatabaseSetup.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{DatabaseSetup.ConnectionStringName}' is not configured. " +
                "Set ConnectionStrings__TradingDb (./run.sh does this from .env; IDE runs in Development read .env directly - run ./run.sh once to create it), or use dotnet user-secrets.");
        }

        var provider = DatabaseSetup.ProviderFrom(configuration);
        services.AddDbContextFactory<TradingDbContext>(o => DatabaseSetup.Configure(o, provider, connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITradingStateStore, EfTradingStateStore>();
        services.AddSingleton<EfDecisionJournal>();
        services.AddSingleton<IDecisionJournal>(sp => sp.GetRequiredService<EfDecisionJournal>());
        services.AddSingleton<IMarketSnapshotSink>(sp => sp.GetRequiredService<EfDecisionJournal>());

        // Deriv settings (Settings page) are shared by the API (writes) and the worker (reads).
        services.AddHvDataProtection(configuration);
        services.AddValidatedOptions<DerivOptions>(configuration, DerivOptions.SectionName);
        services.AddSingleton(sp =>
        {
            var deriv = sp.GetRequiredService<DerivOptions>();
            return new DerivEnvironmentCredentials(deriv.AppId, deriv.ApiToken, deriv.AccountId);
        });
        services.AddSingleton<DerivSettingsStore>();
        services.AddSingleton<IDerivCredentialsProvider>(sp => sp.GetRequiredService<DerivSettingsStore>());
        services.AddSingleton<IDerivStatusSink>(sp => sp.GetRequiredService<DerivSettingsStore>());

        services.AddSingleton<MarketCatalogStore>();
        services.AddSingleton<TestTrades.TestTradeStore>();
        services.AddSingleton<Trades.CloseRequestStore>();
        services.AddSingleton<TradingUniverse>();

        services.AddValidatedOptions<LearningOptions>(configuration, LearningOptions.SectionName);
        services.AddSingleton<IVirtualTradeStore, EfVirtualTradeStore>();
        services.AddSingleton<LearningService>();
        services.AddSingleton<IStrategyPerformanceProvider>(sp => sp.GetRequiredService<LearningService>());

        services.AddSingleton<IReadOnlyList<ITradingStrategy>>(_ => StrategyCatalog.CreateDefault());
        services.AddSingleton<SignalEvaluator>();
        services.AddEmailSettings(configuration);
        services.AddSingleton<RiskSettingsStore>();
        services.AddSingleton<RiskOptionsSource>();
        services.AddSingleton<IRiskOptionsSource>(sp => sp.GetRequiredService<RiskOptionsSource>());
        services.AddSingleton<IRiskManager>(sp => new RiskManager(sp.GetRequiredService<IRiskOptionsSource>(), sp.GetRequiredService<ExecutionCostOptions>()));
        services.AddSingleton<BacktestEngine>();
        return services;
    }

    /// <summary>
    /// Registers the live trading loop. Market data and broker are chosen by MarketData:Provider and Broker:Provider:
    /// Deriv demo (default) or the offline simulated feed with the local paper broker.
    /// </summary>
    public static IServiceCollection AddLiveTrading(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<BrokerOptions>(configuration, BrokerOptions.SectionName);
        services.AddValidatedOptions<MarketDataOptions>(configuration, MarketDataOptions.SectionName);
        services.AddSingleton<IValidateOptions<DerivOptions>, LiveTradingOptionsValidator>();

        var broker = configuration.GetSection(BrokerOptions.SectionName).Get<BrokerOptions>() ?? new BrokerOptions();
        var marketData = configuration.GetSection(MarketDataOptions.SectionName).Get<MarketDataOptions>() ?? new MarketDataOptions();

        services.AddSingleton<IDerivSocketFactory, DerivSocketFactory>();
        // One long-lived client; connection pooling recycles connections so DNS changes are picked up.
        services.AddSingleton(sp => new DerivRestClient(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(30) },
            sp.GetRequiredService<DerivOptions>()));
        services.AddSingleton<DerivSession>();
        services.AddSingleton<DerivMarketDiscovery>();

        if (marketData.Provider == MarketDataProvider.Deriv)
        {
            services.AddSingleton<IMarketDataFeed, DerivMarketDataFeed>();
        }
        else
        {
            services.AddSingleton<IMarketDataFeed, SimulatedMarketDataFeed>();
        }

        if (broker.Provider == BrokerProvider.Deriv)
        {
            services.AddSingleton<IExecutionBroker, DerivBroker>();
        }
        else
        {
            services.AddSingleton<IExecutionBroker, PaperTradingBroker>();
        }

        services.AddSingleton<ExecutionService>();
        // Email notifications (Notifications:Email, off by default) are sent by the worker, which makes the decisions.
        services.AddTradeDecisionNotifications(configuration);
        services.AddSingleton<TradingEngine>();
        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<TradingDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await MigrateAsync(db, cancellationToken);
    }

    /// <summary>Applies migrations; on SQLite also switches to WAL so dashboard reads never wait for engine writes.</summary>
    public static async Task MigrateAsync(TradingDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }

        // EF Core takes a database lock during migration, so the API and worker can both call this safely.
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Binds, validates at startup, and also registers the options class itself for plain constructor injection.</summary>
    private static void AddValidatedOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
