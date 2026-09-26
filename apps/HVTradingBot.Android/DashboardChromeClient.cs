using Android.App;
using Android.Text;
using Android.Webkit;
using Android.Widget;

namespace HVTradingBot.Android;

/// <summary>
/// Shows the dashboard's alert, confirm and prompt dialogs (place a test trade, close a position, kill switch reason,
/// reset risk limits, remove Deriv settings) as Android dialogs. Without this, a WebView answers every confirm with
/// "cancel" and every prompt with nothing, so those buttons silently did nothing.
/// </summary>
public sealed class DashboardChromeClient(Activity activity) : WebChromeClient
{
    public override bool OnJsAlert(WebView? view, string? url, string? message, JsResult? result)
    {
        Dialog(message)
            .SetPositiveButton("OK", (_, _) => result?.Confirm())!
            .Show();
        return true;
    }

    public override bool OnJsConfirm(WebView? view, string? url, string? message, JsResult? result)
    {
        Dialog(message)
            .SetPositiveButton("OK", (_, _) => result?.Confirm())!
            .SetNegativeButton("Cancel", (_, _) => result?.Cancel())!
            .Show();
        return true;
    }

    public override bool OnJsPrompt(WebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result)
    {
        var input = new EditText(activity) { Text = defaultValue ?? "", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine };
        input.SetSelectAllOnFocus(true);
        var padding = (int)(20 * activity.Resources!.DisplayMetrics!.Density);
        var frame = new FrameLayout(activity);
        frame.SetPadding(padding, 0, padding, 0);
        frame.AddView(input);

        Dialog(message)
            .SetView(frame)!
            .SetPositiveButton("OK", (_, _) => result?.Confirm(input.Text ?? ""))!
            .SetNegativeButton("Cancel", (_, _) => result?.Cancel())!
            .Show();
        return true;
    }

    /// <summary>Not cancelable by tapping outside or Back: the page waits for OK or Cancel.</summary>
    private AlertDialog.Builder Dialog(string? message) =>
        new AlertDialog.Builder(activity)
            .SetTitle("HVTradingBot")!
            .SetMessage(message ?? "")!
            .SetCancelable(false)!;
}
