using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Analysis;

public class MarketStructureAndRegimeTests
{
    [Fact]
    public void Zigzag_uptrend_has_higher_highs_and_higher_lows()
    {
        var closes = new[] { 1.100m, 1.104m, 1.108m, 1.105m, 1.102m, 1.106m, 1.110m, 1.114m, 1.111m, 1.108m, 1.112m, 1.116m, 1.120m, 1.117m, 1.114m, 1.118m };
        var bars = closes.Select((c, i) => Bars.Bar(Bars.Start.AddHours(i), c, TimeFrame.H1, 0.001m)).ToList();

        var s = MarketStructureAnalyzer.Analyze(bars, 0.002m);

        Assert.True(s.HigherHighs);
        Assert.True(s.HigherLows);
        Assert.Equal(StructureTrend.Up, s.Trend);
    }

    [Fact]
    public void Swings_require_confirmation_bars_after_the_pivot()
    {
        var closes = new[] { 1.10m, 1.11m, 1.12m, 1.13m, 1.12m };
        var bars = closes.Select((c, i) => Bars.Bar(Bars.Start.AddHours(i), c, TimeFrame.H1)).ToList();
        Assert.DoesNotContain(MarketStructureAnalyzer.FindSwings(bars), s => s.IsHigh && s.Index == 3);
    }

    [Theory]
    [InlineData(30, 1.099, 1.098, 1.1, MarketRegime.TrendingBullish)]
    [InlineData(30, 1.097, 1.098, 1.09, MarketRegime.TrendingBearish)]
    [InlineData(15, 1.099, 1.098, 1.1, MarketRegime.Ranging)]
    [InlineData(21, 1.099, 1.098, 1.0975, MarketRegime.Uncertain)]
    public void Regime_classification(double adx, double ema20, double ema50, double close, MarketRegime expected)
    {
        var structural = Bars.Snapshot(TimeFrame.H4, (decimal)close, (decimal)adx, (decimal)ema20, (decimal)ema50);
        var primary = Bars.Snapshot(atrRank: 0.5m);
        Assert.Equal(expected, RegimeClassifier.Classify(structural, primary, new RegimeOptions()));
    }

    [Fact]
    public void Extreme_volatility_overrides_trend()
    {
        var structural = Bars.Snapshot(TimeFrame.H4, adx: 40);
        var primary = Bars.Snapshot(atrRank: 0.99m);
        Assert.Equal(MarketRegime.ExtremeVolatility, RegimeClassifier.Classify(structural, primary, new RegimeOptions()));
    }
}
