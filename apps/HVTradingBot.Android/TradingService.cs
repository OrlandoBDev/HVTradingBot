using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using HVTradingBot.Contracts;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// Keeps the trading engine running in the background, also with the screen off: a foreground service (Android does
/// not stop those) with a permanent notification showing the engine's state, plus a partial wake lock so the CPU keeps
/// processing bars. Declared in AndroidManifest.xml (foreground service type "specialUse").
/// </summary>
[Register("com.hvtradingbot.android.TradingService")]
public sealed class TradingService : Service
{
    private const string ActionStop = "com.hvtradingbot.android.STOP";

    /// <summary>The Skip and Trade buttons of a signal notification.</summary>
    public const string ActionSkipSignal = "com.hvtradingbot.android.SKIP_SIGNAL";
    public const string ActionTradeSignal = "com.hvtradingbot.android.TRADE_SIGNAL";

    private PowerManager.WakeLock? _wakeLock;
    private MobileRuntime? _subscribed;
    private string? _shown;

    /// <summary>Starts (or keeps) the engine running in the foreground.</summary>
    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(TradingService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        PhoneNotifications.CreateChannels(this);
        // Android requires the notification within seconds of starting a foreground service, also when stopping.
        StartInForeground(EngineNotification("HVTradingBot is starting", "Opening the database…"));

        if (intent?.Action == ActionStop)
        {
            EngineSwitch.Set(this, false);
            _ = StopEngineAsync();
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action is ActionSkipSignal or ActionTradeSignal && Guid.TryParse(intent.GetStringExtra(PhoneNotifications.SignalIdExtra), out var signalId))
        {
            _ = DecideSignalAsync(signalId, intent.Action == ActionTradeSignal);
        }

        AcquireWakeLock();
        var runtime = AppRuntime.Get(this);
        if (!ReferenceEquals(runtime, _subscribed))
        {
            _subscribed = runtime;
            runtime.Live.StatusChanged += UpdateNotification;
        }

        _ = StartEngineAsync();
        // Sticky: if Android ever has to kill the process, it starts the service again.
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        ReleaseWakeLock();
        base.OnDestroy();
    }

    private async Task StartEngineAsync()
    {
        try
        {
            await AppRuntime.EnsureStartedAsync(this);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HVTradingBot", $"Engine failed to start: {ex}");
            Show(EngineNotification("HVTradingBot could not start", ex.Message));
        }
    }

    /// <summary>
    /// A signal notification's button: records the decision through the same route the dashboard uses (Trade accepts
    /// no extra risks; a signal with failed rules has no Trade button and is reviewed in the app).
    /// </summary>
    private async Task DecideSignalAsync(Guid signalId, bool trade)
    {
        var phone = new PhoneNotifications(ApplicationContext!);
        phone.DismissSignal(signalId);
        try
        {
            await AppRuntime.EnsureStartedAsync(this);
            var response = await AppRuntime.Get(this).Api.HandleAsync("POST", $"/api/signals/{signalId}/{(trade ? "accept" : "skip")}",
                trade ? "{\"acceptedRules\":[]}" : null, CancellationToken.None);
            if (response.Status >= 400)
            {
                phone.Show(new PhoneNotification(trade ? "Signal not traded" : "Signal not skipped", ProblemText(response.Body),
                    HVTradingBot.Application.Notifications.NotificationKind.OrderRejected));
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HVTradingBot", $"Signal decision failed: {ex}");
        }
    }

    /// <summary>The first validation message (or the title) of an API problem response.</summary>
    private static string ProblemText(string? body)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body ?? "{}");
            if (json.RootElement.TryGetProperty("errors", out var errors))
            {
                foreach (var field in errors.EnumerateObject())
                {
                    foreach (var message in field.Value.EnumerateArray())
                    {
                        return message.GetString() ?? "";
                    }
                }
            }

            return json.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "";
        }
        catch (System.Text.Json.JsonException)
        {
            return body ?? "";
        }
    }

    private async Task StopEngineAsync()
    {
        if (_subscribed is not null)
        {
            _subscribed.Live.StatusChanged -= UpdateNotification;
            _subscribed = null;
        }

        await AppRuntime.StopAsync();
        ReleaseWakeLock();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    /// <summary>Called every two seconds with the dashboard status; the notification changes only when its text does.</summary>
    private void UpdateNotification(SystemStatusDto status)
    {
        var inv = CultureInfo.InvariantCulture;
        var title = status.KillSwitchActive ? "Kill switch active - no new trades"
            : !status.WorkerHealthy ? "Trading engine starting"
            : status.MarketData.IsStale ? "Waiting for market data"
            : $"Trading on {status.Broker.Name}{(status.Broker.IsDemo ? " demo" : "")}";
        var text = string.Create(inv,
            $"Equity {status.Account.Equity:N2} {status.Account.Currency} · {status.OpenPositions} open · today {(status.DailyRealizedPnl >= 0 ? "+" : "")}{status.DailyRealizedPnl:N2}");
        if (title + text == _shown)
        {
            return;
        }

        _shown = title + text;
        Show(EngineNotification(title, text));
    }

    private Notification EngineNotification(string title, string text)
    {
        var stop = PendingIntent.GetService(this, 1, new Intent(this, typeof(TradingService)).SetAction(ActionStop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
#pragma warning disable CA1422 // Notification.Action.Builder(int, ...) is the variant available on every supported Android version
        var stopAction = new Notification.Action.Builder(0, "Stop trading", stop).Build();
#pragma warning restore CA1422
        return PhoneNotifications.Builder(this, PhoneNotifications.EngineChannel)
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetContentIntent(PhoneNotifications.OpenApp(this))!
            .AddAction(stopAction)!
            .Build();
    }

    private void StartInForeground(Notification notification)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
        {
            StartForeground(PhoneNotifications.EngineNotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(PhoneNotifications.EngineNotificationId, notification);
        }
    }

    private void Show(Notification notification)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.Notify(PhoneNotifications.EngineNotificationId, notification);
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock is { IsHeld: true })
        {
            return;
        }

        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "HVTradingBot:engine");
        _wakeLock!.SetReferenceCounted(false);
        _wakeLock.Acquire();
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock is { IsHeld: true })
        {
            _wakeLock.Release();
        }

        _wakeLock = null;
    }
}
