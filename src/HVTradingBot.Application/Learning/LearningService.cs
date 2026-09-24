using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Application.Learning;

/// <summary>A setup followed as if it had been traded, whether or not it actually was.</summary>
public sealed record VirtualSetup(
    Guid Id,
    string SetupId,
    Guid? DecisionId,
    SetupKey Key,
    string Instrument,
    Direction Direction,
    int Score,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    DateTime OpenedAtUtc,
    DecisionState DecisionState);

public enum VirtualOutcome
{
    Win,
    Loss,
    Expired
}

public interface IVirtualTradeStore
{
    /// <summary>Adds setups; ones whose <see cref="VirtualSetup.SetupId"/> already exists are ignored.</summary>
    Task AddAsync(IReadOnlyList<VirtualSetup> setups, CancellationToken cancellationToken);

    Task<IReadOnlyList<VirtualSetup>> GetOpenAsync(string instrument, CancellationToken cancellationToken);

    Task ResolveAsync(Guid id, VirtualOutcome outcome, decimal rMultiple, decimal exitPrice, DateTime closedAtUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<SetupOutcome>> GetOutcomesAsync(DateTime sinceUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Learns from every setup the engine finds. Each setup becomes a virtual trade resolved on later bars with the same
/// fill model as paper trading (spread, slippage, stop-first on ambiguous bars). Resolved outcomes feed
/// <see cref="StrategyPerformanceModel"/>, whose bounded adjustments are applied when scoring new setups.
/// </summary>
public sealed class LearningService(
    IVirtualTradeStore store,
    LearningOptions options,
    ExecutionCostOptions costs,
    ILogger<LearningService> logger) : IStrategyPerformanceProvider
{
    private volatile IReadOnlyDictionary<SetupKey, StrategyPerformance> _model = new Dictionary<SetupKey, StrategyPerformance>();

    public IReadOnlyDictionary<SetupKey, StrategyPerformance> Model => _model;

    public StrategyPerformance? Get(SetupKey key) => options.Enabled ? _model.GetValueOrDefault(key) : null;

    public async Task RefreshAsync(DateTime marketTimeUtc, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var outcomes = await store.GetOutcomesAsync(marketTimeUtc.AddDays(-options.LookbackDays), cancellationToken);
        _model = StrategyPerformanceModel.Compute(outcomes, marketTimeUtc, options);
        var disabled = _model.Values.Where(p => p.Disabled).Select(p => p.Key.ToString()).ToList();
        logger.LogInformation("Learning model refreshed: {Outcomes} outcomes, {Keys} combinations, disabled: {Disabled}",
            outcomes.Count, _model.Count, disabled.Count == 0 ? "none" : string.Join(", ", disabled));
    }

    /// <summary>Records every scored setup of an evaluation (one per strategy, direction and signal bar).</summary>
    public Task RecordAsync(SignalEvaluation evaluation, DecisionState state, Guid decisionId, DateTime signalBarCloseUtc, CancellationToken cancellationToken)
    {
        if (!options.Enabled || evaluation.ScoredCandidates.Count == 0)
        {
            return Task.CompletedTask;
        }

        var context = evaluation.Context;
        var setups = evaluation.ScoredCandidates.Select(c => new VirtualSetup(
            Guid.NewGuid(),
            IdempotencyKey.For(context.Instrument, c.Result.Strategy, c.Setup.Direction, signalBarCloseUtc),
            decisionId,
            new SetupKey(c.Result.Strategy, context.Regime, context.Instrument.AssetClass),
            context.Instrument.Symbol,
            c.Setup.Direction,
            c.Score.Total,
            c.Setup.Entry,
            c.Setup.StopLoss,
            c.Setup.TakeProfit,
            context.AsOfUtc,
            c == evaluation.Best ? state : DecisionState.NoTrade)).ToList();
        return store.AddAsync(setups, cancellationToken);
    }

    /// <summary>Resolves virtual trades on <paramref name="instrument"/> that hit their stop or target (or expired) in this bar.</summary>
    public async Task<int> OnBarAsync(Instrument instrument, Candle bar, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return 0;
        }

        var resolved = 0;
        foreach (var setup in await store.GetOpenAsync(instrument.Symbol, cancellationToken))
        {
            if (setup.OpenedAtUtc >= bar.CloseTimeUtc)
            {
                continue;
            }

            var risk = Math.Abs(setup.Entry - setup.StopLoss);
            if (risk == 0)
            {
                continue;
            }

            var position = new OpenPosition(setup.Id, setup.SetupId, instrument, setup.Direction, 1, setup.Entry, setup.StopLoss,
                setup.TakeProfit, 0, setup.OpenedAtUtc, setup.Key.Strategy, setup.Score, 0, 0);
            var sign = setup.Direction.Sign();

            if (PaperExecutionModel.CheckExit(position, bar, costs) is { } exit)
            {
                var r = Math.Round(sign * (exit.Price - setup.Entry) / risk, 3);
                await store.ResolveAsync(setup.Id, exit.Reason == ExitReason.TakeProfit ? VirtualOutcome.Win : VirtualOutcome.Loss, r, exit.Price,
                    bar.CloseTimeUtc, cancellationToken);
                resolved++;
            }
            else if (bar.CloseTimeUtc - setup.OpenedAtUtc >= TimeSpan.FromHours(options.ExpireAfterHours))
            {
                var exitPrice = setup.Direction == Direction.Long ? bar.CloseBid : bar.CloseAsk;
                await store.ResolveAsync(setup.Id, VirtualOutcome.Expired, Math.Round(sign * (exitPrice - setup.Entry) / risk, 3), exitPrice,
                    bar.CloseTimeUtc, cancellationToken);
                resolved++;
            }
        }

        return resolved;
    }
}

/// <summary>In-memory store for backtests (learning happens within the replay, deterministically).</summary>
public sealed class InMemoryVirtualTradeStore : IVirtualTradeStore
{
    private readonly Dictionary<string, Dictionary<Guid, VirtualSetup>> _openByInstrument = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<SetupOutcome> _outcomes = [];

    public Task AddAsync(IReadOnlyList<VirtualSetup> setups, CancellationToken cancellationToken)
    {
        foreach (var setup in setups.Where(s => _seen.Add(s.SetupId)))
        {
            if (!_openByInstrument.TryGetValue(setup.Instrument, out var open))
            {
                _openByInstrument[setup.Instrument] = open = new Dictionary<Guid, VirtualSetup>();
            }

            open[setup.Id] = setup;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VirtualSetup>> GetOpenAsync(string instrument, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VirtualSetup>>(_openByInstrument.TryGetValue(instrument, out var open) ? open.Values.ToList() : []);

    public Task ResolveAsync(Guid id, VirtualOutcome outcome, decimal rMultiple, decimal exitPrice, DateTime closedAtUtc, CancellationToken cancellationToken)
    {
        foreach (var open in _openByInstrument.Values)
        {
            if (open.Remove(id, out var setup))
            {
                _outcomes.Add(new SetupOutcome(setup.Key, rMultiple, closedAtUtc));
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SetupOutcome>> GetOutcomesAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SetupOutcome>>(_outcomes.Where(o => o.ClosedAtUtc >= sinceUtc).ToList());
}
