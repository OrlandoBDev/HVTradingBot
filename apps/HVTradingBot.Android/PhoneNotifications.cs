using Android.App;
using Android.Content;
using Android.OS;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// Two notification channels: "Engine" for the permanent notification that keeps the engine running (quiet), and
/// "Trades" for trades opened and closed and kill-switch changes (heads-up).
/// </summary>
public sealed class PhoneNotifications(Context context) : IPhoneNotificationSink
{
    public const string EngineChannel = "engine";
    public const string TradesChannel = "trades";
    public const int EngineNotificationId = 1;

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
