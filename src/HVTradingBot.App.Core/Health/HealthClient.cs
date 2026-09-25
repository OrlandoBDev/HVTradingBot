using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Health;

/// <summary>Calls the API's anonymous health endpoints.</summary>
/// <param name="http">Shared client without a base address; tests pass one built on a fake handler.</param>
public sealed class HealthClient(HttpClient http, TimeProvider time, ILogger<HealthClient> logger)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>True when <c>/health/live</c> answers 2xx; false when the API is not up (yet).</summary>
    public async Task<bool> IsLiveAsync(Uri apiBaseUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await GetAsync(new Uri(apiBaseUri, "/health/live"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            logger.LogDebug("API at {ApiBaseUri} not live yet: {Reason}", apiBaseUri, ex.Message);
            return false;
        }
    }

    /// <summary>The worker state from <c>/health/ready</c>; <see cref="WorkerState.ApiUnreachable"/> when the API does not answer.</summary>
    public async Task<WorkerStatus> GetWorkerStatusAsync(Uri apiBaseUri, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            using var response = await GetAsync(new Uri(apiBaseUri, "/health/ready"), cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            logger.LogDebug("API at {ApiBaseUri} did not answer /health/ready: {Reason}", apiBaseUri, ex.Message);
            return new WorkerStatus(WorkerState.ApiUnreachable, ex.Message, DatabaseHealthy: false);
        }

        try
        {
            return ReadyResponseParser.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unexpected /health/ready response from {ApiBaseUri}", apiBaseUri);
            return new WorkerStatus(WorkerState.ApiUnreachable, "Unexpected /health/ready response: " + ex.Message, DatabaseHealthy: false);
        }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, linked.Token);
    }

    /// <summary>Connection failures and our own request timeout; the caller's cancellation still propagates.</summary>
    private static bool IsUnreachable(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
