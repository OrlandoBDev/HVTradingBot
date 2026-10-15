using Ospreya.Domain.MarketData;

namespace Ospreya.Application.Trading;

/// <summary>
/// The four main Forex sessions (Sydney, Tokyo, London, New York), each in its own time zone so daylight saving is
/// handled, and the currencies each one is home to. Picks the Forex pairs that are most active right now: both
/// currencies' sessions open first (e.g. EUR/USD while London and New York overlap), then one, most liquid first.
/// </summary>
public static class TradingSessions
{
    /// <summary>At most this many session markets are added to the user's selection at a time.</summary>
    public const int MaxSessionMarkets = 4;

    public sealed record Session(string Name, string TimeZoneId, int OpenHour, int CloseHour, IReadOnlyList<string> Currencies);

    public static IReadOnlyList<Session> All { get; } =
    [
        new("Sydney", "Australia/Sydney", 8, 17, ["AUD", "NZD"]),
        new("Tokyo", "Asia/Tokyo", 9, 18, ["JPY"]),
        new("London", "Europe/London", 8, 17, ["GBP", "EUR", "CHF", "PLN"]),
        new("New York", "America/New_York", 8, 17, ["USD", "CAD", "MXN"])
    ];

    /// <summary>Most traded first; a pair ranks by the sum of its currencies' places.</summary>
    private static readonly string[] Liquidity = ["USD", "EUR", "JPY", "GBP", "AUD", "CAD", "CHF", "NZD"];

    /// <summary>Sessions open at <paramref name="utc"/>: their local business hours, Monday to Friday.</summary>
    public static IReadOnlyList<Session> OpenAt(DateTime utc) => All.Where(s =>
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId));
        return local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && local.Hour >= s.OpenHour && local.Hour < s.CloseHour;
    }).ToList();

    /// <summary>
    /// Up to <paramref name="max"/> currency pairs from <paramref name="candidates"/> with at least one currency whose
    /// session is open at <paramref name="utc"/>: pairs with both sessions open first, then by liquidity.
    /// </summary>
    public static IReadOnlyList<Instrument> Pick(IEnumerable<Instrument> candidates, DateTime utc, int max = MaxSessionMarkets)
    {
        var active = OpenAt(utc).SelectMany(s => s.Currencies).ToHashSet();
        int Score(Instrument i) => (active.Contains(i.BaseCurrency) ? 1 : 0) + (active.Contains(i.QuoteCurrency) ? 1 : 0);
        return candidates.Where(i => i.IsCurrencyPair && Score(i) > 0)
            .OrderByDescending(Score).ThenBy(i => Rank(i.BaseCurrency) + Rank(i.QuoteCurrency)).ThenBy(i => i.Symbol, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private static int Rank(string currency)
    {
        var index = Array.IndexOf(Liquidity, currency);
        return index < 0 ? Liquidity.Length : index;
    }
}
