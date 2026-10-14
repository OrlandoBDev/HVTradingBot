using System.Text;
using System.Xml.Linq;
using HVTradingBot.Application.News;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.News;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.News;
using HVTradingBot.UnitTests.TestData;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.UnitTests.News;

public class HeadlineSentimentTests
{
    [Fact]
    public void Pair_quote_moves_base_and_quote_currency_opposite_ways()
    {
        var sentiment = HeadlineSentiment.Score("EUR/USD rises as ECB turns hawkish");
        Assert.True(sentiment["EUR"] > 0);
        Assert.True(sentiment["USD"] < 0);
    }

    [Fact]
    public void Currency_after_against_moves_the_other_way()
    {
        var sentiment = HeadlineSentiment.Score("Dollar slumps against the yen");
        Assert.True(sentiment["USD"] < 0);
        Assert.True(sentiment["JPY"] > 0);
    }

    [Fact]
    public void Dovish_central_bank_is_negative_for_its_currency() =>
        Assert.True(HeadlineSentiment.Score("Fed signals rate cut as inflation cools")["USD"] < 0);

    [Fact]
    public void Headline_without_a_known_currency_is_not_tagged() =>
        Assert.Empty(HeadlineSentiment.Score("Oil rallies on supply worries"));

    [Fact]
    public void Words_must_match_whole_words() =>
        // "trust" contains "us" and "cuts" contains "cut"; neither is a currency or a whole-word match for "us ".
        Assert.Equal(0m, HeadlineSentiment.Tone(" trustworthy haircuts "));
}

public class MarketIntelligenceTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NewsOptions Options = new();

    private static MarketIntelligenceInputs Inputs(
        IReadOnlyList<EconomicEvent>? events = null,
        IReadOnlyList<NewsHeadline>? headlines = null,
        IReadOnlyDictionary<string, decimal>? trend = null,
        bool calendar = true,
        NewsOptions? options = null) =>
        new(new NewsSnapshot(events ?? [], headlines ?? [], Now, calendar, "test"), trend ?? new Dictionary<string, decimal>(), options ?? Options);

    private static EconomicEvent Event(string currency, int minutesFromNow, EventImpact impact = EventImpact.High) =>
        new($"{currency}-{minutesFromNow}", currency, $"{currency} release", Now.AddMinutes(minutesFromNow), impact);

    private static NewsHeadline Headline(string currency, decimal sentiment, int minutesAgo = 30) =>
        new(Guid.NewGuid().ToString(), Now.AddMinutes(-minutesAgo), "test", "headline", new Dictionary<string, decimal> { [currency] = sentiment });

    [Fact]
    public void High_impact_release_soon_blocks_the_trade()
    {
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("USD", 10)]));

        Assert.True(view.Blackout);
        Assert.Contains("USD release", view.BlackoutReason);
        Assert.Equal(NewsCondition.EventRisk, view.Condition);
    }

    [Fact]
    public void High_impact_release_that_just_happened_still_blocks_then_clears()
    {
        Assert.True(MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("EUR", -20)])).Blackout);
        Assert.False(MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("EUR", -45)])).Blackout);
    }

    [Fact]
    public void Release_on_an_unrelated_currency_changes_nothing()
    {
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("JPY", 5)]));

        Assert.False(view.Blackout);
        Assert.Equal(1m, view.RiskMultiplier);
        Assert.Equal(NewsCondition.Calm, view.Condition);
    }

    [Fact]
    public void Release_within_the_caution_window_reduces_size_without_blocking()
    {
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("USD", 90, EventImpact.Medium)]));

        Assert.False(view.Blackout);
        Assert.Equal(Options.CautionRiskMultiplier, view.RiskMultiplier);
        Assert.Equal(NewsCondition.EventRisk, view.Condition);
    }

    [Fact]
    public void Derived_markets_ignore_news()
    {
        var derived = new Instrument("R_100", "R_100", "USD", 0.01m, 2) { AssetClass = AssetClass.SyntheticIndex };
        Assert.Same(NewsAssessment.None, MarketIntelligence.Assess(derived, Direction.Long, Now, Inputs([Event("USD", 5)])));
    }

    [Fact]
    public void Disabled_news_changes_nothing()
    {
        var off = new NewsOptions { Enabled = false };
        Assert.Same(NewsAssessment.None, MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("USD", 5)], options: off)));
    }

    [Fact]
    public void Supportive_headlines_give_a_small_capped_boost()
    {
        var headlines = new[] { Headline("EUR", 1m), Headline("EUR", 1m), Headline("USD", -1m) };
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs(headlines: headlines));

        Assert.Equal(NewsCondition.SentimentWith, view.Condition);
        Assert.Equal(1m, view.Sentiment);
        Assert.Equal(Options.MaxSentimentBoost, view.SentimentScore);
        Assert.Equal(1m, view.RiskMultiplier);
    }

    [Fact]
    public void Opposing_headlines_lower_the_score_and_the_size()
    {
        var headlines = new[] { Headline("EUR", 1m), Headline("EUR", 1m), Headline("USD", -1m) };
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Short, Now, Inputs(headlines: headlines));

        Assert.Equal(NewsCondition.SentimentAgainst, view.Condition);
        Assert.Equal(-Options.SentimentPoints, view.SentimentScore);
        Assert.Equal(Options.OpposedSentimentRiskMultiplier, view.RiskMultiplier);
    }

    [Fact]
    public void Too_few_headlines_have_no_effect()
    {
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs(headlines: [Headline("EUR", 1m)]));
        Assert.Equal(0m, view.SentimentScore);
        Assert.Equal(NewsCondition.Calm, view.Condition);
    }

    [Fact]
    public void Headlines_after_the_evaluation_time_are_never_used()
    {
        var future = new[] { Headline("EUR", 1m, -10), Headline("EUR", 1m, -20), Headline("EUR", 1m, -30) };
        var (sentiment, count) = MarketIntelligence.CurrencySentiment(future, "EUR", Now, Options);
        Assert.Equal(0, count);
        Assert.Equal(0m, sentiment);
    }

    [Fact]
    public void Older_headlines_weigh_less()
    {
        var headlines = new[] { Headline("EUR", 1m, minutesAgo: 10), Headline("EUR", -1m, minutesAgo: 12 * 60) };
        var (sentiment, _) = MarketIntelligence.CurrencySentiment(headlines, "EUR", Now, Options);
        Assert.True(sentiment > 0.5m);
    }

    [Fact]
    public void Risk_multipliers_combine_but_never_exceed_one()
    {
        var headlines = new[] { Headline("EUR", -1m), Headline("EUR", -1m), Headline("EUR", -1m) };
        var view = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs([Event("USD", 100, EventImpact.Medium)], headlines));
        Assert.Equal(Options.CautionRiskMultiplier * Options.OpposedSentimentRiskMultiplier, view.RiskMultiplier);
    }

    [Fact]
    public void Missing_calendar_blocks_only_when_configured()
    {
        var strict = new NewsOptions { BlockWhenCalendarUnavailable = true };
        Assert.False(MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs(calendar: false)).Blackout);
        Assert.True(MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs(calendar: false, options: strict)).Blackout);
    }

    [Fact]
    public void Currency_trend_needs_two_pairs_and_ranks_currencies()
    {
        var up = Snapshot(ema20: 1.1020m, ema50: 1.1000m);
        var down = Snapshot(ema20: 1.0980m, ema50: 1.1000m);
        var trend = MarketIntelligence.CurrencyTrend(
        [
            (Instruments.EurUsd, up),
            (Instruments.GbpUsd, up),
            (Instruments.UsdJpy, down)
        ]);

        Assert.True(trend["USD"] < 0);
        Assert.False(trend.ContainsKey("EUR")); // only one pair
        Assert.False(trend.ContainsKey("JPY"));
    }

    [Fact]
    public void Trading_with_the_currency_trend_scores_higher_than_against_it()
    {
        var trend = new Dictionary<string, decimal> { ["EUR"] = 0.8m, ["USD"] = -0.6m };
        var with = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Long, Now, Inputs(trend: trend));
        var against = MarketIntelligence.Assess(Instruments.EurUsd, Direction.Short, Now, Inputs(trend: trend));

        Assert.Equal(TrendAlignment.With, with.Trend);
        Assert.Equal(Options.MaxTrendBoost, with.TrendScore);
        Assert.Equal(TrendAlignment.Against, against.Trend);
        Assert.True(against.TrendScore < 0);
        Assert.Equal(1m, against.RiskMultiplier); // trends adjust scores only, never size
    }

    private static IndicatorSnapshot Snapshot(decimal ema20, decimal ema50) =>
        Bars.Snapshot(TimeFrame.H4, close: 1.1m, ema20: ema20, ema50: ema50);
}

public class NewsRiskTests
{
    private readonly RiskManager _risk = new(new RiskOptions(), new ExecutionCostOptions());

    private Task<RiskDecision> Evaluate(TradeProposal proposal) => _risk.EvaluateAsync(proposal, Bars.Portfolio(), CancellationToken.None);

    [Fact]
    public async Task News_blackout_rejects_the_trade()
    {
        var decision = await Evaluate(Bars.Proposal() with { NewsBlackout = "High-impact USD release in 10m." });

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c is { Rule: "NewsEvents", Passed: false });
    }

    [Fact]
    public async Task News_multiplier_shrinks_the_position()
    {
        var normal = await Evaluate(Bars.Proposal());
        var reduced = await Evaluate(Bars.Proposal() with { NewsRiskMultiplier = 0.5m });

        Assert.True(reduced.IsApproved, reduced.RejectionReason);
        Assert.True(reduced.RiskAmount <= normal.RiskAmount / 2m + 1m);
        Assert.True(reduced.Units < normal.Units);
    }

    [Fact]
    public async Task News_can_never_grow_a_position()
    {
        var normal = await Evaluate(Bars.Proposal());
        var inflated = await Evaluate(Bars.Proposal() with { NewsRiskMultiplier = 3m });
        Assert.Equal(normal.Units, inflated.Units);
    }

    [Fact]
    public async Task News_does_not_bypass_the_kill_switch()
    {
        var decision = await _risk.EvaluateAsync(Bars.Proposal() with { NewsRiskMultiplier = 1m }, Bars.Portfolio(killSwitch: true), CancellationToken.None);
        Assert.False(decision.IsApproved);
    }
}

public class NewsLearningTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
    private static readonly LearningOptions Options = new();

    private static IEnumerable<SetupOutcome> Outcomes(string strategy, NewsCondition news, decimal r, int count) =>
        Enumerable.Range(0, count).Select(i => new SetupOutcome(new SetupKey(strategy, MarketRegime.Ranging, AssetClass.Forex), r, Now.AddHours(-i))
        {
            News = news
        });

    [Fact]
    public void Learns_which_news_conditions_pay_relative_to_the_strategy()
    {
        var outcomes = Outcomes("A", NewsCondition.Calm, 1m, 40).Concat(Outcomes("A", NewsCondition.EventRisk, -1m, 40)).ToList();
        var model = StrategyPerformanceModel.ComputeContext(outcomes, Now, Options);

        var calm = model[ContextKey.For("A", NewsCondition.Calm)];
        var risky = model[ContextKey.For("A", NewsCondition.EventRisk)];
        Assert.Equal(0m, calm.BaselineR);
        Assert.Equal(Options.ContextMaxBoost, calm.ScoreAdjustment);
        Assert.Equal(-Options.ContextMaxPenalty, risky.ScoreAdjustment);
    }

    [Fact]
    public void A_strategy_that_is_equally_bad_everywhere_is_not_penalised_again()
    {
        var outcomes = Outcomes("B", NewsCondition.Calm, -1m, 40).Concat(Outcomes("B", NewsCondition.EventRisk, -1m, 10)).ToList();
        var model = StrategyPerformanceModel.ComputeContext(outcomes, Now, Options);
        Assert.All(model.Values, c => Assert.Equal(0m, c.ScoreAdjustment));
    }

    [Fact]
    public void Few_samples_move_the_score_little()
    {
        var outcomes = Outcomes("C", NewsCondition.Calm, 0m, 200).Concat(Outcomes("C", NewsCondition.SentimentWith, 1m, 2)).ToList();
        var model = StrategyPerformanceModel.ComputeContext(outcomes, Now, Options);
        Assert.InRange(model[ContextKey.For("C", NewsCondition.SentimentWith)].ScoreAdjustment, 0m, 2m);
    }

    [Fact]
    public void Combined_news_and_trend_adjustment_stays_within_bounds()
    {
        var outcomes = Outcomes("D", NewsCondition.Calm, 2m, 100).Concat(Outcomes("D", NewsCondition.EventRisk, -2m, 100))
            .Select((o, i) => o with { Trend = i < 100 ? TrendAlignment.With : TrendAlignment.Against })
            .ToList();
        var model = StrategyPerformanceModel.ComputeContext(outcomes, Now, Options);

        Assert.Equal(Options.ContextMaxBoost, StrategyPerformanceModel.ContextAdjustment(model, "D", NewsCondition.Calm, TrendAlignment.With, Options));
        Assert.Equal(-Options.ContextMaxPenalty, StrategyPerformanceModel.ContextAdjustment(model, "D", NewsCondition.EventRisk, TrendAlignment.Against, Options));
        Assert.Equal(0m, StrategyPerformanceModel.ContextAdjustment(model, "unknown", NewsCondition.Calm, null, Options));
    }
}

public class NewsScoringTests
{
    private static MarketContext Context(MarketIntelligenceInputs? intelligence)
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        foreach (var bar in Bars.Flat(288 * 12, 1.1m))
        {
            series.Add(bar);
        }

        var quote = Bars.Quote(Instruments.EurUsd, 1.1m, at: series.LastBar!.CloseTimeUtc);
        return MarketContext.Build(series, quote, new RegimeOptions(), false) with { Regime = MarketRegime.Ranging, Intelligence = intelligence };
    }

    [Fact]
    public async Task Evaluator_adds_news_and_trend_to_the_score_and_explains_it()
    {
        var plain = Context(null);
        var at = plain.AsOfUtc;
        var headlines = Enumerable.Range(1, 3)
            .Select(i => new NewsHeadline($"h{i}", at.AddMinutes(-i), "test", "Euro rallies", new Dictionary<string, decimal> { ["EUR"] = 1m }))
            .ToList();
        var inputs = new MarketIntelligenceInputs(new NewsSnapshot([], headlines, at, true, "test"),
            new Dictionary<string, decimal> { ["EUR"] = 1m, ["USD"] = -1m }, new NewsOptions());
        var evaluator = new SignalEvaluator([new FixedStrategy()], new ScoringOptions { ObserveThreshold = 0, CandidateThreshold = 0 });

        var without = await evaluator.EvaluateAsync(plain, CancellationToken.None);
        var with = await evaluator.EvaluateAsync(Context(inputs), CancellationToken.None);

        Assert.Equal(0m, without.Best!.Score.NewsSentiment);
        Assert.Equal(3m, with.Best!.Score.NewsSentiment);
        Assert.Equal(3m, with.Best.Score.MarketTrend);
        Assert.Equal(without.Best.Score.Total + 6, with.Best.Score.Total);
        Assert.Contains(with.Reasons, r => r.Contains("News +3"));
        Assert.Equal(NewsCondition.SentimentWith, with.Best.News.Condition);
    }

    private sealed class FixedStrategy : ITradingStrategy
    {
        public string Name => "Fixed";

        public IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; } = Enum.GetValues<MarketRegime>();

        public Task<StrategyResult> EvaluateAsync(MarketContext context, CancellationToken cancellationToken)
        {
            var entry = context.Quote.Mid;
            return Task.FromResult(StrategyResult.Trade(Name, new TradeSetup(Direction.Long, entry, entry - 0.002m, entry + 0.005m), "fixed", CompatibleRegimes));
        }
    }
}

public class NewsSourceTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Simulated_news_is_reproducible()
    {
        var a = await new SimulatedNewsSource(7).FetchAsync(Now, CancellationToken.None);
        var b = await new SimulatedNewsSource(7).FetchAsync(Now, CancellationToken.None);

        Assert.NotEmpty(a.Events);
        Assert.NotEmpty(a.Headlines);
        Assert.Equal(a.Events, b.Events);
        Assert.Equal(a.Headlines.Select(h => (h.Id, h.Title)), b.Headlines.Select(h => (h.Id, h.Title)));
        Assert.All(a.Headlines, h => Assert.True(h.PublishedUtc <= Now));
    }

    [Fact]
    public async Task Forex_factory_calendar_is_parsed_to_utc()
    {
        const string json = """
            [
              {"title":"Non-Farm Employment Change","country":"USD","date":"2026-09-25T08:30:00-04:00","impact":"High","forecast":"150K","previous":"142K"},
              {"title":"German Ifo Business Climate","country":"EUR","date":"2026-09-24T04:00:00-04:00","impact":"Medium"},
              {"title":"Bank Holiday","country":"JPY","date":"2026-09-23T00:00:00-04:00","impact":"Holiday"}
            ]
            """;
        var events = await PublicNewsSource.ParseCalendarAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), CancellationToken.None);

        Assert.Equal(3, events.Count);
        var nfp = events.Single(e => e.Currency == "USD");
        Assert.Equal(new DateTime(2026, 9, 25, 12, 30, 0, DateTimeKind.Utc), nfp.TimeUtc);
        Assert.Equal(EventImpact.High, nfp.Impact);
        Assert.Equal(EventImpact.Low, events.Single(e => e.Currency == "JPY").Impact);
    }

    [Fact]
    public void Rss_and_atom_feeds_are_parsed()
    {
        var rss = XDocument.Parse("""
            <rss version="2.0"><channel>
              <item><title>EUR/USD rises as ECB turns hawkish</title><pubDate>Thu, 24 Sep 2026 11:00:00 GMT</pubDate><guid>1</guid></item>
              <item><title>No date here</title></item>
            </channel></rss>
            """);
        var atom = XDocument.Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><title>Yen slides as BoJ stays dovish</title><updated>2026-09-24T10:30:00Z</updated><id>a</id></entry>
            </feed>
            """);

        var items = PublicNewsSource.ParseFeed(rss, "rss").Concat(PublicNewsSource.ParseFeed(atom, "atom")).ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(new DateTime(2026, 9, 24, 11, 0, 0, DateTimeKind.Utc), items[0].PublishedUtc);
        Assert.True(items[0].Sentiment["EUR"] > 0);
        Assert.True(items[1].Sentiment["JPY"] < 0);
    }

    [Fact]
    public async Task Failed_refresh_keeps_the_last_calendar_and_never_throws()
    {
        var source = new FlakySource();
        var clock = new ManualTime(Now);
        var service = new NewsService(source, new NewsOptions { RefreshMinutes = 15 }, NullLogger<NewsService>.Instance, clock);

        var first = await service.RefreshIfDueAsync(Now, CancellationToken.None);
        source.Fail = true;
        clock.Now = Now.AddMinutes(15);
        var afterFailure = await service.RefreshIfDueAsync(Now.AddMinutes(15), CancellationToken.None);
        clock.Now = Now.AddMinutes(15.5);
        await service.RefreshIfDueAsync(Now.AddMinutes(15.5), CancellationToken.None); // within the retry delay: no call
        clock.Now = Now.AddMinutes(16);
        await service.RefreshIfDueAsync(Now.AddMinutes(16), CancellationToken.None); // retried after a minute

        Assert.Single(first.Events);
        Assert.Equal(3, source.Calls);
        Assert.Single(afterFailure.Events);
        Assert.True(afterFailure.CalendarAvailable);
    }

    [Fact]
    public async Task Real_sources_are_not_refetched_as_fast_market_time_passes()
    {
        var source = new FlakySource();
        var service = new NewsService(source, new NewsOptions { RefreshMinutes = 15 }, NullLogger<NewsService>.Instance, new ManualTime(Now));

        for (var i = 0; i < 10; i++)
        {
            await service.RefreshIfDueAsync(Now.AddHours(i), CancellationToken.None);
        }

        Assert.Equal(1, source.Calls);
    }

    private sealed class ManualTime(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FlakySource : INewsSource
    {
        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public string Name => "flaky";

        public Task<NewsSnapshot> FetchAsync(DateTime nowUtc, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new HttpRequestException("down");
            }

            return Task.FromResult(new NewsSnapshot([new EconomicEvent("1", "USD", "CPI", nowUtc.AddHours(1), EventImpact.High)], [], nowUtc, true, Name));
        }
    }
}
