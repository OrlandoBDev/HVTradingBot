using System.Collections.Concurrent;
using System.Text.Json;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.Mobile.Core;
using Microsoft.Data.Sqlite;

namespace HVTradingBot.Mobile.Tests;

/// <summary>
/// The whole app minus Android: engine, dashboard router and live updates on a SQLite file, fully offline
/// (simulated prices, paper broker), exactly as the app runs it.
/// </summary>
public sealed class MobileRuntimeTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hvtradingbot-mobile-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<(string Event, string Json)> _published = new();
    private MobileRuntime _runtime = null!;

    public async Task InitializeAsync()
    {
        _runtime = MobileRuntime.Create(new MobileSettings(_folder)
        {
            MarketData = MarketDataProvider.Simulated,
            Broker = BrokerProvider.Paper,
            Overrides = new Dictionary<string, string?>
            {
                ["MarketData:Simulated:BarIntervalMilliseconds"] = "20",
                ["MarketData:Simulated:WarmupDays"] = "10"
            }
        });
        _runtime.Live.Published += (name, json) => _published.Enqueue((name, json));
        await _runtime.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _runtime.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Trades_offline_and_answers_every_dashboard_read()
    {
        await WaitForTradingAsync();

        string[] reads =
        [
            "/api/auth/me", "/api/status", "/api/markets", "/api/markets/names", "/api/decisions?limit=5", "/api/decisions/paged?page=1&pageSize=10",
            "/api/decisions/paged?state=NoTrade&instrument=EUR%2FUSD", "/api/trades/paged?page=1&pageSize=10", "/api/audit/paged?page=1&pageSize=10",
            "/api/positions/open", "/api/trades", "/api/risk", "/api/performance", "/api/audit", "/api/learning", "/api/test-trades",
            "/api/backtests?limit=20", "/api/settings/deriv", "/api/settings/risk", "/api/settings/notifications", "/api/settings/markets",
            "/api/app/engine", "/api/app/logs?count=50"
        ];
        foreach (var path in reads)
        {
            var response = await _runtime.Api.HandleAsync("GET", path, null, CancellationToken.None);
            Assert.True(response.Status == 200, $"GET {path} returned {response.Status}: {response.Body}");
            using var _ = JsonDocument.Parse(response.Body!);
        }

        await WaitUntilAsync(async () =>
        {
            using var page = await GetJsonAsync("/api/decisions/paged?page=1&pageSize=1");
            return page.RootElement.GetProperty("total").GetInt32() > 0;
        }, "the first decision");
        using var decisions = await GetJsonAsync("/api/decisions/paged?page=1&pageSize=1");
        var first = decisions.RootElement.GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(200, (await _runtime.Api.HandleAsync("GET", $"/api/decisions/{first}", null, CancellationToken.None)).Status);
        Assert.Equal(404, (await _runtime.Api.HandleAsync("GET", $"/api/decisions/{Guid.NewGuid()}", null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Kill_switch_and_validation_behave_like_the_api()
    {
        await WaitForTradingAsync();

        var on = await _runtime.Api.HandleAsync("POST", "/api/kill-switch", """{"active":true,"reason":"test"}""", CancellationToken.None);
        Assert.Equal(200, on.Status);
        using (var status = await GetJsonAsync("/api/status"))
        {
            Assert.True(status.RootElement.GetProperty("killSwitchActive").GetBoolean());
        }

        // Deactivating needs a reason: same ValidationProblem shape as the API, which the dashboard shows by the field.
        var off = await _runtime.Api.HandleAsync("POST", "/api/kill-switch", """{"active":false}""", CancellationToken.None);
        Assert.Equal(400, off.Status);
        using (var problem = JsonDocument.Parse(off.Body!))
        {
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("reason", out _));
        }

        Assert.Equal(400, (await _runtime.Api.HandleAsync("POST", "/api/kill-switch", "not json", CancellationToken.None)).Status);
        Assert.Equal(400, (await _runtime.Api.HandleAsync("GET", "/api/audit/paged?page=x", null, CancellationToken.None)).Status);
        Assert.Equal(404, (await _runtime.Api.HandleAsync("GET", "/api/nothing", null, CancellationToken.None)).Status);
        Assert.Equal(405, (await _runtime.Api.HandleAsync("DELETE", "/api/status", null, CancellationToken.None)).Status);

        using var audit = await GetJsonAsync("/api/audit/paged?page=1&pageSize=50");
        Assert.Contains(audit.RootElement.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "KillSwitchActivated" && e.GetProperty("actor").GetString() == LocalApi.Actor);
    }

    [Fact]
    public async Task Backtests_run_on_the_phone()
    {
        await WaitForTradingAsync();

        var run = await _runtime.Api.HandleAsync("POST", "/api/backtests", """{"source":"simulated","days":20,"instruments":["EUR/USD"]}""",
            CancellationToken.None);

        Assert.True(run.Status == 200, run.Body);
        using var list = await GetJsonAsync("/api/backtests?limit=5");
        Assert.Equal(1, list.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task New_market_selection_restarts_the_engine_and_is_applied()
    {
        await WaitForTradingAsync();

        var saved = await _runtime.Api.HandleAsync("PUT", "/api/settings/markets", """{"instruments":["EUR/USD","GBP/USD"]}""", CancellationToken.None);
        Assert.True(saved.Status == 200, saved.Body);
        var version = JsonDocument.Parse(saved.Body!).RootElement.GetProperty("version").GetInt32();

        await WaitUntilAsync(async () =>
        {
            using var markets = await GetJsonAsync("/api/settings/markets");
            return markets.RootElement.GetProperty("appliedVersion").GetInt32() == version && _runtime.Engine.State == EngineState.Running;
        }, "the engine to restart with the new markets");
        Assert.True(_runtime.Engine.Restarts >= 1);
        using var live = await GetJsonAsync("/api/markets");
        Assert.Equal(new[] { "EUR/USD", "GBP/USD" }, live.RootElement.EnumerateArray().Select(m => m.GetProperty("instrument").GetString()!).Order());
    }

    [Fact]
    public async Task Live_updates_push_status_and_learning()
    {
        await WaitForTradingAsync();
        await WaitUntilAsync(() => Task.FromResult(_published.Any(p => p.Event == LiveUpdates.LearningEvent)), "a learning pulse");

        var status = _published.Last(p => p.Event == LiveUpdates.StatusEvent);
        using var json = JsonDocument.Parse(status.Json);
        Assert.Equal("Paper", json.RootElement.GetProperty("broker").GetProperty("name").GetString());
        Assert.NotNull(_runtime.Live.Status);
        Assert.NotNull(_runtime.Live.Learning);
    }

    [Fact]
    public async Task WebView_requests_carry_their_body_in_the_url_and_live_updates_long_poll()
    {
        await WaitForTradingAsync();
        var origin = new Uri(WebBridge.Origin);

        // "é" and "+" check the UTF-8 and URL encoding round trip.
        var body = Uri.EscapeDataString(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"active":true,"reason":"from the WebView é>?"}""")));
        var saved = await _runtime.Bridge.HandleApiAsync("POST", new Uri(origin, $"/api/kill-switch?{WebBridge.BodyParameter}={body}"),
            CancellationToken.None);
        Assert.True(saved.Status == 200, saved.Body);
        Assert.Equal("from the WebView é>?", JsonDocument.Parse(saved.Body!).RootElement.GetProperty("killSwitchReason").GetString());

        using (var page = JsonDocument.Parse((await _runtime.Bridge.HandleApiAsync("GET",
                   new Uri(origin, $"/api/audit/paged?page=1&{WebBridge.BodyParameter}=&pageSize=2"), CancellationToken.None)).Body!))
        {
            Assert.Equal(2, page.RootElement.GetProperty("pageSize").GetInt32());
        }

        var events = await _runtime.Bridge.HandleApiAsync("GET", new Uri(origin, "/api/app/events?after=0"), CancellationToken.None);
        using var json = JsonDocument.Parse(events.Body!);
        var names = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains(LiveUpdates.StatusEvent, names);
        Assert.Contains(LiveUpdates.LearningEvent, names);

        var bad = await _runtime.Bridge.HandleApiAsync("PUT", new Uri(origin, $"/api/settings/risk?{WebBridge.BodyParameter}=%25%25%25"),
            CancellationToken.None);
        Assert.Equal(400, bad.Status);
    }

    private async Task WaitForTradingAsync() =>
        await WaitUntilAsync(async () =>
        {
            using var status = await GetJsonAsync("/api/status");
            var root = status.RootElement;
            return root.GetProperty("workerHealthy").GetBoolean() && root.GetProperty("marketData").GetProperty("lastBarTimeUtc").ValueKind == JsonValueKind.String;
        }, "the engine to process bars");

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        var response = await _runtime.Api.HandleAsync("GET", path, null, CancellationToken.None);
        Assert.True(response.Status == 200, $"GET {path} returned {response.Status}: {response.Body}");
        return JsonDocument.Parse(response.Body!);
    }

    private async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        var log = string.Join(Environment.NewLine, _runtime.Log.Recent(40).Select(e => $"{e.Level} {e.Category}: {e.Message}"));
        Assert.Fail($"Timed out waiting for {what}. Engine {_runtime.Engine.State}, last error {_runtime.Engine.LastError}.{Environment.NewLine}{log}");
    }
}
