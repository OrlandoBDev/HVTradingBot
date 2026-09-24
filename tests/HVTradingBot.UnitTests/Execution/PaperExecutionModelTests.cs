using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Execution;

public class PaperExecutionModelTests
{
    private readonly ExecutionCostOptions _costs = new() { SlippagePips = 0.2m, CommissionPer100K = 3m };

    [Fact]
    public void Longs_fill_at_ask_plus_slippage_and_shorts_at_bid_minus_slippage()
    {
        var quote = new Quote(Instruments.EurUsd, Bars.Start, 1.10000m, 1.10010m);
        Assert.Equal(1.10012m, PaperExecutionModel.EntryFill(Direction.Long, quote, _costs));
        Assert.Equal(1.09998m, PaperExecutionModel.EntryFill(Direction.Short, quote, _costs));
    }

    [Fact]
    public void Stop_is_assumed_hit_first_when_bar_touches_both_levels()
    {
        var position = Bars.Position(Instruments.EurUsd, Direction.Long, entry: 1.1000m, stop: 1.0980m, target: 1.1020m);
        var bar = new Candle(Bars.Start, TimeFrame.M5, 1.1000m, 1.1030m, 1.0970m, 1.1000m, 0.0001m, 1);

        var exit = PaperExecutionModel.CheckExit(position, bar, _costs);

        Assert.Equal(ExitReason.StopLoss, exit?.Reason);
        Assert.Equal(1.09798m, exit?.Price); // stop minus 0.2 pip slippage
    }

    [Fact]
    public void Take_profit_uses_bid_for_longs()
    {
        var position = Bars.Position(Instruments.EurUsd, Direction.Long, entry: 1.1000m, stop: 1.0980m, target: 1.1020m);
        // Mid high 1.10204 -> bid high 1.10199 does not reach the target.
        var bar = new Candle(Bars.Start, TimeFrame.M5, 1.1010m, 1.10204m, 1.1005m, 1.1015m, 0.0001m, 1);
        Assert.Null(PaperExecutionModel.CheckExit(position, bar, _costs));

        var through = bar with { High = 1.1030m };
        Assert.Equal(ExitReason.TakeProfit, PaperExecutionModel.CheckExit(position, through, _costs)?.Reason);
    }

    [Fact]
    public void Gap_through_stop_fills_at_the_worse_open()
    {
        var position = Bars.Position(Instruments.EurUsd, Direction.Short, entry: 1.1000m, stop: 1.1020m, target: 1.0950m);
        var gapBar = new Candle(Bars.Start, TimeFrame.M5, 1.1050m, 1.1060m, 1.1040m, 1.1055m, 0m, 1);

        var exit = PaperExecutionModel.CheckExit(position, gapBar, _costs);

        Assert.Equal(ExitReason.StopLoss, exit?.Reason);
        Assert.Equal(1.10502m, exit?.Price);
    }

    [Fact]
    public void Realized_pnl_includes_commission_and_currency_conversion()
    {
        var eur = Bars.Position(Instruments.EurUsd, Direction.Long, entry: 1.1000m, units: 100_000m);
        Assert.Equal(94m, PaperExecutionModel.RealizedPnl(eur, 1.1010m, 1m, _costs)); // 100 - 6 commission

        var jpy = Bars.Position(Instruments.UsdJpy, Direction.Short, entry: 150.00m, units: 100_000m);
        // 0.30 JPY * 100k = 30,000 JPY / 150 = 200 USD - 6
        Assert.Equal(194m, PaperExecutionModel.RealizedPnl(jpy, 149.70m, 1m / 150m, _costs));
    }

    [Fact]
    public void Excursions_track_best_and_worst_prices()
    {
        var position = Bars.Position(Instruments.EurUsd, Direction.Long, entry: 1.1000m);
        var bar = new Candle(Bars.Start, TimeFrame.M5, 1.1000m, 1.1015m, 1.0990m, 1.1005m, 0m, 1);

        var tracked = PaperExecutionModel.TrackExcursion(position, bar);

        Assert.Equal(0.0015m, tracked.MaxFavorableExcursion);
        Assert.Equal(0.0010m, tracked.MaxAdverseExcursion);
    }

    [Fact]
    public void Idempotency_key_is_deterministic_per_signal()
    {
        var a = IdempotencyKey.For(Instruments.EurUsd, "Momentum", Direction.Long, Bars.Start);
        var b = IdempotencyKey.For(Instruments.EurUsd, "Momentum", Direction.Long, Bars.Start);
        var c = IdempotencyKey.For(Instruments.EurUsd, "Momentum", Direction.Long, Bars.Start.AddHours(1));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("EURUSD-Momentum-L-202601050000", a);
    }
}
