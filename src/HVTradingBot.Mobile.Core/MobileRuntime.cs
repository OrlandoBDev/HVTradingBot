using HVTradingBot.Application.Abstractions;
using HVTradingBot.Dashboard;
using HVTradingBot.Hosting;
using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Notifications;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

/// <summary>
/// Everything that runs on the phone, in one process. Mirrors the server setup: a dashboard side (what the API does:
/// reads, settings, requests) and a trading side (what the worker does), sharing only the SQLite database. The trading
/// side is supervised and restarted on its own, so the dashboard keeps answering while the engine restarts, and the
/// same cross-process rules apply as between the API and the worker (only the worker talks to the broker).
/// </summary>
public sealed class MobileRuntime : IAsyncDisposable
{
    private readonly ServiceProvider _dashboard;
    private readonly CancellationTokenSource _stopping = new();
    private Task _running = Task.CompletedTask;

    private MobileRuntime(MobileSettings settings, IConfigurationRoot configuration, AppLog log, IPhoneNotificationSink? phone)
    {
        Settings = settings;
        Log = log;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => ConfigureLogging(b, log));
        services.AddTradingCore(configuration).AddDashboard();
        services.AddSingleton(sp => new EngineSupervisor(() => CreateEngineHost(configuration, log, phone), sp.GetRequiredService<ILogger<EngineSupervisor>>()));
        services.AddSingleton(log);
        services.AddSingleton<LiveUpdates>();
        services.AddSingleton<LocalApi>();
        services.AddSingleton<EventFeed>();
        services.AddSingleton<WebBridge>();
        _dashboard = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Engine = _dashboard.GetRequiredService<EngineSupervisor>();
        Api = _dashboard.GetRequiredService<LocalApi>();
        Live = _dashboard.GetRequiredService<LiveUpdates>();
        if (phone is not null)
        {
            Live.SignalAlerted += phone.ShowSignal;
            Live.SignalCleared += phone.DismissSignal;
            Live.SignalNotice += phone.Show;
        }

        _dashboard.GetRequiredService<EventFeed>(); // subscribes to live updates from the start
        Bridge = _dashboard.GetRequiredService<WebBridge>();
    }

    public MobileSettings Settings { get; }

    public AppLog Log { get; }

    public EngineSupervisor Engine { get; }

    public LocalApi Api { get; }

    public LiveUpdates Live { get; }

    /// <summary>Answers the WebView's dashboard requests.</summary>
    public WebBridge Bridge { get; }

    public bool IsRunning => !_running.IsCompleted;

    public static MobileRuntime Create(MobileSettings settings, IPhoneNotificationSink? phone = null, AppLog? log = null) =>
        new(settings, MobileConfiguration.Build(settings), log ?? new AppLog(), phone);

    /// <summary>Creates or upgrades the database, then starts live updates and the trading engine in the background.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        await _dashboard.MigrateDatabaseAsync(cancellationToken);
        var token = _stopping.Token;
        _running = Task.WhenAll(
            Task.Run(() => Live.RunAsync(token), CancellationToken.None),
            Task.Run(() => Engine.RunAsync(token), CancellationToken.None));
    }

    /// <summary>Stops the engine (open positions keep their broker-side stop loss and take profit) and live updates.</summary>
    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        await _running;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _dashboard.DisposeAsync();
        _stopping.Dispose();
    }

    /// <summary>The trading side: the worker's services (TradingWorker and companions) in a host of their own.</summary>
    private static IHost CreateEngineHost(IConfiguration configuration, AppLog log, IPhoneNotificationSink? phone)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = "HVTradingBot.Mobile" });
        builder.Configuration.AddConfiguration(configuration);
        ConfigureLogging(builder.Logging, log);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(15));
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services.AddTradingCore(configuration).AddLiveTrading(configuration);
        builder.Services.AddTradingWorker(_ => new InProcessWorkerLock());
        if (phone is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<ITradeDecisionNotifier>(sp =>
                new PhoneTradeNotifier(ActivatorUtilities.CreateInstance<QueuedTradeDecisionNotifier>(sp), phone)));
        }

        return builder.Build();
    }

    private static void ConfigureLogging(ILoggingBuilder logging, AppLog log)
    {
        logging.ClearProviders();
        logging.AddProvider(log);
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
    }
}
