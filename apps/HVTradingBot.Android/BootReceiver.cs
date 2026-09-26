using Android.Content;
using Android.Runtime;

namespace HVTradingBot.Android;

/// <summary>Starts the engine again after the phone restarts or the app is updated, if it was running before.</summary>
[Register("com.hvtradingbot.android.BootReceiver")]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null
            && intent?.Action is Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced
            && EngineSwitch.IsOn(context))
        {
            TradingService.Start(context);
        }
    }
}
