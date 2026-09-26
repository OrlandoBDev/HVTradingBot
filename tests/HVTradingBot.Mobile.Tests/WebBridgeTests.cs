using System.Text.Json;
using HVTradingBot.Mobile.Core;

namespace HVTradingBot.Mobile.Tests;

public class WebBridgeTests
{
    [Theory]
    [InlineData("https://appassets.androidplatform.net/", "index.html")]
    [InlineData("https://appassets.androidplatform.net/assets/index-abc.js", "assets/index-abc.js")]
    // Uri collapses dot segments (also encoded ones) before the bridge sees the path, so nothing leaves the folder.
    [InlineData("https://appassets.androidplatform.net/assets/../../secret", "secret")]
    [InlineData("https://appassets.androidplatform.net/assets/%2e%2e/x", "x")]
    [InlineData("https://appassets.androidplatform.net/assets//x", null)]
    public void Asset_paths_stay_inside_the_dashboard_folder(string url, string? expected) =>
        Assert.Equal(expected, WebBridge.AssetPath(new Uri(url)));

    [Theory]
    [InlineData("https://appassets.androidplatform.net/api/status", true)]
    [InlineData("https://appassets.androidplatform.net/index.html", false)]
    [InlineData("https://example.com/api/status", false)]
    [InlineData("http://appassets.androidplatform.net/api/status", false)]
    public void Only_same_origin_api_requests_are_answered_in_process(string url, bool expected) =>
        Assert.Equal(expected, WebBridge.IsApi(new Uri(url)));

    [Fact]
    public async Task Events_are_returned_at_once_when_newer_and_otherwise_awaited()
    {
        var feed = new EventFeed();
        feed.Publish("status", """{"a":1}""");

        using var first = JsonDocument.Parse(await feed.WaitAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(1, first.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(1, first.RootElement.GetProperty("events")[0].GetProperty("data").GetProperty("a").GetInt32());

        var waiting = feed.WaitAsync(1, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.False(waiting.IsCompleted);
        feed.Publish("learning", """{"version":"x"}""");
        using var second = JsonDocument.Parse(await waiting);
        var only = Assert.Single(second.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal("learning", only.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Nothing_new_returns_an_empty_list_after_the_timeout()
    {
        var feed = new EventFeed();
        feed.Publish("status", "{}");

        using var result = JsonDocument.Parse(await feed.WaitAsync(1, TimeSpan.FromMilliseconds(50), CancellationToken.None));

        Assert.Equal(0, result.RootElement.GetProperty("events").GetArrayLength());
    }

    [Fact]
    public async Task Sequence_from_before_an_app_restart_gets_everything()
    {
        var feed = new EventFeed();
        feed.Publish("status", "{}");

        using var result = JsonDocument.Parse(await feed.WaitAsync(500, TimeSpan.FromSeconds(1), CancellationToken.None));

        Assert.Equal(1, result.RootElement.GetProperty("events").GetArrayLength());
    }
}
