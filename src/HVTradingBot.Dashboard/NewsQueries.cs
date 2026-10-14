using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.News;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.News;

namespace HVTradingBot.Dashboard;

/// <summary>
/// The News page: the economic calendar, recent headlines with their sentiment, and what the news means for each
/// selected market right now. Uses the same <see cref="MarketIntelligence"/> rules as the trading engine.
/// </summary>
public sealed class NewsQueries(NewsService news, DashboardQueries queries, ITradingStateStore state, IClock clock)
{
    public async Task<object> GetNewsAsync(CancellationToken ct)
    {
        var options = news.Options;
        // The simulated news follows the simulated market's (accelerated) clock; real news follows the real one.
        var asOf = options.Provider == NewsProvider.Simulated ? (await state.GetAsync(ct)).LastBarTimeUtc ?? clock.UtcNow : clock.UtcNow;
        var snapshot = await news.RefreshIfDueAsync(asOf, ct);
        var inputs = new MarketIntelligenceInputs(snapshot, new Dictionary<string, decimal>(), options);

        var markets = new List<object>();
        foreach (var market in await queries.GetMarketsAsync(ct))
        {
            if (!Instruments.TryGet(market.Instrument, out var instrument))
            {
                continue;
            }

            var currencies = NewsCurrencies.For(instrument);
            var view = MarketIntelligence.Assess(instrument, Direction.Long, asOf, inputs);
            var next = snapshot.Events
                .Where(e => e.Impact >= EventImpact.Medium && e.TimeUtc >= asOf && currencies.Contains(e.Currency, StringComparer.OrdinalIgnoreCase))
                .MinBy(e => e.TimeUtc);
            markets.Add(new
            {
                market.Instrument,
                market.DisplayName,
                Currencies = currencies,
                Status = !news.IsActive || currencies.Count == 0 ? "unaffected"
                    : view.Blackout ? "blocked"
                    : view.RiskMultiplier < 1 ? "reduced"
                    : "clear",
                view.RiskMultiplier,
                // Sentiment for buying the market: positive favours longs, negative favours shorts.
                LongSentiment = view.Condition is null ? (decimal?)null : view.Sentiment,
                Note = view.Notes.FirstOrDefault(),
                NextEvent = next
            });
        }

        var since = asOf.AddHours(-options.SentimentLookbackHours);
        return new
        {
            options.Enabled,
            Provider = options.Provider.ToString(),
            Active = news.IsActive,
            snapshot.Source,
            snapshot.UpdatedUtc,
            snapshot.CalendarAvailable,
            AsOfUtc = asOf,
            Rules = new
            {
                options.BlackoutMinutesBefore,
                options.BlackoutMinutesAfter,
                options.CautionMinutes,
                options.CautionRiskMultiplier,
                options.OpposedSentimentRiskMultiplier,
                options.MaxSentimentBoost,
                options.MaxTrendBoost,
                MaxSentimentPenalty = options.SentimentPoints,
                MaxTrendPenalty = options.TrendPoints
            },
            Markets = markets,
            Events = snapshot.Events
                .Where(e => e.Impact >= EventImpact.Medium && e.TimeUtc >= asOf.AddHours(-2) && e.TimeUtc <= asOf.AddHours(36))
                .OrderBy(e => e.TimeUtc)
                .Take(40)
                .Select(e => new { e.Currency, e.Title, e.TimeUtc, Impact = e.Impact.ToString() }),
            Currencies = NewsCurrencies.Known
                .Select(c => (Currency: c, Result: MarketIntelligence.CurrencySentiment(snapshot.Headlines, c, asOf, options)))
                .Where(x => x.Result.Count > 0)
                .OrderByDescending(x => x.Result.Sentiment)
                .Select(x => new { x.Currency, x.Result.Sentiment, Headlines = x.Result.Count }),
            Headlines = snapshot.Headlines
                .Where(h => h.PublishedUtc <= asOf && h.PublishedUtc >= since)
                .OrderByDescending(h => h.PublishedUtc)
                .Take(40)
                .Select(h => new { h.PublishedUtc, h.Source, h.Title, h.Sentiment })
        };
    }
}
