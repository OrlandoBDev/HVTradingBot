using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// Shows the dashboard (the same React app as the web version) and makes sure the engine runs: opening the app starts
/// the trading service, which keeps running after the app is closed until "Stop trading" in its notification.
/// </summary>
[Activity(
    Label = "HVTradingBot",
    MainLauncher = true,
    Theme = "@style/AppTheme",
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private const int NotificationPermissionRequest = 1;
    private const int ExportBackupRequest = 10;
    private const int RestoreBackupRequest = 11;
    private const string AskedBatteryKey = "asked-battery";

    private WebView? _webView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PhoneNotifications.CreateChannels(this);

        _webView = new WebView(this);
        var settings = _webView.Settings!;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = false;
        settings.UserAgentString = $"{settings.UserAgentString} {WebBridge.UserAgentMarker}";
        _webView.SetWebViewClient(new DashboardWebViewClient(this));
        // Confirmations and the kill switch's reason prompt need a chrome client; without one they never show.
        _webView.SetWebChromeClient(new DashboardChromeClient(this));
        _webView.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0F172A"));

        // Android 15 draws apps edge to edge: keep the dashboard clear of the status and navigation bars.
        var root = new FrameLayout(this);
        root.AddView(_webView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        root.SetOnApplyWindowInsetsListener(new SystemBarPadding());
        SetContentView(root);

        StartEngine();
        // Opened from a signal notification: straight to that signal.
        _webView.LoadUrl(WebBridge.Origin + "/" + SignalHash(Intent));
        AskForNotifications();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (SignalHash(intent) is { Length: > 0 } hash)
        {
            _webView?.EvaluateJavascript($"window.location.hash = '{hash[1..]}';", null);
        }
    }

    /// <summary>"#/signals/{id}" for an intent from a signal notification, otherwise empty.</summary>
    private static string SignalHash(Intent? intent) =>
        Guid.TryParse(intent?.GetStringExtra(PhoneNotifications.SignalIdExtra), out var id) ? $"#/signals/{id}" : "";

    protected override void OnResume()
    {
        base.OnResume();
        if (!EngineSwitch.IsOn(this))
        {
            // Stopped from the notification while the app was in the background: opening the app starts it again.
            StartEngine();
            _webView?.Reload();
        }
    }

#pragma warning disable CS0672, CA1422 // OnBackPressed: the dashboard's pages are WebView history entries
    public override void OnBackPressed()
    {
        if (_webView?.CanGoBack() == true)
        {
            _webView.GoBack();
        }
        else
        {
            base.OnBackPressed();
        }
    }
#pragma warning restore CS0672, CA1422

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == NotificationPermissionRequest)
        {
            AskToIgnoreBatteryOptimizations();
        }
    }

    private void StartEngine()
    {
        EngineSwitch.Set(this, true);
        TradingService.Start(this);
    }

    private void AskForNotifications()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequest);
        }
        else
        {
            AskToIgnoreBatteryOptimizations();
        }
    }

    /// <summary>
    /// Battery optimization (Doze) would delay the engine's timers and network while the phone sleeps. Asked once; it
    /// can be changed later in Android's battery settings for the app.
    /// </summary>
    private void AskToIgnoreBatteryOptimizations()
    {
        var power = (PowerManager)GetSystemService(PowerService)!;
        var preferences = GetSharedPreferences("app", FileCreationMode.Private)!;
        if (power.IsIgnoringBatteryOptimizations(PackageName) || preferences.GetBoolean(AskedBatteryKey, false))
        {
            return;
        }

        preferences.Edit()!.PutBoolean(AskedBatteryKey, true)!.Apply();
        try
        {
            StartActivity(new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, global::Android.Net.Uri.Parse($"package:{PackageName}")));
        }
        catch (ActivityNotFoundException)
        {
            // Some phones do not offer the dialog; the battery settings page does the same.
        }
    }

    /// <summary>Something the dashboard asked for that only the app can do (see <see cref="DashboardWebViewClient.ActionPrefix"/>).</summary>
    public void RunAppAction(string action)
    {
        switch (action)
        {
            case "backup-export":
                // The user picks where the backup goes (Google Drive, Downloads, ...).
                StartActivityForResult(new Intent(Intent.ActionCreateDocument).AddCategory(Intent.CategoryOpenable).SetType("application/zip")
                    .PutExtra(Intent.ExtraTitle, $"HVTradingBot-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip"), ExportBackupRequest);
                break;
            case "backup-restore":
                StartActivityForResult(new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable).SetType("*/*")
                    .PutExtra(Intent.ExtraMimeTypes, ["application/zip", "application/x-zip-compressed", "application/octet-stream"]), RestoreBackupRequest);
                break;
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode is not (ExportBackupRequest or RestoreBackupRequest))
        {
            return;
        }

        if (resultCode != Result.Ok || data?.Data is not { } uri)
        {
            ReportBackup("cancelled", "");
            return;
        }

        _ = requestCode == ExportBackupRequest ? ExportBackupAsync(uri) : RestoreBackupAsync(uri);
    }

    private async Task ExportBackupAsync(global::Android.Net.Uri uri)
    {
        ReportBackup("working", "Saving the backup…");
        try
        {
            var info = await Task.Run(async () =>
            {
                await using var stream = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("The chosen location cannot be written.");
                return await AppRuntime.Get(this).Backup.ExportAsync(stream, CancellationToken.None);
            });
            ReportBackup("exported", $"Backup saved: {info.ClosedTrades} closed trades, {info.Decisions} decisions, {info.DatabaseBytes / 1_048_576.0:0.#} MB.");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HVTradingBot", $"Backup failed: {ex}");
            ReportBackup("failed", $"Backup failed: {ex.Message}");
        }
    }

    private async Task RestoreBackupAsync(global::Android.Net.Uri uri)
    {
        ReportBackup("working", "Checking the backup…");
        var copy = Path.Combine(CacheDir!.AbsolutePath, "restore.zip");
        BackupInfo info;
        try
        {
            info = await Task.Run(async () =>
            {
                // A local copy: the picked file may be a stream that cannot seek (Google Drive).
                await using (var source = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("The chosen file cannot be read."))
                await using (var target = File.Create(copy))
                {
                    await source.CopyToAsync(target);
                }

                await using var file = File.OpenRead(copy);
                return await new DataBackup(AppRuntime.Settings(this)).InspectAsync(file, CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            File.Delete(copy);
            ReportBackup("failed", ex is BackupException ? ex.Message : $"The backup cannot be read: {ex.Message}");
            return;
        }

        var created = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(info.CreatedAtUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
        new AlertDialog.Builder(this)
            .SetTitle("Restore this backup?")!
            .SetMessage($"Made {created:yyyy-MM-dd HH:mm}: {info.ClosedTrades} closed trades, {info.Decisions} decisions, {info.Signals} signals.\n\n" +
                        "It replaces the data on this phone. Your current data is saved first, inside the app, so nothing is lost. " +
                        "Trading stops for a moment and starts again on the restored data. On a different phone, enter your Deriv " +
                        "token (and email password) again afterwards.")!
            .SetPositiveButton("Restore", (_, _) => _ = ConfirmRestoreAsync(copy))!
            .SetNegativeButton("Cancel", (_, _) =>
            {
                File.Delete(copy);
                ReportBackup("cancelled", "");
            })!
            .SetCancelable(false)!
            .Show();
    }

    private async Task ConfirmRestoreAsync(string copy)
    {
        ReportBackup("working", "Restoring… trading restarts in a moment.");
        try
        {
            var info = await Task.Run(() => AppRuntime.RestoreAsync(this, copy));
            var message = $"Restored: {info.ClosedTrades} closed trades, {info.Decisions} decisions.";
            ReportBackup("restored", message);
            // The dashboard reloads on the restored data, so say it here as well.
            RunOnUiThread(() => Toast.MakeText(this, message, ToastLength.Long)!.Show());
            RunOnUiThread(() => _webView?.Reload());
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HVTradingBot", $"Restore failed: {ex}");
            ReportBackup("failed", ex is BackupException ? ex.Message : $"Restore failed, nothing was changed: {ex.Message}");
        }
        finally
        {
            File.Delete(copy);
        }
    }

    /// <summary>Tells the dashboard page how a backup or restore went (it listens for "hv:backup").</summary>
    private void ReportBackup(string state, string message) => RunOnUiThread(() =>
    {
        var detail = System.Text.Json.JsonSerializer.Serialize(new { state, message });
        _webView?.EvaluateJavascript($"window.dispatchEvent(new CustomEvent('hv:backup', {{ detail: {detail} }}));", null);
    });

    private sealed class SystemBarPadding : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.Ime());
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
                return WindowInsets.Consumed;
            }

#pragma warning disable CA1422 // the pre-Android 11 inset API
            view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
            return insets.ConsumeSystemWindowInsets();
#pragma warning restore CA1422
        }
    }
}
