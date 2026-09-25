using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Learning;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Application.Performance;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.Application.Backtesting;

public sealed record BacktestResult(
    DateTime FromUtc,
    DateTime ToUtc,
    int BarsProcessed,
    decimal StartingBalance,
    decimal EndingBalance,
    PerformanceMetrics Metrics,
    IReadOnlyDictionary<string, PerformanceMetrics> ByStrategy,
    IReadOnlyDictionary<string, int> DecisionCounts,
    IReadOnlyList<ClosedTrade> Trades,
    ExecutionCostOptions Costs);

/// <summary>
/// Replays historical 5m bars through the same <see cref="TradingEngine"/> used by the live worker, with an
/// in-memory broker that applies the same spread, slippage and commission model.
/// </summary>
public sealed class BacktestEngine(
    TradingEngineOptions engineOptions,
    RiskOptions riskOptions,
    ScoringOptions scoringOptions,
    RegimeOptions regimeOptions,
    ExecutionCostOptions costs,
    LearningOptions learningOptions)
{
    public async Task<BacktestResult> RunAsync(
        IReadOnlyDictionary<Instrument, IReadOnlyList<Candle>> bars,
        CancellationToken cancellationToken,
        bool derivedOnlyWhenForexClosed = false)
    {
        var options = new TradingEngineOptions
        {
            Mode = TradingMode.Paper,
            Instruments = bars.Keys.Select(i => i.Symbol).ToList(),
            AccountCurrency = engineOptions.AccountCurrency,
            StartingBalance = engineOptions.StartingBalance,
            MinPrimaryBars = engineOptions.MinPrimaryBars,
            MinStructuralBars = engineOptions.MinStructuralBars,
            EvaluationTimeFrame = engineOptions.EvaluationTimeFrame
        };

        var broker = new InMemorySimulatedBroker(options.AccountCurrency, options.StartingBalance, costs, riskOptions.AssumedCommissionPercent);
        var journal = new InMemoryJournal();
        var clock = new ReplayClock();
        // The automatic kill switch waits for a person to reset it, which never happens in a replay; after a daily-loss
        // breach the daily-loss rule alone blocks the rest of that day, and trading resumes the next market day.
        var replayRisk = riskOptions.Clone();
        replayRisk.KillSwitchOnDailyLossBreach = false;
        replayRisk.KillSwitchOnStaleData = false;
        var riskManager = new RiskManager(replayRisk, costs);
        // Learning runs inside the replay from a blank slate, so results stay reproducible and free of look-ahead.
        var learning = new LearningService(new InMemoryVirtualTradeStore(), learningOptions, costs, NullLogger<LearningService>.Instance);
        var engine = new TradingEngine(
            new SignalEvaluator(StrategyCatalog.CreateDefault(), scoringOptions, learning),
            riskManager,
            broker,
            new ExecutionService(broker, riskManager, NullLogger<ExecutionService>.Instance),
            new InMemoryStateStore(),
            journal,
            journal,
            clock,
            learning,
            TradingUniverse.From(bars.Keys, options.AccountCurrency, derivedOnlyWhenForexClosed),
            options,
            new FixedRiskOptions(replayRisk),
            regimeOptions,
            NullLogger<TradingEngine>.Instance);

        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>>(), cancellationToken);

        var timeline = bars
            .SelectMany(kv => kv.Value.Select(bar => new InstrumentBar(kv.Key, bar)))
            .GroupBy(b => b.Bar.OpenTimeUtc)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var closeTime = group.First().Bar.CloseTimeUtc;
            clock.UtcNow = closeTime;
            var status = new MarketDataStatus(closeTime, closeTime);
            await engine.ProcessBarsAsync(group.ToList(), status, $"backtest-{closeTime:yyyyMMddHHmm}", cancellationToken);
        }

        var trades = broker.ClosedPositions
            .Select(c => new ClosedTrade(
                c.Closed.Position.Strategy,
                c.Closed.Position.Instrument.Symbol,
                c.Closed.Position.Direction.ToString(),
                c.Closed.Position.OpenedAtUtc,
                c.Closed.ClosedAtUtc,
                c.Closed.RealizedPnl,
                c.Closed.RMultiple,
                Math.Round(c.MaePips, 1),
                Math.Round(c.MfePips, 1)))
            .ToList();

        var account = await broker.GetAccountAsync(cancellationToken);
        return new BacktestResult(
            timeline.Count == 0 ? default : timeline[0].Key,
            timeline.Count == 0 ? default : timeline[^1].Key,
            timeline.Count,
            options.StartingBalance,
            account.Balance,
            PerformanceCalculator.Calculate(trades, options.StartingBalance),
            PerformanceCalculator.ByStrategy(trades, options.StartingBalance),
            journal.DecisionCounts.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value),
            trades,
            costs);
    }
}
