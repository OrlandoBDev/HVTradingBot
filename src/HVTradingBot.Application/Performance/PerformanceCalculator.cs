namespace HVTradingBot.Application.Performance;

public sealed record ClosedTrade(
    string Strategy,
    string Instrument,
    string Direction,
    DateTime OpenedAtUtc,
    DateTime ClosedAtUtc,
    decimal RealizedPnl,
    decimal RMultiple,
    decimal MaePips,
    decimal MfePips);

public sealed record PerformanceMetrics(
    int TotalTrades,
    int Wins,
    int Losses,
    decimal WinRate,
    decimal NetPnl,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal? ProfitFactor,
    decimal Expectancy,
    decimal AverageR,
    decimal MaxDrawdown,
    decimal MaxDrawdownPercent,
    int LongestLosingStreak,
    decimal AverageMaePips,
    decimal AverageMfePips);

public static class PerformanceCalculator
{
    /// <summary>Computes metrics over trades in close order. Drawdown is measured on realized equity from <paramref name="startingBalance"/>.</summary>
    public static PerformanceMetrics Calculate(IEnumerable<ClosedTrade> trades, decimal startingBalance)
    {
        var ordered = trades.OrderBy(t => t.ClosedAtUtc).ToList();
        if (ordered.Count == 0)
        {
            return new PerformanceMetrics(0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, 0, 0);
        }

        var wins = ordered.Count(t => t.RealizedPnl > 0);
        var losses = ordered.Count(t => t.RealizedPnl <= 0);
        var grossProfit = ordered.Where(t => t.RealizedPnl > 0).Sum(t => t.RealizedPnl);
        var grossLoss = -ordered.Where(t => t.RealizedPnl < 0).Sum(t => t.RealizedPnl);
        var net = grossProfit - grossLoss;

        decimal equity = startingBalance, peak = startingBalance, maxDd = 0, maxDdPct = 0;
        int streak = 0, longest = 0;
        foreach (var trade in ordered)
        {
            equity += trade.RealizedPnl;
            peak = Math.Max(peak, equity);
            var dd = peak - equity;
            if (dd > maxDd)
            {
                maxDd = dd;
                maxDdPct = peak == 0 ? 0 : dd / peak * 100m;
            }

            streak = trade.RealizedPnl <= 0 ? streak + 1 : 0;
            longest = Math.Max(longest, streak);
        }

        return new PerformanceMetrics(
            ordered.Count,
            wins,
            losses,
            Math.Round((decimal)wins / ordered.Count * 100m, 2),
            Math.Round(net, 2),
            Math.Round(grossProfit, 2),
            Math.Round(grossLoss, 2),
            grossLoss == 0 ? null : Math.Round(grossProfit / grossLoss, 2),
            Math.Round(net / ordered.Count, 2),
            Math.Round(ordered.Average(t => t.RMultiple), 2),
            Math.Round(maxDd, 2),
            Math.Round(maxDdPct, 2),
            longest,
            Math.Round(ordered.Average(t => t.MaePips), 1),
            Math.Round(ordered.Average(t => t.MfePips), 1));
    }

    public static IReadOnlyDictionary<string, PerformanceMetrics> ByStrategy(IEnumerable<ClosedTrade> trades, decimal startingBalance) =>
        trades.GroupBy(t => t.Strategy)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Calculate(g, startingBalance));
}
