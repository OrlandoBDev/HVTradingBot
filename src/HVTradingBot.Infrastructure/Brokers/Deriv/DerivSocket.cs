using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>JSON request/response over one Deriv WebSocket connection, correlated by req_id.</summary>
public interface IDerivSocket : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Sends a request and returns the response message. Throws <see cref="DerivApiException"/> on an API error
    /// and <see cref="DerivConnectionException"/> when the outcome is unknown (timeout, disconnect).</summary>
    Task<JsonElement> SendAsync(JsonObject request, CancellationToken cancellationToken);

    /// <summary>Sends a subscribing request. Every later message for it is passed to <paramref name="onMessage"/>.</summary>
    Task<JsonElement> SubscribeAsync(JsonObject request, Action<JsonElement> onMessage, CancellationToken cancellationToken);
}

public interface IDerivSocketFactory
{
    Task<IDerivSocket> ConnectAsync(Uri uri, TimeSpan requestTimeout, CancellationToken cancellationToken);
}

public sealed class DerivSocketFactory(ILoggerFactory loggerFactory) : IDerivSocketFactory
{
    public async Task<IDerivSocket> ConnectAsync(Uri uri, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        var socket = new DerivSocket(requestTimeout, loggerFactory.CreateLogger<DerivSocket>());
        await socket.ConnectAsync(uri, cancellationToken);
        return socket;
    }
}

public sealed class DerivSocket : IDerivSocket
{
    // Deriv closes idle sessions after 2 minutes.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly ClientWebSocket _ws = new();
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger<DerivSocket> _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<long, Action<JsonElement>> _subscriptions = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private long _nextRequestId;
    private Task? _receiveLoop;
    private Task? _keepAlive;

    public DerivSocket(TimeSpan requestTimeout, ILogger<DerivSocket> logger)
    {
        _requestTimeout = requestTimeout;
        _logger = logger;
    }

    public bool IsConnected => _ws.State == WebSocketState.Open && !_lifetime.IsCancellationRequested;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            await _ws.ConnectAsync(uri, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException)
        {
            // Never include the URI: authenticated URLs carry a one-time password.
            throw new DerivConnectionException($"Could not connect to Deriv WebSocket at {uri.Host}{uri.AbsolutePath}.", ex);
        }

        _receiveLoop = Task.Run(ReceiveLoopAsync);
        _keepAlive = Task.Run(KeepAliveLoopAsync);
    }

    public Task<JsonElement> SendAsync(JsonObject request, CancellationToken cancellationToken) =>
        SendCoreAsync(request, null, cancellationToken);

    public Task<JsonElement> SubscribeAsync(JsonObject request, Action<JsonElement> onMessage, CancellationToken cancellationToken)
    {
        request["subscribe"] = 1;
        return SendCoreAsync(request, onMessage, cancellationToken);
    }

    private async Task<JsonElement> SendCoreAsync(JsonObject request, Action<JsonElement>? onMessage, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new DerivConnectionException("Deriv WebSocket is not connected.");
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        request["req_id"] = requestId;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;
        if (onMessage is not null)
        {
            _subscriptions[requestId] = onMessage;
        }

        try
        {
            var payload = Encoding.UTF8.GetBytes(request.ToJsonString());
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(_requestTimeout);
            var response = await completion.Task.WaitAsync(timeout.Token);
            ThrowIfError(response);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _subscriptions.TryRemove(requestId, out _);
            throw new DerivConnectionException($"No response from Deriv within {_requestTimeout.TotalSeconds:0}s ({FirstKey(request)}).");
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            _subscriptions.TryRemove(requestId, out _);
            throw new DerivConnectionException($"Deriv connection failed during {FirstKey(request)}.", ex);
        }
        catch (DerivApiException)
        {
            _subscriptions.TryRemove(requestId, out _);
            throw;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!_lifetime.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _lifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("Deriv closed the WebSocket: {Status} {Description}", result.CloseStatus, result.CloseStatusDescription);
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                Dispatch(message.ToArray());
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException)
        {
            _logger.LogWarning(ex, "Deriv WebSocket receive loop ended");
        }
        finally
        {
            _lifetime.Cancel();
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new DerivConnectionException("Deriv connection closed before a response arrived."));
            }
        }
    }

    private void Dispatch(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement.Clone();
        if (!root.TryGetProperty("req_id", out var idElement) || !idElement.TryGetInt64(out var requestId))
        {
            return;
        }

        if (_pending.TryGetValue(requestId, out var completion) && completion.TrySetResult(root))
        {
            return;
        }

        if (_subscriptions.TryGetValue(requestId, out var handler))
        {
            try
            {
                handler(root);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deriv subscription handler failed");
            }
        }
    }

    private async Task KeepAliveLoopAsync()
    {
        using var timer = new PeriodicTimer(KeepAliveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                try
                {
                    await SendAsync(new JsonObject { ["ping"] = 1 }, _lifetime.Token);
                }
                catch (DerivConnectionException ex)
                {
                    _logger.LogWarning("Deriv keep-alive failed: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void ThrowIfError(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var c) ? c.GetString() ?? "Error" : "Error";
            var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            throw new DerivApiException(code, message);
        }
    }

    private static string FirstKey(JsonObject request) => request.Select(p => p.Key).FirstOrDefault(k => k != "req_id") ?? "request";

    public async ValueTask DisposeAsync()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", closeTimeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Best effort on shutdown.
            }
        }

        if (_receiveLoop is not null) await _receiveLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (_keepAlive is not null) await _keepAlive.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _ws.Dispose();
        _lifetime.Dispose();
        _sendLock.Dispose();
    }
}
