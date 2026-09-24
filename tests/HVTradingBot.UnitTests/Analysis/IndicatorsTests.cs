using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Analysis;

public class IndicatorsTests
{
    [Fact]
    public void Sma_matches_hand_calculation_and_is_null_during_warmup()
    {
        var sma = Indicators.Sma([1m, 2m, 3m, 4m, 5m], 3);
        Assert.Equal([null, null, 2m, 3m, 4m], sma);
    }

    [Fact]
    public void Ema_is_seeded_with_sma_then_smooths()
    {
        var ema = Indicators.Ema([1m, 2m, 3m, 4m], 3);
        Assert.Null(ema[1]);
        Assert.Equal(2m, ema[2]);
        Assert.Equal(3m, ema[3]); // (4 - 2) * 0.5 + 2
    }

    [Fact]
    public void Rsi_is_100_for_only_gains_and_50_for_flat_prices()
    {
        var rising = Enumerable.Range(1, 30).Select(i => (decimal)i).ToArray();
        Assert.Equal(100m, Indicators.Rsi(rising)[^1]);

        var flat = Enumerable.Repeat(1.1m, 30).ToArray();
        Assert.Equal(50m, Indicators.Rsi(flat)[^1]);
    }

    [Fact]
    public void Atr_equals_constant_bar_range_on_flat_prices()
    {
        var bars = Bars.Flat(40, 1.1m);
        var atr = Indicators.Atr(bars);
        Assert.Null(atr[13]);
        Assert.Equal(0.0010m, atr[^1]);
    }

    [Fact]
    public void Bollinger_bands_collapse_on_flat_prices()
    {
        var bb = Indicators.Bollinger(Enumerable.Repeat(1.2m, 25).ToArray());
        Assert.Equal(1.2m, bb.Upper[^1]);
        Assert.Equal(1.2m, bb.Lower[^1]);
    }

    [Fact]
    public void Adx_is_high_for_a_persistent_trend()
    {
        var adx = Indicators.Adx(Bars.Trend(80, 1.1m, 0.0010m));
        Assert.True(adx[^1] > 40m, $"ADX was {adx[^1]}");
    }

    [Fact]
    public void Macd_histogram_is_positive_in_an_accelerating_uptrend()
    {
        var closes = Enumerable.Range(0, 60).Select(i => 1m + i * i * 0.00001m).ToArray();
        Assert.True(Indicators.Macd(closes).Histogram[^1] > 0);
    }

    [Fact]
    public void Indicators_are_deterministic()
    {
        var bars = Bars.Trend(250, 1.1m, 0.0002m);
        var a = IndicatorSnapshot.Calculate(TimeFrame.H1, bars);
        var b = IndicatorSnapshot.Calculate(TimeFrame.H1, bars);
        Assert.Equivalent(a, b, strict: true);
        Assert.NotNull(a.Ema200);
    }
}
