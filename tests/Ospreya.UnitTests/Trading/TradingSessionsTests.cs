using Ospreya.Application.Trading;
using Ospreya.Domain.MarketData;

namespace Ospreya.UnitTests.Trading;

public class TradingSessionsTests
{
    private static readonly Instrument EurUsd = Instrument.CurrencyPair("EUR", "USD");
    private static readonly Instrument GbpUsd = Instrument.CurrencyPair("GBP", "USD");
    private static readonly Instrument EurGbp = Instrument.CurrencyPair("EUR", "GBP");
    private static readonly Instrument UsdCad = Instrument.CurrencyPair("USD", "CAD");
    private static readonly Instrument UsdChf = Instrument.CurrencyPair("USD", "CHF");
    private static readonly Instrument UsdJpy = Instrument.CurrencyPair("USD", "JPY");
    private static readonly Instrument AudUsd = Instrument.CurrencyPair("AUD", "USD");
    private static readonly Instrument AudJpy = Instrument.CurrencyPair("AUD", "JPY");
    private static readonly Instrument AudNzd = Instrument.CurrencyPair("AUD", "NZD");
    private static readonly Instrument NzdJpy = Instrument.CurrencyPair("NZD", "JPY");

    private static readonly Instrument Gold = new("XAU/USD", "XAU", "USD", 0.01m, 2) { AssetClass = AssetClass.Commodity };

    private static readonly Instrument[] Candidates = [EurUsd, GbpUsd, EurGbp, UsdCad, UsdChf, UsdJpy, AudUsd, AudJpy, AudNzd, NzdJpy];

    [Fact]
    public void London_and_New_York_overlap_picks_the_most_liquid_pairs_of_both_sessions()
    {
        var utc = new DateTime(2026, 10, 14, 13, 0, 0, DateTimeKind.Utc); // 14:00 London, 09:00 New York

        Assert.Equal(["London", "New York"], TradingSessions.OpenAt(utc).Select(s => s.Name));
        Assert.Equal([EurUsd, GbpUsd, EurGbp, UsdCad], TradingSessions.Pick(Candidates, utc));
    }

    [Fact]
    public void Asian_hours_prefer_pairs_of_the_Sydney_and_Tokyo_currencies()
    {
        var utc = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc); // 12:00 Sydney (daylight saving), 10:00 Tokyo

        Assert.Equal(["Sydney", "Tokyo"], TradingSessions.OpenAt(utc).Select(s => s.Name));
        Assert.Equal([AudJpy, NzdJpy, AudNzd, UsdJpy], TradingSessions.Pick(Candidates, utc));
    }

    [Fact]
    public void Nothing_is_picked_on_weekends()
    {
        var saturday = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

        Assert.Empty(TradingSessions.OpenAt(saturday));
        Assert.Empty(TradingSessions.Pick(Candidates, saturday));
    }

    [Fact]
    public void Sessions_follow_local_daylight_saving()
    {
        // 07:30 UTC is 08:30 in London in summer (open) but 07:30 in winter (not yet open).
        Assert.Contains("London", TradingSessions.OpenAt(new DateTime(2026, 7, 1, 7, 30, 0, DateTimeKind.Utc)).Select(s => s.Name));
        Assert.DoesNotContain("London", TradingSessions.OpenAt(new DateTime(2026, 1, 7, 7, 30, 0, DateTimeKind.Utc)).Select(s => s.Name));
    }

    [Fact]
    public void Universe_follows_session_candidates_and_adds_at_most_four_outside_the_selection()
    {
        var universe = TradingUniverse.From([EurUsd, UsdJpy], "USD", sessionCandidates: [.. Candidates, Gold]);
        var overlap = new DateTime(2026, 10, 14, 13, 0, 0, DateTimeKind.Utc);

        Assert.Equal([EurUsd, UsdJpy], universe.Traded);
        Assert.DoesNotContain(EurUsd, universe.SessionCandidates); // already selected
        Assert.DoesNotContain(universe.SessionCandidates, i => !i.IsCurrencyPair);
        Assert.All(universe.SessionCandidates, i => Assert.Contains(i, universe.Data)); // prices followed for monitoring
        Assert.Equal([GbpUsd, EurGbp, UsdCad, UsdChf], universe.SessionMarkets(overlap));
        Assert.Empty(TradingUniverse.From([EurUsd], "USD").SessionMarkets(overlap)); // setting off
    }
}
