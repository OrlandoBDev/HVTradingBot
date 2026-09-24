using System.Net.Http.Headers;
using System.Text.Json;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

public sealed record DerivAccount(string AccountId, string AccountType, string Currency)
{
    public bool IsDemo => string.Equals(AccountType, "demo", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Deriv REST API (https://api.derivws.com): lists the token's accounts and issues one-time passwords for the
/// authenticated WebSocket. Authenticates with a Personal Access Token plus the Deriv-App-ID header.
/// </summary>
public sealed class DerivRestClient(HttpClient http, DerivOptions options)
{
    public async Task<IReadOnlyList<DerivAccount>> GetAccountsAsync(DerivCredentials credentials, CancellationToken cancellationToken)
    {
        using var request = NewRequest(credentials, HttpMethod.Get, "/trading/v1/options/accounts");
        using var document = await SendAsync(request, "list accounts", cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new DerivApiException("UnexpectedResponse", "Account list response has no 'data' array.");
        }

        return data.EnumerateArray()
            .Where(a => a.TryGetProperty("account_id", out _))
            .Select(a => new DerivAccount(
                a.GetProperty("account_id").GetString()!,
                a.TryGetProperty("account_type", out var t) ? t.GetString() ?? "" : "",
                a.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : ""))
            .ToList();
    }

    /// <summary>Returns the ready-to-use WebSocket URL (with a single-use, 120-second OTP) for the account.</summary>
    public async Task<Uri> CreateWebSocketUrlAsync(DerivCredentials credentials, string accountId, CancellationToken cancellationToken)
    {
        using var request = NewRequest(credentials, HttpMethod.Post, $"/trading/v1/options/accounts/{Uri.EscapeDataString(accountId)}/otp");
        using var document = await SendAsync(request, "create OTP", cancellationToken);
        var url = document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var u) ? u.GetString() : null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "wss"
            ? uri
            : throw new DerivApiException("UnexpectedResponse", "OTP response did not contain a WebSocket URL.");
    }

    private HttpRequestMessage NewRequest(DerivCredentials credentials, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(options.RestBaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ApiToken);
        request.Headers.Add("Deriv-App-ID", credentials.AppId);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DerivConnectionException($"Deriv REST call failed ({operation}).", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var hint = (int)response.StatusCode is 401 or 403
                    ? " Check the App ID and Personal Access Token in Settings."
                    : "";
                throw new DerivApiException($"HTTP{(int)response.StatusCode}", $"{operation} failed: {Summarize(body)}.{hint}");
            }

            return JsonDocument.Parse(body);
        }
    }

    /// <summary>Extracts error messages without echoing arbitrary response content.</summary>
    private static string Summarize(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join("; ", errors.EnumerateArray().Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString()));
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }
}
