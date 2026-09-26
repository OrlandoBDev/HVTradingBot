using HVTradingBot.Application.Performance;

namespace HVTradingBot.UnitTests.Backtesting;

public class RobustnessAnalysisTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(80);

    /// <summary>Trades spread evenly over the 80 days with the given R results (1R = 100).</summary>
    private static List<ClosedTrade> Trades(params decimal[] r) => r.Select((x, i) =>
    {
        var closed = From.AddHours(12 + i * (80 * 24.0 - 24) / r.Length);
        return new ClosedTrade("Test", "EUR/USD", "Long", closed.AddHours(-2), closed, x * 100m, x, 0, 0);
    }).ToList();

    private static decimal[] Repeat(int times, params decimal[] pattern) => Enumerable.Repeat(pattern, times).SelectMany(p => p).ToArray();

    [Fact]
    public void A_steady_edge_holds()
    {
        // Win 2R, lose 1R, lose 1R, win 2R: +0.5R a trade, in every period.
        var report = RobustnessAnalysis.Analyze(Trades(Repeat(20, 2, -1, -1, 2)), From, To);

        Assert.Equal(RobustnessVerdict.Holds, report.Verdict);
        Assert.Equal(4, report.Periods.Count);
        Assert.Equal(80, report.Periods.Sum(p => p.Trades));
        Assert.Equal(4, report.ProfitablePeriods);
        Assert.True(report.MonteCarlo!.AverageRLowerBound > 0);
        Assert.True(report.MonteCarlo.ProbabilityOfLossPercent < 5);
    }

    [Fact]
    public void Losing_on_average_is_no_edge()
    {
        var report = RobustnessAnalysis.Analyze(Trades(Repeat(20, 1.5m, -1, -1, -1)), From, To);

        Assert.Equal(RobustnessVerdict.NoEdge, report.Verdict);
        Assert.True(report.MonteCarlo!.ProbabilityOfLossPercent > 50);
    }

    [Fact]
    public void One_lucky_stretch_is_not_proof()
    {
        // Break-even for most of the run, then a stretch of wins: positive overall, but too few periods made money.
        var r = Repeat(18, -1, 1).Concat(Repeat(12, 2)).ToArray();
        var report = RobustnessAnalysis.Analyze(Trades(r), From, To);

        Assert.Equal(RobustnessVerdict.Unproven, report.Verdict);
        Assert.True(report.ProfitablePeriods * 4 < report.Periods.Count * 3, $"{report.ProfitablePeriods} profitable periods");
    }

    [Fact]
    public void Few_trades_are_not_judged_and_results_are_reproducible()
    {
        var few = RobustnessAnalysis.Analyze(Trades(2, -1, 2), From, To);
        var a = RobustnessAnalysis.Analyze(Trades(Repeat(10, 2, -1, -1)), From, To);
        var b = RobustnessAnalysis.Analyze(Trades(Repeat(10, 2, -1, -1)), From, To);

        Assert.Equal(RobustnessVerdict.TooFewTrades, few.Verdict);
        Assert.Null(few.MonteCarlo);
        Assert.Equal(a.MonteCarlo, b.MonteCarlo);
    }
}
