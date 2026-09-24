using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Performance;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Infrastructure.MarketData;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Backtesting;

public class BacktestAndEngineTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BacktestEngine Engine() =>
        new(new TradingEngineOptions(), new RiskOptions(), new ScoringOptions(), new RegimeOptions(), new ExecutionCostOptions(), new LearningOptions());

    private static Dictionary<Instrument, IReadOnlyList<Candle>> Data(int days, int seed = 7) =>
        Instruments.Defaults.ToDictionary(i => i, i => MarketSeriesGenerator.Generate(i, seed, End, days));

    [Fact]
    public void Generator_is_deterministic_and_produces_consistent_bars()
    {
        var a = MarketSeriesGenerator.Generate(Instruments.EurUsd, 1, End, 5);
        var b = MarketSeriesGenerator.Generate(Instruments.EurUsd, 1, End, 5);
        var c = MarketSeriesGenerator.Generate(Instruments.EurUsd, 2, End, 5);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.All(a, bar =>
        {
            Assert.True(bar.High >= Math.Max(bar.Open, bar.Close));
            Assert.True(bar.Low <= Math.Min(bar.Open, bar.Close));
            Assert.True(bar.Spread > 0);
            Assert.NotEqual(DayOfWeek.Saturday, bar.OpenTimeUtc.DayOfWeek);
            Assert.NotEqual(DayOfWeek.Sunday, bar.OpenTimeUtc.DayOfWeek);
        });
    }

    [Fact]
    public async Task Backtest_is_reproducible_with_the_same_data()
    {
        var data = Data(60);

        var first = await Engine().RunAsync(data, CancellationToken.None);
        var second = await Engine().RunAsync(data, CancellationToken.None);

        Assert.Equal(first.Metrics, second.Metrics);
        Assert.Equal(first.DecisionCounts, second.DecisionCounts);
        Assert.Equal(first.Trades.Select(t => (t.Instrument, t.OpenedAtUtc, t.RealizedPnl)), second.Trades.Select(t => (t.Instrument, t.OpenedAtUtc, t.RealizedPnl)));
    }

    [Fact]
    public async Task Backtest_respects_risk_limits_and_journals_rejections()
    {
        var result = await Engine().RunAsync(Data(90, seed: 11), CancellationToken.None);

        Assert.True(result.BarsProcessed > 15_000); // ~64 trading days x 288 bars
        Assert.True(result.DecisionCounts.GetValueOrDefault(nameof(DecisionState.NoTrade)) > 0);
        Assert.Equal(result.StartingBalance + result.Metrics.NetPnl, result.EndingBalance);

        // Stops are sized to 0.5% of equity; slippage and gaps can push slightly past -1R but never far.
        Assert.All(result.Trades, t => Assert.True(t.RMultiple >= -1.5m, $"{t.Instrument} {t.RMultiple}R"));
    }

    [Fact]
    public async Task Duplicate_client_order_id_never_opens_a_second_position()
    {
        var broker = new InMemorySimulatedBroker("USD", 100_000m, new ExecutionCostOptions());
        var bar = Bars.Bar(Bars.Start, 1.1m);
        await broker.ProcessBarAsync(Instruments.EurUsd, bar, Bars.Converter(), CancellationToken.None);
        var order = new TradeOrder("EURUSD-X-L-1", Instruments.EurUsd, Direction.Long, 10_000m, 1.1m, 1.09m, 1.12m, 100m, "X", 80, null, "c");

        var first = await broker.PlaceOrderAsync(order, CancellationToken.None);
        var retry = await broker.PlaceOrderAsync(order, CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, first.Status);
        Assert.Equal(OrderStatus.Duplicate, retry.Status);
        Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public void Performance_metrics_match_hand_calculation()
    {
        var t0 = Bars.Start;
        ClosedTrade Trade(int i, decimal pnl, decimal r) => new("S", "EUR/USD", "Long", t0, t0.AddHours(i), pnl, r, 5, 10);
        var trades = new[] { Trade(1, 200, 2), Trade(2, -100, -1), Trade(3, -100, -1), Trade(4, 300, 3) };

        var m = PerformanceCalculator.Calculate(trades, 10_000m);

        Assert.Equal(4, m.TotalTrades);
        Assert.Equal(50m, m.WinRate);
        Assert.Equal(300m, m.NetPnl);
        Assert.Equal(2.5m, m.ProfitFactor);
        Assert.Equal(75m, m.Expectancy);
        Assert.Equal(0.75m, m.AverageR);
        Assert.Equal(200m, m.MaxDrawdown);
        Assert.Equal(2, m.LongestLosingStreak);
    }
}
