using Android.Content;
using Android.Webkit;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Android;

/// <summary>
/// Serves the dashboard inside the WebView without any server: its files come from the app's assets and its API
/// requests (same origin, /api/...) are answered in-process by the engine. Links to other sites open in the browser.
/// Android calls ShouldInterceptRequest on a background thread, so waiting for the answer here is allowed.
/// </summary>
public sealed class DashboardWebViewClient(MainActivity activity) : WebViewClient
{
    /// <summary>Pages the dashboard opens to ask for something only the app can do (e.g. a file picker).</summary>
    public const string ActionPrefix = "/app-action/";

    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        if (request?.Url?.ToString() is not { } raw || !Uri.TryCreate(raw, UriKind.Absolute, out var url) || !WebBridge.IsOwnOrigin(url))
        {
            return null; // not ours: normal network loading
        }

        return WebBridge.IsApi(url) ? Api(request.Method ?? "GET", url) : Asset(url);
    }

    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        if (request?.Url is not { } uri)
        {
            return false;
        }

        if (Uri.TryCreate(uri.ToString(), UriKind.Absolute, out var url) && WebBridge.IsOwnOrigin(url))
        {
            if (!url.AbsolutePath.StartsWith(ActionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            activity.RunAppAction(url.AbsolutePath[ActionPrefix.Length..]);
            return true;
        }

        activity.StartActivity(new Intent(Intent.ActionView, uri).AddFlags(ActivityFlags.NewTask));
        return true;
    }

    private WebResourceResponse Api(string method, Uri url)
    {
        if (!EngineSwitch.IsOn(activity))
        {
            // Stopped from the notification: do not start it again behind the user's back (reopening the app does).
            return Json(new LocalApiResponse(503, """{"title":"Trading is stopped. Reopen the app to start it again.","status":503}"""));
        }

        try
        {
            AppRuntime.EnsureStartedAsync(activity).GetAwaiter().GetResult();
            var response = AppRuntime.Get(activity).Bridge.HandleApiAsync(method, url, CancellationToken.None).GetAwaiter().GetResult();
            return Json(response);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HVTradingBot", $"Dashboard request {method} {url.AbsolutePath} failed: {ex}");
            return Json(new LocalApiResponse(500, System.Text.Json.JsonSerializer.Serialize(new { title = ex.Message, status = 500 })));
        }
    }

    private WebResourceResponse Asset(Uri url)
    {
        var path = WebBridge.AssetPath(url);
        if (path is not null)
        {
            try
            {
                var stream = activity.Assets!.Open("wwwroot/" + path);
                return new WebResourceResponse(WebBridge.ContentType(path), "utf-8", 200, "OK",
                    new Dictionary<string, string> { ["Cache-Control"] = "no-cache" }, stream);
            }
            catch (Java.IO.IOException)
            {
                // Not in the app: 404 below.
            }
        }

        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", new Dictionary<string, string>(), new MemoryStream());
    }

    private static WebResourceResponse Json(LocalApiResponse response) =>
        new("application/json", "utf-8", response.Status, Reason(response.Status),
            new Dictionary<string, string> { ["Cache-Control"] = "no-store" },
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(response.Body ?? "")));

    /// <summary>Android rejects an empty reason phrase.</summary>
    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        499 => "Client Closed Request",
        503 => "Service Unavailable",
        _ => status < 400 ? "OK" : "Error"
    };
}
