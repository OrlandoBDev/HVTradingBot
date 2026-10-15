using Ospreya.Domain.Analysis;
using Ospreya.Domain.Common;
using Ospreya.Domain.Execution;
using Ospreya.Domain.MarketData;
using Ospreya.UnitTests.TestData;

namespace Ospreya.UnitTests.Execution;

public class EarlyExitRulesTests
{
    // A buy at 1.1000 with its stop at 1.0980: 1R = 20 pips.
    private static readonly OpenPosition Buy = Bars.Position(Instruments.EurUsd, Direction.Long, entry: 1.1m, stop: 1.098m, target: 1.105m);
    private static readonly OpenPosition Sell = Bars.Position(Instruments.EurUsd, Direction.Short, entry: 1.1m, stop: 1.102m, target: 1.095m);
    private static readonly DateTime SoonAfter = Bars.Start.AddHours(2);

    /// <summary>1H momentum pointing down: EMA9 (the close) below EMA20, MACD histogram negative.</summary>
    private static readonly IndicatorSnapshot Falling = Bars.Snapshot(close: 1.0985m, ema20: 1.0995m) with { MacdHistogramRecent = [-0.0001m] };

    private static readonly IndicatorSnapshot Rising = Bars.Snapshot();

    [Fact]
    public void A_trade_in_profit_is_never_closed_early()
    {
        Assert.Null(EarlyExitRules.Check(Buy, 1.1005m, MarketRegime.TrendingBearish, Falling, Bars.Start.AddDays(3)));
    }

    [Fact]
    public void A_losing_trade_closes_when_the_trend_turns_against_it()
    {
        var why = EarlyExitRules.Check(Buy, 1.0995m, MarketRegime.TrendingBearish, Rising, SoonAfter);

        Assert.Contains("trend turned against", why);
        Assert.Contains("-0.25R", why);
        Assert.NotNull(EarlyExitRules.Check(Sell, 1.1005m, MarketRegime.TrendingBullish, Rising, SoonAfter)); // the mirror for a sell
        Assert.Null(EarlyExitRules.Check(Buy, 1.0995m, MarketRegime.Ranging, Rising, SoonAfter));
    }

    [Fact]
    public void Momentum_against_the_trade_closes_it_only_halfway_to_the_stop()
    {
        Assert.Null(EarlyExitRules.Check(Buy, 1.0995m, MarketRegime.Ranging, Falling, SoonAfter)); // -0.25R: give it room
        Assert.Contains("momentum turned against", EarlyExitRules.Check(Buy, 1.0988m, MarketRegime.Ranging, Falling, SoonAfter)); // -0.6R
        Assert.Null(EarlyExitRules.Check(Buy, 1.0988m, MarketRegime.Ranging, Rising, SoonAfter)); // momentum still with it
    }

    [Fact]
    public void A_trade_still_losing_after_a_day_without_progress_is_closed()
    {
        var dayLater = Bars.Start.AddHours(25);

        Assert.Contains("No progress after 25 h", EarlyExitRules.Check(Buy, 1.0998m, MarketRegime.Ranging, Rising, dayLater));
        // It once reached +0.6R: it was working, so it keeps its stop.
        Assert.Null(EarlyExitRules.Check(Buy with { MaxFavorableExcursion = 0.0012m }, 1.0998m, MarketRegime.Ranging, Rising, dayLater));
    }
}
