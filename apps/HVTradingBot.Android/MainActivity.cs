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
        _webView.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0F172A"));

        // Android 15 draws apps edge to edge: keep the dashboard clear of the status and navigation bars.
        var root = new FrameLayout(this);
        root.AddView(_webView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        root.SetOnApplyWindowInsetsListener(new SystemBarPadding());
        SetContentView(root);

        StartEngine();
        _webView.LoadUrl(WebBridge.Origin + "/");
        AskForNotifications();
    }

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
