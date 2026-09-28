using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Performance;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Dashboard;

/// <summary>One condition for trusting the bot with real money.</summary>
public sealed record EvaluationCheck(string Key, string Label, bool Passed, string Detail);

/// <summary>
/// How the bot's own Forex trades on the demo account measure up: enough trades, a positive average trade that is more
/// than luck, consistent across periods, with a tolerable drawdown, and run on the simple setup the result is meant to
/// describe. Other markets, signal trades and test trades are reported apart and not judged.
/// </summary>
public sealed record EvaluationDto(
    int TargetTrades,
    int ForexTrades,
    DateTime? SinceUtc,
    PerformanceMetrics Metrics,
    RobustnessReport Robustness,
    int OtherTrades,
    decimal OtherPnl,
    IReadOnlyList<EvaluationCheck> Checks,
    bool Ready,
    string Summary);

/// <summary>
/// The evaluation before any real money: run the demo for <see cref="TargetTrades"/> closed Forex trades and judge it on
/// expectancy (the average trade in R, and whether it survives a Monte Carlo reshuffle) and drawdown, not on win rate
/// or a good week.
/// </summary>
public sealed class EvaluationService(
    IDbContextFactory<TradingDbContext> dbFactory,
    ITradingStateStore stateStore,
    DashboardQueries queries,
    DashboardActions actions,
    MarketCatalogStore catalog,
    TradingEngineOptions engineOptions,
    IDecisionJournal journal,
    IClock clock)
{
    public const int TargetTrades = 200;
    public const decimal MaxDrawdownPercent = 10m;

    public async Task<EvaluationDto> GetAsync(CancellationToken ct)
    {
        var state = await stateStore.GetAsync(ct);
        var broker = state.BrokerName ?? "Paper";
        var forex = await ForexSymbolsAsync(ct);
        var status = await queries.GetStatusAsync(ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The bot's own trades: not signals (the user's choices) and not test trades.
        var closed = await db.Positions.AsNoTracking()
            .Where(p => !p.IsOpen && p.Broker == broker && !p.ClientOrderId.StartsWith(SignalOrders.Prefix) && p.Strategy != TradingEngine.TestTradeStrategy)
            .ToListAsync(ct);
        var trades = closed.Select(p => new ClosedTrade(p.Strategy, p.Instrument, p.Direction, p.OpenedAtUtc, p.ClosedAtUtc!.Value,
            p.RealizedPnl ?? 0, p.RMultiple ?? 0, p.MaePips ?? 0, p.MfePips ?? 0)).ToList();
        var forexTrades = trades.Where(t => forex(t.Instrument)).ToList();
        var others = trades.Where(t => !forex(t.Instrument)).ToList();

        // Open and close times can come from different clocks (market time vs. the clock, which a simulated feed runs
        // apart): the window spans every time either one shows.
        var times = forexTrades.SelectMany(t => new[] { t.OpenedAtUtc, t.ClosedAtUtc }).ToList();
        var since = times.Count == 0 ? (DateTime?)null : times.Min();
        // Drawdown is measured against the money actually traded: the trading capital when one is set.
        var metrics = PerformanceCalculator.Calculate(forexTrades, state.CapitalBase ?? status.Account.StartingBalance);
        var until = times.Count == 0 ? clock.UtcNow : new[] { clock.UtcNow, times.Max() }.Max();
        var robustness = RobustnessAnalysis.Analyze(forexTrades, since ?? until, until);
        var risk = (await actions.GetRiskSettingsAsync(ct)).Effective;
        var selected = await SelectedAsync(ct);
        var nonForex = selected.Where(s => !forex(s)).ToList();
        var withTrades = robustness.Periods.Count(p => p.Trades > 0);

        var checks = new List<EvaluationCheck>
        {
            new("trades", $"{TargetTrades} closed Forex trades", forexTrades.Count >= TargetTrades,
                $"{forexTrades.Count} of {TargetTrades}."),
            new("expectancy", "The average trade makes money, beyond luck", robustness.MonteCarlo is { AverageRLowerBound: > 0 },
                robustness.MonteCarlo is { } mc
                    ? $"Average {metrics.AverageR:0.00}R; with 95% confidence at least {mc.AverageRLowerBound:0.00}R."
                    : $"Needs {RobustnessAnalysis.MinTrades} trades to judge."),
            new("periods", "Profitable in most periods, not one lucky stretch",
                withTrades > 0 && robustness.ProfitablePeriods * 4 >= withTrades * 3 && forexTrades.Count >= RobustnessAnalysis.MinTrades,
                $"Profitable in {robustness.ProfitablePeriods} of {withTrades} period(s)."),
            new("drawdown", $"Largest drop under {MaxDrawdownPercent:0}%", forexTrades.Count > 0 && metrics.MaxDrawdownPercent < MaxDrawdownPercent,
                $"Largest drop from a peak: {metrics.MaxDrawdownPercent:0.0}% ({metrics.MaxDrawdown:N2} {status.Account.Currency})."),
            new("extras", "High-score extras off", risk.MaxExtraDerivedPositions is null or 0,
                risk.MaxExtraDerivedPositions is null or 0 ? "Off." : $"{risk.MaxExtraDerivedPositions} extra position(s) allowed."),
            new("forexOnly", "Only Forex markets selected", nonForex.Count == 0,
                nonForex.Count == 0 ? "Yes." : $"Also selected: {string.Join(", ", nonForex)}.")
        };

        var ready = checks.All(c => c.Passed);
        var summary = ready
            ? "Every check passes. The demo results support moving on, carefully: start real trading with smaller limits than on demo."
            : forexTrades.Count < TargetTrades
                ? $"Keep the demo running: {forexTrades.Count} of {TargetTrades} Forex trades so far. Judge it on the average trade and the drawdown, not the win rate or a good week."
                : "Enough trades, but not every check passes: the demo results do not yet support trading real money.";
        return new EvaluationDto(TargetTrades, forexTrades.Count, since, metrics, robustness, others.Count, Math.Round(others.Sum(t => t.RealizedPnl), 2),
            checks, ready, summary);
    }

    /// <summary>
    /// The setup the evaluation is meant to describe: only Forex markets selected and high-score extras off. Uses the
    /// normal settings paths (validated, audited); every change can be undone on the Settings page.
    /// </summary>
    public async Task<DashboardResult> ApplySetupAsync(DashboardCaller caller, CancellationToken ct)
    {
        var forex = await ForexSymbolsAsync(ct);
        var selection = await catalog.GetSelectionAsync(ct);
        var selected = await SelectedAsync(ct);
        var keep = selected.Where(s => forex(s)).ToList();
        if (keep.Count == 0)
        {
            return DashboardResult.Invalid("instruments", "Select at least one Forex market first (Settings › Markets).");
        }

        var changes = new List<string>();
        if (keep.Count != selected.Count)
        {
            var saved = await actions.SaveMarketSelectionAsync(new MarketSelectionRequest(keep, selection.DerivedOnlyWhenForexClosed), caller, ct);
            if (saved is not DashboardResult.OkResult)
            {
                return saved;
            }

            changes.Add($"markets: {string.Join(", ", keep)}");
        }

        var risk = (await actions.GetRiskSettingsAsync(ct)).Effective;
        if (risk.MaxExtraDerivedPositions is not (null or 0))
        {
            var saved = await actions.SaveRiskSettingsAsync(risk with { MaxExtraDerivedPositions = 0 }, caller, ct);
            if (saved is not DashboardResult.OkResult)
            {
                return saved;
            }

            changes.Add("high-score extras off");
        }

        await journal.RecordAuditAsync(caller.Actor, "EvaluationSetupApplied", changes.Count == 0 ? "Already in place." : string.Join("; ", changes),
            caller.CorrelationId, ct);
        return DashboardResult.Ok(await GetAsync(ct));
    }

    private async Task<IReadOnlyList<string>> SelectedAsync(CancellationToken ct) =>
        (await catalog.GetSelectionAsync(ct)).Instruments ?? engineOptions.Instruments;

    /// <summary>Whether a symbol is a currency pair, from the stored catalog (the worker may not have registered it here).</summary>
    private async Task<Func<string, bool>> ForexSymbolsAsync(CancellationToken ct)
    {
        var classes = (await catalog.GetCatalogAsync(ct)).GroupBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AssetClass, StringComparer.OrdinalIgnoreCase);
        return symbol => classes.TryGetValue(symbol, out var assetClass)
            ? assetClass == nameof(AssetClass.Forex)
            : Instruments.TryGet(symbol, out var instrument) && instrument.IsCurrencyPair;
    }
}
