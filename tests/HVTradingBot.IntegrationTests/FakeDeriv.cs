using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Infrastructure.Brokers.Deriv;

namespace HVTradingBot.IntegrationTests;

/// <summary>Scriptable stand-in for the Deriv WebSocket: each request type maps to a handler.</summary>
public sealed class FakeDerivSocket : IDerivSocket, IDerivSocketFactory
{
    public List<JsonObject> Requests { get; } = [];

    public Dictionary<string, Func<JsonObject, object>> Handlers { get; } = new()
    {
        ["balance"] = _ => new { balance = new { balance = 10_000m, currency = "USD" } },
        ["portfolio"] = _ => new { portfolio = new { contracts = Array.Empty<object>() } },
        ["contracts_for"] = _ => new
        {
            contracts_for = new { available = new[] { new { contract_type = "MULTUP", multiplier_range = new[] { 100, 200, 300, 500, 800 } } } }
        }
    };

    public Uri? ConnectedTo { get; private set; }

    public bool IsConnected => true;

    public int Count(string type) => Requests.Count(r => r.ContainsKey(type));

    public Task<IDerivSocket> ConnectAsync(Uri uri, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        ConnectedTo = uri;
        return Task.FromResult<IDerivSocket>(this);
    }

    public Task<JsonElement> SendAsync(JsonObject request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var type = request.Select(p => p.Key).First(k => Handlers.ContainsKey(k));
        var response = Handlers[type](request); // may throw DerivApiException / DerivConnectionException
        return Task.FromResult(JsonSerializer.SerializeToElement(response));
    }

    public Task<JsonElement> SubscribeAsync(JsonObject request, Action<JsonElement> onMessage, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Deriv REST API stub returning one demo account and an OTP WebSocket URL.</summary>
public sealed class FakeDerivRest : HttpMessageHandler
{
    public const string DemoAccount = "DOT90004580";

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var path = request.RequestUri!.AbsolutePath;
        var body = path.EndsWith("/otp")
            ? """{"data":{"url":"wss://api.derivws.com/trading/v1/options/ws/demo?otp=abc"}}"""
            : $$"""{"data":[{"account_id":"CR555","account_type":"real","currency":"USD"},{"account_id":"{{DemoAccount}}","account_type":"demo","currency":"USD"}]}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
