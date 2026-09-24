using HVTradingBot.Domain.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Analysis;

public class MultiTimeFrameSeriesTests
{
    [Fact]
    public void Twelve_5m_bars_close_exactly_one_hourly_bar()
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        var bars = Bars.Flat(12, 1.1m);
        IReadOnlyList<TimeFrame> closed = [];
        for (var i = 0; i < bars.Count; i++)
        {
            closed = series.Add(bars[i]);
            if (i < 11)
            {
                Assert.DoesNotContain(TimeFrame.H1, closed);
                Assert.Empty(series.Closed(TimeFrame.H1));
            }
        }

        Assert.Contains(TimeFrame.H1, closed);
        var hour = Assert.Single(series.Closed(TimeFrame.H1));
        Assert.Equal(Bars.Start, hour.OpenTimeUtc);
        Assert.Equal(1200L, hour.Volume);
    }

    [Fact]
    public void Aggregated_bar_uses_first_open_last_close_and_extremes()
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        series.Add(Bars.Bar(Bars.Start, 1.1010m, openPrice: 1.1000m));
        series.Add(Bars.Bar(Bars.Start.AddMinutes(5), 1.0990m, openPrice: 1.1010m));
        series.Add(Bars.Bar(Bars.Start.AddMinutes(10), 1.1005m, openPrice: 1.0990m));

        var m15 = Assert.Single(series.Closed(TimeFrame.M15));
        Assert.Equal(1.1000m, m15.Open);
        Assert.Equal(1.1005m, m15.Close);
        Assert.Equal(1.1015m, m15.High);
        Assert.Equal(1.0985m, m15.Low);
    }

    [Fact]
    public void Out_of_order_bars_are_rejected()
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        series.Add(Bars.Bar(Bars.Start.AddMinutes(5), 1.1m));
        Assert.Throws<InvalidOperationException>(() => series.Add(Bars.Bar(Bars.Start, 1.1m)));
    }

    [Fact]
    public void Gap_closes_the_forming_bar()
    {
        var series = new MultiTimeFrameSeries(Instruments.EurUsd);
        series.Add(Bars.Bar(Bars.Start, 1.1m));
        var closed = series.Add(Bars.Bar(Bars.Start.AddHours(2), 1.1m));
        Assert.Contains(TimeFrame.H1, closed);
        Assert.Single(series.Closed(TimeFrame.H1));
    }
}
