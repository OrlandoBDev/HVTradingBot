using System.Text;

namespace HVTradingBot.Mobile.Core;

/// <summary>
/// Connects the WebView to the app. The dashboard is served from https://appassets.androidplatform.net (the host
/// Android reserves for app content), and its API requests to the same origin are answered by <see cref="LocalApi"/>
/// instead of the network. Android's request interception does not expose request bodies (and custom headers are not
/// guaranteed), so in the app the dashboard sends the JSON body base64-encoded in the <see cref="BodyParameter"/> query
/// parameter, which is always part of the intercepted URL.
/// </summary>
public sealed class WebBridge(LocalApi api, EventFeed events)
{
    public const string Origin = "https://appassets.androidplatform.net";
    public const string BodyParameter = "_body";

    /// <summary>Marks the WebView's user agent so the dashboard knows it runs inside the app.</summary>
    public const string UserAgentMarker = "HVTradingBotApp/1";

    /// <summary>Longest a live-update request is held open waiting for news.</summary>
    public static readonly TimeSpan EventWait = TimeSpan.FromSeconds(20);

    /// <summary>True for requests the app answers itself (API calls); everything else is a dashboard file.</summary>
    public static bool IsApi(Uri url) => IsOwnOrigin(url) && url.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);

    public static bool IsOwnOrigin(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && string.Equals(url.Host, new Uri(Origin).Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>The dashboard file for a request path ("/" is index.html); null for paths that try to leave the folder.</summary>
    public static string? AssetPath(Uri url)
    {
        var path = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/');
        if (path.Length == 0)
        {
            return "index.html";
        }

        return path.Split('/').Any(segment => segment is "" or "." or "..") ? null : path;
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".json" => "application/json",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    public async Task<LocalApiResponse> HandleApiAsync(string method, Uri url, CancellationToken cancellationToken)
    {
        var parameters = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToList();
        string? Parameter(string name) => parameters.FirstOrDefault(p => p[0] == name) is { Length: 2 } pair ? Uri.UnescapeDataString(pair[1]) : null;

        if (url.AbsolutePath == "/api/app/events" && method == "GET")
        {
            var after = long.TryParse(Parameter("after"), out var value) ? value : 0;
            return new LocalApiResponse(200, await events.WaitAsync(after, EventWait, cancellationToken));
        }

        string? body = null;
        if (Parameter(BodyParameter) is { Length: > 0 } encoded)
        {
            try
            {
                body = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return new LocalApiResponse(400, """{"title":"The request body is not valid base64.","status":400}""");
            }
        }

        var query = string.Join('&', parameters.Where(p => p[0] != BodyParameter).Select(p => string.Join('=', p)));
        return await api.HandleAsync(method, url.AbsolutePath + (query.Length > 0 ? "?" + query : ""), body, cancellationToken);
    }
}
