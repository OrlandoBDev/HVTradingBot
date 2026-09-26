using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using HVTradingBot.Application.News;
using HVTradingBot.Domain.News;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.News;

/// <summary>
/// Free, keyless public news: the weekly economic calendar published by Forex Factory (JSON export) and market
/// headlines from RSS/Atom feeds (News:HeadlineFeeds). The calendar is cached for an hour because release times rarely
/// change and the export asks callers not to poll it often. A feed that fails is skipped; the snapshot says whether
/// the calendar could be read, and the source throws only when nothing at all could be read.
/// </summary>
public sealed class PublicNewsSource(HttpClient http, NewsOptions options, ILogger<PublicNewsSource> logger) : INewsSource
{
    private static readonly TimeSpan CalendarCacheFor = TimeSpan.FromHours(1);

    private IReadOnlyList<EconomicEvent>? _calendar;
    private DateTime _calendarFetchedUtc;

    public string Name => "public";

    public async Task<NewsSnapshot> FetchAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var calendarAvailable = true;
        if (_calendar is null || DateTime.UtcNow - _calendarFetchedUtc >= CalendarCacheFor)
        {
            try
            {
                await using var stream = await http.GetStreamAsync(options.CalendarUrl, cancellationToken);
                _calendar = await ParseCalendarAsync(stream, cancellationToken);
                _calendarFetchedUtc = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Economic calendar {Url} could not be read", options.CalendarUrl);
                calendarAvailable = _calendar is not null && DateTime.UtcNow - _calendarFetchedUtc < TimeSpan.FromDays(1);
            }
        }

        var headlines = new List<NewsHeadline>();
        var feedsRead = 0;
        foreach (var feed in options.HeadlineFeeds.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            try
            {
                await using var stream = await http.GetStreamAsync(feed, cancellationToken);
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
                headlines.AddRange(ParseFeed(document, new Uri(feed).Host));
                feedsRead++;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or UriFormatException
                                           or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Headline feed {Feed} could not be read", feed);
            }
        }

        if (!calendarAvailable && feedsRead == 0)
        {
            throw new HttpRequestException("Neither the economic calendar nor any headline feed could be read.");
        }

        var recent = headlines
            .Where(h => h.PublishedUtc <= nowUtc && h.PublishedUtc >= nowUtc.AddHours(-options.SentimentLookbackHours) && h.Sentiment.Count > 0)
            .DistinctBy(h => h.Title.Trim().ToLowerInvariant())
            .OrderBy(h => h.PublishedUtc)
            .ToList();
        return new NewsSnapshot(calendarAvailable ? _calendar ?? [] : [], recent, nowUtc, calendarAvailable, Name);
    }

    /// <summary>Parses the Forex Factory calendar export: <c>[{"title","country","date","impact"}]</c>, dates with offsets.</summary>
    public static async Task<IReadOnlyList<EconomicEvent>> ParseCalendarAsync(Stream json, CancellationToken cancellationToken)
    {
        var rows = await JsonSerializer.DeserializeAsync<List<CalendarRow>>(json, JsonOptions, cancellationToken) ?? [];
        var events = new List<EconomicEvent>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Title) || string.IsNullOrWhiteSpace(row.Country)
                || !DateTimeOffset.TryParse(row.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var impact = row.Impact?.Trim().ToLowerInvariant() switch
            {
                "high" => EventImpact.High,
                "medium" => EventImpact.Medium,
                _ => EventImpact.Low
            };
            var time = date.UtcDateTime;
            var currency = row.Country.Trim().ToUpperInvariant();
            events.Add(new EconomicEvent($"{currency}-{time:yyyyMMddHHmm}-{row.Title.Trim()}", currency, row.Title.Trim(), time, impact));
        }

        return events.DistinctBy(e => e.Id).OrderBy(e => e.TimeUtc).ToList();
    }

    /// <summary>Headlines from an RSS 2.0 (<c>item</c>) or Atom (<c>entry</c>) feed, scored with <see cref="HeadlineSentiment"/>.</summary>
    public static IEnumerable<NewsHeadline> ParseFeed(XDocument document, string source)
    {
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            var title = Child(item, "title")?.Trim();
            var published = Child(item, "pubDate") ?? Child(item, "published") ?? Child(item, "updated") ?? Child(item, "date");
            if (string.IsNullOrWhiteSpace(title) || published is null
                || !DateTimeOffset.TryParse(published.Replace(" GMT", " +0000").Replace(" UTC", " +0000"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var time))
            {
                continue;
            }

            var id = Child(item, "guid") ?? Child(item, "id") ?? Child(item, "link") ?? title;
            yield return new NewsHeadline(id, time.UtcDateTime, source, title, HeadlineSentiment.Score(title));
        }
    }

    private static string? Child(XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CalendarRow(string? Title, string? Country, string? Date, string? Impact);
}
