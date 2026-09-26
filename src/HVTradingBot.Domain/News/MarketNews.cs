using System.ComponentModel.DataAnnotations;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.News;

public enum EventImpact
{
    Low,
    Medium,
    High
}

/// <summary>A scheduled economic release (economic calendar), e.g. US Non-Farm Payrolls. Times are UTC.</summary>
public sealed record EconomicEvent(string Id, string Currency, string Title, DateTime TimeUtc, EventImpact Impact);

/// <summary>
/// A news headline tagged with the currencies it concerns. <see cref="Sentiment"/> is in [-1, 1] per currency tag:
/// positive means the news is supportive for that currency (see <see cref="HeadlineSentiment"/>).
/// </summary>
public sealed record NewsHeadline(string Id, DateTime PublishedUtc, string Source, string Title, IReadOnlyDictionary<string, decimal> Sentiment);

/// <summary>Everything the news providers returned at one point in time.</summary>
public sealed record NewsSnapshot(
    IReadOnlyList<EconomicEvent> Events,
    IReadOnlyList<NewsHeadline> Headlines,
    DateTime? UpdatedUtc,
    bool CalendarAvailable,
    string Source)
{
    public static NewsSnapshot Empty { get; } = new([], [], null, false, "none");
}

public enum NewsProvider
{
    /// <summary>No news: nothing changes scores or risk (backtests always behave like this).</summary>
    None,

    /// <summary>Deterministic generated calendar and headlines for offline development and tests.</summary>
    Simulated,

    /// <summary>Free, keyless public sources: the Forex Factory weekly calendar export and RSS headline feeds.</summary>
    Public
}

/// <summary>
/// How market news and cross-market trends influence decisions. They can only make the engine more careful: lower
/// scores, smaller positions or a blocked trade near high-impact events. They never raise a risk limit or bypass the
/// kill switch, and the boosts they can add to a score are small and bounded.
/// </summary>
public sealed class NewsOptions
{
    public const string SectionName = "News";

    public bool Enabled { get; set; } = true;

    public NewsProvider Provider { get; set; } = NewsProvider.Public;

    /// <summary>How often the calendar and headlines are fetched.</summary>
    [Range(1, 1440)] public int RefreshMinutes { get; set; } = 15;

    /// <summary>No new trade on a market whose currency has a high-impact release within this many minutes ahead...</summary>
    [Range(0, 720)] public int BlackoutMinutesBefore { get; set; } = 30;

    /// <summary>...or this many minutes after it.</summary>
    [Range(0, 720)] public int BlackoutMinutesAfter { get; set; } = 30;

    /// <summary>A medium- or high-impact release within this window (outside the blackout) reduces position size.</summary>
    [Range(0, 1440)] public int CautionMinutes { get; set; } = 120;

    /// <summary>Position size multiplier while a release is within <see cref="CautionMinutes"/>.</summary>
    [Range(0.1, 1)] public decimal CautionRiskMultiplier { get; set; } = 0.5m;

    /// <summary>Position size multiplier when news sentiment clearly opposes the trade direction.</summary>
    [Range(0.1, 1)] public decimal OpposedSentimentRiskMultiplier { get; set; } = 0.75m;

    /// <summary>Net sentiment (in [-1, 1]) beyond which it counts as clearly for or against a trade.</summary>
    [Range(0.05, 1)] public decimal SentimentThreshold { get; set; } = 0.25m;

    /// <summary>Headlines older than this are ignored; newer ones are weighted by <see cref="SentimentHalfLifeHours"/>.</summary>
    [Range(1, 168)] public int SentimentLookbackHours { get; set; } = 24;

    [Range(0.5, 72)] public decimal SentimentHalfLifeHours { get; set; } = 6m;

    /// <summary>Headlines needed on a market before its sentiment affects anything.</summary>
    [Range(1, 50)] public int MinHeadlines { get; set; } = 3;

    /// <summary>Score points for news sentiment at full strength (±1). The boost is capped at <see cref="MaxSentimentBoost"/>.</summary>
    [Range(0, 20)] public decimal SentimentPoints { get; set; } = 6m;

    [Range(0, 10)] public decimal MaxSentimentBoost { get; set; } = 3m;

    /// <summary>Score points for trading with (+) or against (−) the cross-market currency trend at full strength.</summary>
    [Range(0, 20)] public decimal TrendPoints { get; set; } = 6m;

    [Range(0, 10)] public decimal MaxTrendBoost { get; set; } = 3m;

    /// <summary>When true, no trades are allowed on news-sensitive markets while the economic calendar cannot be read.</summary>
    public bool BlockWhenCalendarUnavailable { get; set; }

    /// <summary>Economic calendar (JSON, Forex Factory export format).</summary>
    public string CalendarUrl { get; set; } = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";

    /// <summary>RSS or Atom feeds for market headlines.</summary>
    public List<string> HeadlineFeeds { get; set; } =
    [
        "https://www.fxstreet.com/rss/news",
        "https://www.investing.com/rss/news_1.rss"
    ];

    public NewsOptions Clone()
    {
        var clone = (NewsOptions)MemberwiseClone();
        clone.HeadlineFeeds = [.. HeadlineFeeds];
        return clone;
    }
}

/// <summary>News and cross-market trend inputs for one evaluation, attached to the <see cref="Analysis.MarketContext"/>.</summary>
public sealed record MarketIntelligenceInputs(
    NewsSnapshot News,
    IReadOnlyDictionary<string, decimal> CurrencyTrend,
    NewsOptions Options);

/// <summary>What the learning model distinguishes about the news around a setup.</summary>
public enum NewsCondition
{
    /// <summary>News data available, nothing notable.</summary>
    Calm,

    /// <summary>A medium- or high-impact release is near.</summary>
    EventRisk,

    /// <summary>Recent headlines favour the trade direction.</summary>
    SentimentWith,

    /// <summary>Recent headlines oppose the trade direction.</summary>
    SentimentAgainst
}

/// <summary>How a setup relates to the cross-market currency trend.</summary>
public enum TrendAlignment
{
    Neutral,
    With,
    Against
}

/// <summary>
/// The news and trend view of one setup. <see cref="RiskMultiplier"/> is at most 1 (it can only shrink a position);
/// <see cref="BlackoutReason"/> blocks the trade in the risk engine.
/// </summary>
public sealed record NewsAssessment(
    NewsCondition? Condition,
    TrendAlignment? Trend,
    decimal Sentiment,
    decimal TrendStrength,
    decimal SentimentScore,
    decimal TrendScore,
    decimal RiskMultiplier,
    string? BlackoutReason,
    IReadOnlyList<EconomicEvent> NearbyEvents,
    IReadOnlyList<string> Notes)
{
    public bool Blackout => BlackoutReason is not null;

    public static NewsAssessment None { get; } = new(null, null, 0, 0, 0, 0, 1m, null, [], []);
}

public static class NewsCurrencies
{
    /// <summary>Currencies the news sources cover (calendar and headline tags).</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "JPY", "AUD", "NZD", "CAD", "CHF", "CNY"
    };

    /// <summary>
    /// Currencies whose news moves <paramref name="instrument"/>: both sides of a currency pair; the pricing currency
    /// of metals, crypto and stock indices; none for Derived (synthetic) markets, which real-world news does not move.
    /// </summary>
    public static IReadOnlyList<string> For(Instrument instrument) => instrument.AssetClass switch
    {
        AssetClass.SyntheticIndex => [],
        AssetClass.Forex => new[] { instrument.BaseCurrency, instrument.QuoteCurrency }.Where(Known.Contains).Distinct().ToList(),
        _ => new[] { instrument.QuoteCurrency }.Where(Known.Contains).ToList()
    };
}
