namespace HVTradingBot.Application.Performance;

/// <summary>Results of one consecutive slice of a backtest.</summary>
public sealed record PeriodResult(int Index, DateTime FromUtc, DateTime ToUtc, int Trades, decimal NetPnl, decimal AverageR, decimal WinRate,
    decimal? ProfitFactor);

/// <summary>
/// The spread of outcomes if the same trades had come in a different order and mix (bootstrap: trades drawn at
/// random, with replacement). R values: multiples of the amount risked per trade.
/// </summary>
public sealed record MonteCarloResult(
    int Simulations,
    int TradesPerRun,
    decimal TotalRWorst5,
    decimal TotalRMedian,
    decimal TotalRBest5,
    decimal MaxDrawdownRMedian,
    decimal MaxDrawdownRWorst5,
    decimal ProbabilityOfLossPercent,
    decimal AverageRLowerBound);

public enum RobustnessVerdict
{
    TooFewTrades,
    NoEdge,
    Unproven,
    Holds
}

public sealed record RobustnessReport(
    IReadOnlyList<PeriodResult> Periods,
    int ProfitablePeriods,
    MonteCarloResult? MonteCarlo,
    RobustnessVerdict Verdict,
    string Summary);

/// <summary>
/// Is a backtest's result more than luck? A backtest runs in time order and the engine only learns from trades that
/// have already closed, so each later period is effectively out of sample for what was learned before it (walk
/// forward). An edge should show in most periods, not just one lucky stretch, and survive the Monte Carlo reshuffle:
/// the average trade's 5th-percentile bound must stay above zero.
/// </summary>
public static class RobustnessAnalysis
{
    public const int MinTrades = 30;

    public static RobustnessReport Analyze(IReadOnlyList<ClosedTrade> trades, DateTime fromUtc, DateTime toUtc, int periods = 4,
        int simulations = 2000, int seed = 1)
    {
        var ordered = trades.OrderBy(t => t.ClosedAtUtc).ToList();
        var slices = Periods(ordered, fromUtc, toUtc, Math.Max(1, periods));
        var profitable = slices.Count(p => p.Trades > 0 && p.NetPnl > 0);
        if (ordered.Count < MinTrades)
        {
            return new RobustnessReport(slices, profitable, null, RobustnessVerdict.TooFewTrades,
                $"Only {ordered.Count} trade(s): at least {MinTrades} are needed to judge. Run a longer period or more markets.");
        }

        var monteCarlo = MonteCarlo(ordered.Select(t => t.RMultiple).ToArray(), simulations, seed);
        var averageR = ordered.Average(t => t.RMultiple);
        var withTrades = slices.Count(p => p.Trades > 0);
        var (verdict, summary) = averageR <= 0
            ? (RobustnessVerdict.NoEdge,
                $"No edge: the average trade made {averageR:0.00}R. Profitable in {profitable} of {withTrades} periods.")
            : monteCarlo.AverageRLowerBound > 0 && profitable * 4 >= withTrades * 3
                ? (RobustnessVerdict.Holds,
                    $"The edge holds: profitable in {profitable} of {withTrades} periods, and 95% confident the average trade is above " +
                    $"{monteCarlo.AverageRLowerBound:0.00}R. Confirm it on real (stored) data before trusting it.")
                : (RobustnessVerdict.Unproven,
                    $"Positive but not proven: the average trade made {averageR:0.00}R, but " +
                    (monteCarlo.AverageRLowerBound <= 0
                        ? $"it could be luck (the 95% bound is {monteCarlo.AverageRLowerBound:0.00}R)"
                        : $"it was profitable in only {profitable} of {withTrades} periods") + ".");
        return new RobustnessReport(slices, profitable, monteCarlo, verdict, summary);
    }

    private static List<PeriodResult> Periods(List<ClosedTrade> ordered, DateTime fromUtc, DateTime toUtc, int count)
    {
        var length = (toUtc - fromUtc) / count;
        var result = new List<PeriodResult>();
        for (var i = 0; i < count; i++)
        {
            var start = fromUtc + length * i;
            var end = i == count - 1 ? toUtc : start + length;
            // The last period also takes trades closed after the last bar (open positions closed at the end).
            var inPeriod = ordered.Where(t => t.ClosedAtUtc >= start && (t.ClosedAtUtc < end || i == count - 1)).ToList();
            var gross = inPeriod.Where(t => t.RealizedPnl > 0).Sum(t => t.RealizedPnl);
            var loss = -inPeriod.Where(t => t.RealizedPnl < 0).Sum(t => t.RealizedPnl);
            result.Add(new PeriodResult(i + 1, start, end, inPeriod.Count, Math.Round(gross - loss, 2),
                inPeriod.Count == 0 ? 0 : Math.Round(inPeriod.Average(t => t.RMultiple), 2),
                inPeriod.Count == 0 ? 0 : Math.Round((decimal)inPeriod.Count(t => t.RealizedPnl > 0) / inPeriod.Count * 100m, 1),
                loss == 0 ? null : Math.Round(gross / loss, 2)));
        }

        return result;
    }

    private static MonteCarloResult MonteCarlo(decimal[] r, int simulations, int seed)
    {
        var random = new Random(seed);
        var totals = new decimal[simulations];
        var drawdowns = new decimal[simulations];
        for (var s = 0; s < simulations; s++)
        {
            decimal equity = 0, peak = 0, worst = 0;
            for (var i = 0; i < r.Length; i++)
            {
                equity += r[random.Next(r.Length)];
                peak = Math.Max(peak, equity);
                worst = Math.Max(worst, peak - equity);
            }

            totals[s] = equity;
            drawdowns[s] = worst;
        }

        Array.Sort(totals);
        Array.Sort(drawdowns);
        decimal At(decimal[] sorted, double p) => Math.Round(sorted[Math.Clamp((int)Math.Floor(p * (sorted.Length - 1)), 0, sorted.Length - 1)], 2);
        return new MonteCarloResult(simulations, r.Length, At(totals, 0.05), At(totals, 0.5), At(totals, 0.95), At(drawdowns, 0.5),
            At(drawdowns, 0.95), Math.Round((decimal)totals.Count(t => t < 0) / simulations * 100m, 1),
            Math.Round(At(totals, 0.05) / r.Length, 3));
    }
}
