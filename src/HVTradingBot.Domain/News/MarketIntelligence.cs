using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.News;

/// <summary>
/// Turns news, the economic calendar and the cross-market currency trend into a bounded, auditable view of one setup.
/// Pure and deterministic: the same inputs always give the same assessment, and only headlines published at or before
/// the evaluation time are used (no look-ahead). Scheduled releases are known in advance, so future events are fair.
/// </summary>
public static class MarketIntelligence
{
    /// <summary>A currency needs this many pairs in the universe before its cross-market trend is used.</summary>
    public const int MinPairsForCurrencyTrend = 2;

    /// <summary>Directional trend beyond this counts as with or against the trade.</summary>
    public const decimal TrendAlignmentThreshold = 0.25m;

    public static NewsAssessment Assess(Instrument instrument, Direction direction, DateTime asOfUtc, MarketIntelligenceInputs inputs)
    {
        var options = inputs.Options;
        var currencies = NewsCurrencies.For(instrument);
        if (!options.Enabled || currencies.Count == 0)
        {
            return NewsAssessment.None;
        }

        var sign = direction.Sign();
        var notes = new List<string>();
        var news = inputs.News;
        var fetched = news.UpdatedUtc is not null;
        var hasNews = fetched && (news.CalendarAvailable || news.Headlines.Count > 0);

        // Economic calendar: releases on this market's currencies around now.
        var windowStart = asOfUtc.AddMinutes(-options.BlackoutMinutesAfter);
        var nearby = news.Events
            .Where(e => e.Impact >= EventImpact.Medium && currencies.Contains(e.Currency, StringComparer.OrdinalIgnoreCase)
                        && e.TimeUtc >= windowStart && e.TimeUtc <= asOfUtc.AddMinutes(options.CautionMinutes))
            .OrderBy(e => e.TimeUtc)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        var blackoutEvent = nearby.FirstOrDefault(e => e.Impact == EventImpact.High
                                                       && e.TimeUtc >= windowStart && e.TimeUtc <= asOfUtc.AddMinutes(options.BlackoutMinutesBefore));
        string? blackoutReason = null;
        decimal riskMultiplier = 1m;
        if (blackoutEvent is not null)
        {
            blackoutReason = $"High-impact {blackoutEvent.Currency} release {Describe(blackoutEvent, asOfUtc)}: no new trades from {options.BlackoutMinutesBefore}m before to {options.BlackoutMinutesAfter}m after.";
            notes.Add(blackoutReason);
        }
        else if (nearby.Count > 0)
        {
            riskMultiplier *= options.CautionRiskMultiplier;
            var first = nearby[0];
            notes.Add($"{first.Impact}-impact {first.Currency} release {Describe(first, asOfUtc)}: size × {options.CautionRiskMultiplier:0.##}.");
        }

        if (!news.CalendarAvailable && fetched && options.BlockWhenCalendarUnavailable)
        {
            blackoutReason ??= "Economic calendar unavailable; trading paused on news-sensitive markets (News:BlockWhenCalendarUnavailable).";
            notes.Add(blackoutReason);
        }
        else if (!news.CalendarAvailable && fetched)
        {
            notes.Add("Economic calendar unavailable; release times unknown.");
        }

        // Headline sentiment and the cross-market trend apply to currency pairs, where each side is a currency.
        decimal sentiment = 0, trend = 0, sentimentScore = 0, trendScore = 0;
        var sentimentKnown = false;
        TrendAlignment? alignment = null;
        if (instrument.IsCurrencyPair)
        {
            var (baseSentiment, baseCount) = CurrencySentiment(news.Headlines, instrument.BaseCurrency, asOfUtc, options);
            var (quoteSentiment, quoteCount) = CurrencySentiment(news.Headlines, instrument.QuoteCurrency, asOfUtc, options);
            if (baseCount + quoteCount >= options.MinHeadlines)
            {
                sentimentKnown = true;
                sentiment = Math.Round(Math.Clamp(sign * (baseSentiment - quoteSentiment), -1m, 1m), 3);
                sentimentScore = Math.Round(Math.Clamp(sentiment * options.SentimentPoints, -options.SentimentPoints, options.MaxSentimentBoost), 1);
                if (sentiment <= -options.SentimentThreshold)
                {
                    riskMultiplier *= options.OpposedSentimentRiskMultiplier;
                    notes.Add($"News sentiment against the trade ({sentiment:+0.00;-0.00}, {baseCount + quoteCount} headlines): size × {options.OpposedSentimentRiskMultiplier:0.##}.");
                }
                else if (sentiment >= options.SentimentThreshold)
                {
                    notes.Add($"News sentiment supports the trade ({sentiment:+0.00;-0.00}, {baseCount + quoteCount} headlines).");
                }
            }

            if (inputs.CurrencyTrend.TryGetValue(instrument.BaseCurrency, out var baseTrend)
                && inputs.CurrencyTrend.TryGetValue(instrument.QuoteCurrency, out var quoteTrend))
            {
                trend = Math.Round(Math.Clamp(sign * (baseTrend - quoteTrend) / 2m, -1m, 1m), 3);
                trendScore = Math.Round(Math.Clamp(trend * options.TrendPoints, -options.TrendPoints, options.MaxTrendBoost), 1);
                alignment = trend >= TrendAlignmentThreshold ? TrendAlignment.With
                    : trend <= -TrendAlignmentThreshold ? TrendAlignment.Against
                    : TrendAlignment.Neutral;
                if (alignment != TrendAlignment.Neutral)
                {
                    notes.Add($"Cross-market trend {(alignment == TrendAlignment.With ? "supports" : "opposes")} the trade: {instrument.BaseCurrency} {baseTrend:+0.00;-0.00} vs {instrument.QuoteCurrency} {quoteTrend:+0.00;-0.00}.");
                }
            }
        }

        NewsCondition? condition = !hasNews ? null
            : nearby.Count > 0 ? NewsCondition.EventRisk
            : sentimentKnown && sentiment >= options.SentimentThreshold ? NewsCondition.SentimentWith
            : sentimentKnown && sentiment <= -options.SentimentThreshold ? NewsCondition.SentimentAgainst
            : NewsCondition.Calm;

        return new NewsAssessment(condition, alignment, sentiment, trend, sentimentScore, trendScore,
            Math.Clamp(Math.Round(riskMultiplier, 4), 0.1m, 1m), blackoutReason, nearby, notes);
    }

    /// <summary>
    /// Time-weighted average sentiment of the headlines tagged with <paramref name="currency"/>, published within the
    /// lookback and not after <paramref name="asOfUtc"/>. Each headline's weight halves every half-life.
    /// </summary>
    public static (decimal Sentiment, int Count) CurrencySentiment(IReadOnlyList<NewsHeadline> headlines, string currency, DateTime asOfUtc, NewsOptions options)
    {
        var since = asOfUtc.AddHours(-options.SentimentLookbackHours);
        decimal weighted = 0, weights = 0;
        var count = 0;
        foreach (var headline in headlines)
        {
            if (headline.PublishedUtc > asOfUtc || headline.PublishedUtc < since || !headline.Sentiment.TryGetValue(currency, out var value))
            {
                continue;
            }

            var ageHours = (decimal)(asOfUtc - headline.PublishedUtc).TotalHours;
            var weight = (decimal)Math.Pow(0.5, (double)(ageHours / options.SentimentHalfLifeHours));
            weighted += weight * value;
            weights += weight;
            count++;
        }

        return (weights == 0 ? 0 : Math.Round(weighted / weights, 3), count);
    }

    /// <summary>
    /// Cross-market currency trend in [-1, 1] from the 4H trend of every currency pair: each pair's EMA20−EMA50 gap in
    /// ATRs (capped at ±2) counts for its base currency and against its quote currency. A currency rising against most
    /// others is strong. Only currencies in at least <see cref="MinPairsForCurrencyTrend"/> pairs are returned, since a
    /// single pair's trend is already part of the setup score.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> CurrencyTrend(IEnumerable<(Instrument Instrument, IndicatorSnapshot? Structural)> markets)
    {
        var sums = new Dictionary<string, (decimal Sum, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instrument, snapshot) in markets)
        {
            if (!instrument.IsCurrencyPair || snapshot is not { Ema20: { } ema20, Ema50: { } ema50, Atr: { } atr } || atr <= 0)
            {
                continue;
            }

            var pairTrend = Math.Clamp((ema20 - ema50) / atr, -2m, 2m) / 2m;
            Add(instrument.BaseCurrency, pairTrend);
            Add(instrument.QuoteCurrency, -pairTrend);
        }

        return sums
            .Where(kv => kv.Value.Count >= MinPairsForCurrencyTrend)
            .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value.Sum / kv.Value.Count, 3), StringComparer.OrdinalIgnoreCase);

        void Add(string currency, decimal value)
        {
            var current = sums.GetValueOrDefault(currency);
            sums[currency] = (current.Sum + value, current.Count + 1);
        }
    }

    private static string Describe(EconomicEvent e, DateTime asOfUtc)
    {
        var minutes = (int)Math.Round((e.TimeUtc - asOfUtc).TotalMinutes);
        var when = minutes >= 0 ? $"in {minutes}m" : $"{-minutes}m ago";
        return $"\"{e.Title}\" {when} ({e.TimeUtc:HH:mm} UTC)";
    }
}
