using Android.App;
using Android.Content;
using Android.OS;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// Three notification channels: "Engine" for the permanent notification that keeps the engine running (quiet),
/// "Trades" for trades opened and closed and kill-switch changes (heads-up), and "Signals" for trade signals waiting
/// for the user's decision (heads-up, with Skip and optionally Trade buttons).
/// </summary>
public sealed class PhoneNotifications(Context context) : IPhoneNotificationSink
{
    public const string EngineChannel = "engine";
    public const string TradesChannel = "trades";
    public const string SignalsChannel = "signals";
    public const int EngineNotificationId = 1;

    /// <summary>Intent extra with the signal to open (a notification tap) or act on (its buttons).</summary>
    public const string SignalIdExtra = "signalId";

    private static int _nextId = 100;

    public static void CreateChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(EngineChannel, "Trading engine", NotificationImportance.Low)
        {
            Description = "Shows that the engine is running, with balance and open positions."
        });
        manager.CreateNotificationChannel(new NotificationChannel(TradesChannel, "Trades", NotificationImportance.High)
        {
            Description = "Trades opened and closed, and kill-switch changes."
        });
        manager.CreateNotificationChannel(new NotificationChannel(SignalsChannel, "Signals", NotificationImportance.High)
        {
            Description = "Trade signals waiting for your decision."
        });
    }

    public void ShowSignal(SignalAlert alert)
    {
        var builder = Builder(context, SignalsChannel)
            .SetContentTitle(alert.Title)!
            .SetContentText(alert.Text)!
            .SetStyle(new Notification.BigTextStyle().BigText(alert.Text))!
            .SetAutoCancel(true)!
            .SetContentIntent(OpenSignal(context, alert.SignalId))!
            .AddAction(SignalAction(alert.SignalId, TradingService.ActionSkipSignal, "Skip"))!;
        if (alert.OfferTrade)
        {
            builder.AddAction(SignalAction(alert.SignalId, TradingService.ActionTradeSignal, "Trade"));
        }

        var remaining = alert.ExpiresAtUtc - DateTime.UtcNow;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && remaining > TimeSpan.Zero)
        {
            builder.SetTimeoutAfter((long)remaining.TotalMilliseconds); // gone when the signal expires
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Notify(SignalNotificationId(alert.SignalId), builder.Build());
    }

    public void DismissSignal(Guid signalId)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Cancel(SignalNotificationId(signalId));
    }

    /// <summary>One notification per signal (a review of the same signal replaces it).</summary>
    public static int SignalNotificationId(Guid signalId) => 1_000_000 + (signalId.GetHashCode() & 0x3FFFFFFF);

    /// <summary>Opens the app on the signal.</summary>
    public static PendingIntent OpenSignal(Context context, Guid signalId)
    {
        var intent = new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop)
            .PutExtra(SignalIdExtra, signalId.ToString());
        return PendingIntent.GetActivity(context, SignalNotificationId(signalId), intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    private Notification.Action SignalAction(Guid signalId, string action, string label)
    {
        var intent = new Intent(context, typeof(TradingService)).SetAction(action).PutExtra(SignalIdExtra, signalId.ToString());
        // A request code per signal and button, so each notification's buttons keep their own signal.
        var requestCode = SignalNotificationId(signalId) + (action == TradingService.ActionTradeSignal ? 1 : 2);
        var pending = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? PendingIntent.GetForegroundService(context, requestCode, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)
            : PendingIntent.GetService(context, requestCode, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
#pragma warning disable CA1422 // Notification.Action.Builder(int, ...) is the variant available on every supported Android version
        return new Notification.Action.Builder(0, label, pending).Build()!;
#pragma warning restore CA1422
    }

    public void Show(PhoneNotification notification)
    {
        var builder = Builder(context, TradesChannel)
            .SetContentTitle(notification.Title)!
            .SetContentText(notification.Text)!
            .SetStyle(new Notification.BigTextStyle().BigText(notification.Text))!
            .SetAutoCancel(true)!
            .SetContentIntent(OpenApp(context))!;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.Notify(Interlocked.Increment(ref _nextId), builder.Build());
    }

    public static Notification.Builder Builder(Context context, string channel)
    {
#pragma warning disable CA1422 // the channel-less constructor is only used before Android 8
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(context, channel) : new Notification.Builder(context);
#pragma warning restore CA1422
        return builder.SetSmallIcon(Resource.Drawable.ic_notification)!;
    }

    public static PendingIntent OpenApp(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
