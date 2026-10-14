using HVTradingBot.Application.News;
using HVTradingBot.Domain.News;

namespace HVTradingBot.Infrastructure.News;

/// <summary>
/// Offline news for development with the simulated market: a generated economic calendar and headlines. Everything
/// is derived from the seed and the time, so the same seed always produces the same news (and restarts, tests and
/// backtests reproduce exactly). It works in market time, so it keeps up with the accelerated simulated feed.
/// </summary>
public sealed class SimulatedNewsSource(int seed = SimulatedNewsSource.DefaultSeed) : INewsSource
{
    public const int DefaultSeed = 20260926;

    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "JPY", "AUD", "CAD", "CHF", "NZD"];
    private static readonly (int Hour, int Minute)[] ReleaseTimes = [(1, 30), (7, 0), (8, 30), (9, 0), (12, 30), (14, 0), (18, 0), (23, 50)];

    private static readonly (string Title, EventImpact Impact)[] Releases =
    [
        ("CPI y/y", EventImpact.High),
        ("Interest Rate Decision", EventImpact.High),
        ("Employment Change", EventImpact.High),
        ("GDP q/q", EventImpact.High),
        ("Retail Sales m/m", EventImpact.Medium),
        ("Manufacturing PMI", EventImpact.Medium),
        ("Trade Balance", EventImpact.Low),
        ("Building Permits", EventImpact.Low)
    ];

    private static readonly Dictionary<string, string> Names = new()
    {
        ["USD"] = "Dollar", ["EUR"] = "Euro", ["GBP"] = "Pound", ["JPY"] = "Yen",
        ["AUD"] = "Aussie", ["CAD"] = "Loonie", ["CHF"] = "Swiss franc", ["NZD"] = "Kiwi"
    };

    private static readonly string[] Bullish = ["rallies as data beats expectations", "firmer after hawkish comments", "climbs on strong jobs data", "gains as inflation runs hot"];
    private static readonly string[] Bearish = ["slides as data misses expectations", "weaker after dovish comments", "falls on weak jobs data", "drops as growth slows"];
    private static readonly string[] Neutral = ["steady ahead of key data", "little changed in quiet trade"];

    public string Name => "simulated";

    public bool FollowsMarketTime => true;

    public Task<NewsSnapshot> FetchAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var events = Calendar(nowUtc.Date.AddDays(-1), nowUtc.Date.AddDays(7)).ToList();
        var headlines = Headlines(nowUtc.AddHours(-48), nowUtc).ToList();
        return Task.FromResult(new NewsSnapshot(events, headlines, nowUtc, true, Name));
    }

    /// <summary>Releases for each day in [from, to): each currency has zero to two, at typical release times.</summary>
    public IEnumerable<EconomicEvent> Calendar(DateTime fromDateUtc, DateTime toDateUtc)
    {
        for (var day = fromDateUtc.Date; day < toDateUtc.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var currency in Currencies)
            {
                var random = new Random(StableSeed($"{seed}|{day:yyyyMMdd}|{currency}"));
                var count = random.Next(0, 3);
                for (var i = 0; i < count; i++)
                {
                    var (hour, minute) = ReleaseTimes[random.Next(ReleaseTimes.Length)];
                    var (title, impact) = Releases[random.Next(Releases.Length)];
                    var time = day.AddHours(hour).AddMinutes(minute);
                    yield return new EconomicEvent($"sim-{currency}-{time:yyyyMMddHHmm}-{i}", currency, $"{currency} {title}", time, impact);
                }
            }
        }
    }

    /// <summary>Headlines published in (from, to]: zero to two per hour, each about one currency.</summary>
    public IEnumerable<NewsHeadline> Headlines(DateTime fromUtc, DateTime toUtc)
    {
        var hour = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);
        for (; hour <= toUtc; hour = hour.AddHours(1))
        {
            // A slowly changing bias per currency and day makes sentiment trend rather than flip every hour.
            var random = new Random(StableSeed($"{seed}|{hour:yyyyMMddHH}|news"));
            var count = random.Next(0, 3);
            for (var i = 0; i < count; i++)
            {
                var currency = Currencies[random.Next(Currencies.Length)];
                var bias = new Random(StableSeed($"{seed}|{hour:yyyyMMdd}|{currency}|bias")).NextDouble() - 0.5;
                var roll = random.NextDouble() - 0.5 + bias;
                var phrases = roll > 0.2 ? Bullish : roll < -0.2 ? Bearish : Neutral;
                var title = $"{Names[currency]} {phrases[random.Next(phrases.Length)]}";
                var published = hour.AddMinutes(random.Next(0, 60));
                if (published <= fromUtc || published > toUtc)
                {
                    continue;
                }

                yield return new NewsHeadline($"sim-{published:yyyyMMddHHmm}-{i}", published, Name, title, HeadlineSentiment.Score(title));
            }
        }
    }

    /// <summary>FNV-1a, so seeds are identical across processes (string.GetHashCode is randomised per process).</summary>
    private static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
