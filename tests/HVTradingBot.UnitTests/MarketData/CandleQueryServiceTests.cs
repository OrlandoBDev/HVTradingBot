using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.MarketData;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.MarketData;

public class CandleQueryServiceTests
{
    /// <summary>In-memory store with the same contract as the EF store: newest <c>limit</c> bars in range, oldest first.</summary>
    private sealed class FakeCandleStore(IEnumerable<Candle> bars) : ICandleStore
    {
        public List<(string Symbol, DateTime? From, DateTime? To, int Limit)> Calls { get; } = [];

        public Task<IReadOnlyList<Candle>> GetM5Async(string symbol, DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken)
        {
            Calls.Add((symbol, fromUtc, toUtc, limit));
            IReadOnlyList<Candle> result = bars
                .Where(b => (fromUtc is not { } f || b.OpenTimeUtc >= f) && (toUtc is not { } t || b.OpenTimeUtc < t))
                .OrderBy(b => b.OpenTimeUtc)
                .TakeLast(limit)
                .ToList();
            return Task.FromResult(result);
        }
    }

    private static List<Candle> M5(int count, DateTime? start = null) =>
        Enumerable.Range(0, count).Select(i => Bars.Bar((start ?? Bars.Start) + TimeSpan.FromMinutes(5 * i), 1.1000m + i * 0.0001m)).ToList();

    private static CandleQuery Parse(string? instrument = "EUR/USD", string? timeframe = null, DateTime? from = null, DateTime? to = null, int? limit = null)
    {
        var query = CandleQueryService.TryParse(instrument, timeframe, from, to, limit, out var errors);
        Assert.Empty(errors);
        return query!;
    }

    [Fact]
    public void Parse_defaults_to_5m_and_default_limit_and_canonical_symbol()
    {
        var query = Parse("eur/usd");

        Assert.Equal("EUR/USD", query.Symbol);
        Assert.Equal(TimeFrame.M5, query.TimeFrame);
        Assert.Equal(CandleQueryService.DefaultLimit, query.Limit);
        Assert.Null(query.FromUtc);
        Assert.Null(query.ToUtc);
    }

    [Fact]
    public void Parse_accepts_timeframe_in_any_case_and_unknown_symbols_as_given()
    {
        var query = Parse("R_100", "h1", limit: 50);

        Assert.Equal("R_100", query.Symbol);
        Assert.Equal(TimeFrame.H1, query.TimeFrame);
        Assert.Equal(50, query.Limit);
    }

    [Theory]
    [InlineData(null, null, null, "instrument")]
    [InlineData("  ", null, null, "instrument")]
    [InlineData("EUR/USD", "M1", null, "timeframe")]
    [InlineData("EUR/USD", "7", null, "timeframe")]
    [InlineData("EUR/USD", null, 0, "limit")]
    [InlineData("EUR/USD", null, CandleQueryService.MaxLimit + 1, "limit")]
    public void Parse_rejects_invalid_values(string? instrument, string? timeframe, int? limit, string key)
    {
        var query = CandleQueryService.TryParse(instrument, timeframe, null, null, limit, out var errors);

        Assert.Null(query);
        Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public void Parse_rejects_from_not_before_to()
    {
        var query = CandleQueryService.TryParse("EUR/USD", null, Bars.Start, Bars.Start, null, out var errors);

        Assert.Null(query);
        Assert.Contains("from", errors.Keys);
    }

    [Fact]
    public void Parse_treats_unspecified_times_as_utc()
    {
        var query = Parse(from: new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(DateTimeKind.Utc, query.FromUtc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), query.FromUtc);
    }

    [Fact]
    public async Task M5_returns_the_newest_stored_bars_oldest_first()
    {
        var bars = M5(10);
        var service = new CandleQueryService(new FakeCandleStore(bars));

        var result = await service.GetAsync(Parse(limit: 3), CancellationToken.None);

        Assert.Equal(bars.TakeLast(3), result);
    }

    [Fact]
    public void Aggregate_builds_ohlc_like_the_engine()
    {
        var m5 = new List<Candle>
        {
            new(Bars.Start, TimeFrame.M5, 1.10m, 1.12m, 1.09m, 1.11m, 0.0001m, 10),
            new(Bars.Start.AddMinutes(5), TimeFrame.M5, 1.11m, 1.15m, 1.10m, 1.14m, 0.0002m, 20),
            new(Bars.Start.AddMinutes(10), TimeFrame.M5, 1.14m, 1.14m, 1.05m, 1.06m, 0.0003m, 30),
            new(Bars.Start.AddMinutes(15), TimeFrame.M5, 1.06m, 1.07m, 1.06m, 1.07m, 0.0004m, 40)
        };

        var bars = CandleQueryService.Aggregate(m5, TimeFrame.M15);

        Assert.Equal(2, bars.Count);
        Assert.Equal(new Candle(Bars.Start, TimeFrame.M15, 1.10m, 1.15m, 1.05m, 1.06m, 0.0003m, 60), bars[0]);
        Assert.Equal(new Candle(Bars.Start.AddMinutes(15), TimeFrame.M15, 1.06m, 1.07m, 1.06m, 1.07m, 0.0004m, 40), bars[1]);
    }

    [Fact]
    public void Aggregate_starts_a_new_bar_after_a_gap()
    {
        // 10:55 then 11:05 (10:55-11:00 bar of H1 10:00 closes early, e.g. a data gap).
        var m5 = M5(1, Bars.Start.AddHours(10).AddMinutes(55)).Concat(M5(1, Bars.Start.AddHours(11).AddMinutes(5))).ToList();

        var bars = CandleQueryService.Aggregate(m5, TimeFrame.H1);

        Assert.Equal([Bars.Start.AddHours(10), Bars.Start.AddHours(11)], bars.Select(b => b.OpenTimeUtc));
    }

    [Fact]
    public async Task Higher_timeframe_drops_the_oldest_bar_when_the_limit_cut_it_short()
    {
        // 00:30 to 10:55, so every H1 bar is complete except the first.
        var bars = M5(126, Bars.Start.AddMinutes(30));
        var store = new FakeCandleStore(bars);
        var service = new CandleQueryService(store);

        var result = await service.GetAsync(Parse(timeframe: "H1", limit: 3), CancellationToken.None);

        Assert.Equal((3 + 1) * 12, store.Calls.Single().Limit);
        Assert.Equal(3, result.Count);
        Assert.All(result, b => Assert.Equal(TimeFrame.H1, b.TimeFrame));
        Assert.Equal(bars[^1].Close, result[^1].Close);
        // Each returned bar is built from its full 12 5m bars.
        Assert.All(result, b => Assert.Equal(12 * 100, b.Volume));
        Assert.Equal(bars[^36].OpenTimeUtc, result[0].OpenTimeUtc);
    }

    [Fact]
    public async Task Higher_timeframe_keeps_everything_when_history_is_shorter_than_the_limit()
    {
        var bars = M5(30); // 2.5 hours
        var service = new CandleQueryService(new FakeCandleStore(bars));

        var result = await service.GetAsync(Parse(timeframe: "H1", limit: 10), CancellationToken.None);

        Assert.Equal([Bars.Start, Bars.Start.AddHours(1), Bars.Start.AddHours(2)], result.Select(b => b.OpenTimeUtc));
        Assert.Equal(6 * 100, result[^1].Volume); // still forming
    }

    [Fact]
    public async Task Higher_timeframe_aligns_from_to_the_bar_start()
    {
        var store = new FakeCandleStore(M5(48));
        var service = new CandleQueryService(store);

        var result = await service.GetAsync(Parse(timeframe: "H1", from: Bars.Start.AddMinutes(95)), CancellationToken.None);

        Assert.Equal(Bars.Start.AddHours(1), store.Calls.Single().From);
        Assert.Equal([Bars.Start.AddHours(1), Bars.Start.AddHours(2), Bars.Start.AddHours(3)], result.Select(b => b.OpenTimeUtc));
        Assert.All(result, b => Assert.Equal(12 * 100, b.Volume));
    }
}
