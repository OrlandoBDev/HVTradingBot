using Android.Content;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// The one <see cref="MobileRuntime"/> of the app process, shared by the trading service (which starts and stops it)
/// and the dashboard WebView (which talks to it). Everything is stored in the app's private files folder.
/// </summary>
public static class AppRuntime
{
    private static readonly Lock Gate = new();
    private static MobileRuntime? _runtime;
    private static Task? _started;

    public static MobileRuntime Get(Context context)
    {
        lock (Gate)
        {
            if (_runtime is null)
            {
                var log = new AppLog();
                log.Written += e => global::Android.Util.Log.WriteLine(Priority(e.Level), "HVTradingBot", $"{e.Category}: {e.Message}");
                var settings = new MobileSettings(Path.Combine(context.FilesDir!.AbsolutePath, "hvtradingbot"));
                _runtime = MobileRuntime.Create(settings, new PhoneNotifications(context.ApplicationContext!), log);
            }

            return _runtime;
        }
    }

    /// <summary>Starts the engine once (idempotent); the dashboard waits on it before its first request.</summary>
    public static Task EnsureStartedAsync(Context context)
    {
        var runtime = Get(context);
        lock (Gate)
        {
            if (_started is null || _started.IsFaulted || (_started.IsCompletedSuccessfully && !runtime.IsRunning))
            {
                _started = runtime.StartAsync(CancellationToken.None);
            }

            return _started;
        }
    }

    /// <summary>Stops trading (the notification's Stop button). Open positions keep their broker-side stop and target.</summary>
    public static async Task StopAsync()
    {
        MobileRuntime? runtime;
        lock (Gate)
        {
            runtime = _runtime;
            _runtime = null;
            _started = null;
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }
    }

    private static global::Android.Util.LogPriority Priority(Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Critical or Microsoft.Extensions.Logging.LogLevel.Error => global::Android.Util.LogPriority.Error,
        Microsoft.Extensions.Logging.LogLevel.Warning => global::Android.Util.LogPriority.Warn,
        _ => global::Android.Util.LogPriority.Info
    };
}
