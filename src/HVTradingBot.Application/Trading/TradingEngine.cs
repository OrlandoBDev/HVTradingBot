using System.Diagnostics;
using System.Diagnostics.Metrics;
using Instrument = HVTradingBot.Domain.MarketData.Instrument;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Application.Trading;

/// <summary>
/// The trading loop: market data -> indicators -> regime -> strategies -> scoring -> abstention -> risk ->
/// final risk -> execution -> monitoring -> journal. The live worker and the backtester both drive this class,
/// so they share identical strategy, risk and fill logic.
/// </summary>
public sealed class TradingEngine
{
    public const string TelemetryName = "HVTradingBot.Trading";
    public const string SystemActor = "system";

    private static readonly ActivitySource ActivitySource = new(TelemetryName);
    private static readonly Meter Meter = new(TelemetryName);
    private static readonly Counter<long> DecisionCounter = Meter.CreateCounter<long>("hvtb.decisions");
    private static readonly Counter<long> ClosedTradeCounter = Meter.CreateCounter<long>("hvtb.trades.closed");

    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private readonly Dictionary<Instrument, MultiTimeFrameSeries> _series = new();
    private readonly Dictionary<Instrument, Quote> _quotes = new();
    private readonly Dictionary<Instrument, (MarketRegime Regime, IndicatorSnapshot Primary, DecisionState State, DateTime Time)> _lastEvaluation = new();

    private readonly SignalEvaluator _evaluator;
    private readonly IRiskManager _riskManager;
    private readonly IExecutionBroker _broker;
    private readonly ExecutionService _execution;
    private readonly ITradingStateStore _state;
    private readonly IDecisionJournal _journal;
    private readonly IMarketSnapshotSink _snapshots;
    private readonly IClock _clock;
    private readonly LearningService _learning;
    private readonly TradingUniverse _universe;
    private readonly TradingEngineOptions _options;
    private readonly IRiskOptionsSource _risk;
    private readonly RegimeOptions _regimeOptions;
    private readonly ILogger<TradingEngine> _logger;
    private readonly ITradeDecisionNotifier? _notifier;

    public TradingEngine(
        SignalEvaluator evaluator,
        IRiskManager riskManager,
        IExecutionBroker broker,
        ExecutionService execution,
        ITradingStateStore state,
        IDecisionJournal journal,
        IMarketSnapshotSink snapshots,
        IClock clock,
        LearningService learning,
        TradingUniverse universe,
        TradingEngineOptions options,
        IRiskOptionsSource risk,
        RegimeOptions regimeOptions,
        ILogger<TradingEngine> logger,
        ITradeDecisionNotifier? notifier = null)
    {
        _evaluator = evaluator;
        _riskManager = riskManager;
        _broker = broker;
        _execution = execution;
        _state = state;
        _journal = journal;
        _snapshots = snapshots;
        _clock = clock;
        _learning = learning;
        _universe = universe;
        _options = options;
        _risk = risk;
        _regimeOptions = regimeOptions;
        _logger = logger;
        _notifier = notifier;
    }

    public IReadOnlyCollection<Instrument> TradedInstruments => _series.Keys;

    public bool IsReady { get; private set; }

    /// <summary>True while at least one Forex market is producing bars (not the weekend or daily break).</summary>
    public bool ForexOpen { get; private set; }

    /// <summary>Forex counts as open if any Forex market produced a bar within the last 15 minutes of market time.</summary>
    private bool IsForexOpen(DateTime marketTimeUtc) =>
        _series.Values.Any(s => s.Instrument.IsCurrencyPair && s.LastBar is { } bar && marketTimeUtc - bar.CloseTimeUtc <= TimeSpan.FromMinutes(15));

    /// <summary>
    /// Warms up indicators from history, then reconciles with broker state (which is authoritative)
    /// before any new trade is allowed.
    /// </summary>
    public async Task InitializeAsync(IReadOnlyDictionary<Instrument, IReadOnlyList<Candle>> history, CancellationToken cancellationToken)
    {
        if (!_universe.IsConfigured)
        {
            throw new InvalidOperationException("The trading universe must be configured before the engine is initialized.");
        }

        _series.Clear();
        foreach (var instrument in _universe.Data)
        {
            _series[instrument] = new MultiTimeFrameSeries(instrument);
        }

        foreach (var (instrument, bars) in history)
        {
            if (!_series.TryGetValue(instrument, out var series))
            {
                continue;
            }

            foreach (var bar in bars)
            {
                series.Add(bar);
            }

            if (bars.Count > 0)
            {
                _quotes[instrument] = QuoteFrom(instrument, bars[^1]);
            }
        }

        // The broker needs current prices for sizing and fills before the first new bar arrives.
        _broker.UpdateQuotes(_quotes.Values);

        // Broker state is authoritative: connect, read the account and open positions before any new trade.
        var account = await _broker.GetAccountAsync(cancellationToken);
        var positions = await _broker.GetPositionsAsync(cancellationToken);
        var broker = _broker.Descriptor;
        if (_series.Values.Select(s => s.LastBar?.CloseTimeUtc).Where(t => t.HasValue).Max() is { } lastBar)
        {
            await _learning.RefreshAsync(lastBar, cancellationToken);
        }
        await _state.UpdateAsync(s => s with { BrokerName = broker.Name, BrokerAccountId = broker.AccountId, BrokerIsDemo = broker.IsDemo },
            cancellationToken);
        await _journal.RecordAuditAsync(SystemActor, "StartupReconciliation",
            $"Broker {broker.Name}{(broker.IsDemo ? " (demo)" : "")} {broker.AccountId}; balance {account.Balance:F2} {account.Currency}; " +
            $"restored {positions.Count} open position(s): " +
            string.Join(", ", positions.Select(p => $"{p.Instrument} {p.Direction} {p.Units:N0}")),
            null, cancellationToken);

        _logger.LogInformation("Trading engine initialized: {Traded} traded instruments ({Symbols}), balance {Balance}, {OpenPositions} open positions restored",
            _universe.Traded.Count, string.Join(", ", _universe.Traded.Select(i => i.Symbol)), account.Balance, positions.Count);
        IsReady = true;

        // Show every selected market on the dashboard immediately, not only after its next bar.
        foreach (var instrument in _universe.Traded)
        {
            await PublishSnapshotAsync(instrument, cancellationToken);
        }
    }

    /// <summary>Records liveness when the feed produced no bar (e.g. market closed), so health checks stay accurate.</summary>
    public Task RecordIdleAsync(MarketDataStatus dataStatus, CancellationToken cancellationToken) =>
        _state.UpdateAsync(s => s with
        {
            LastBarTimeUtc = dataStatus.LastBarTimeUtc ?? s.LastBarTimeUtc,
            LastDataReceivedUtc = dataStatus.LastReceivedUtc,
            WorkerHeartbeatUtc = _clock.UtcNow
        }, cancellationToken);

    /// <summary>Processes one closed 5m bar per instrument (all with the same open time).</summary>
    public async Task ProcessBarsAsync(
        IReadOnlyList<InstrumentBar> bars,
        MarketDataStatus dataStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("InitializeAsync must complete before processing bars.");
        }

        if (bars.Count == 0)
        {
            await RecordIdleAsync(dataStatus, cancellationToken);
            return;
        }

        await _cycleLock.WaitAsync(cancellationToken);
        try
        {
            await ProcessBarsCoreAsync(bars, dataStatus, correlationId, cancellationToken);
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private async Task ProcessBarsCoreAsync(
        IReadOnlyList<InstrumentBar> bars,
        MarketDataStatus dataStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {

        using var activity = ActivitySource.StartActivity("TradingCycle");
        activity?.SetTag("correlation.id", correlationId);

        var closedPrimary = new List<Instrument>();
        var hourClosed = false;
        foreach (var (instrument, bar) in bars)
        {
            if (!_series.TryGetValue(instrument, out var series))
            {
                continue;
            }

            var closed = series.Add(bar);
            _quotes[instrument] = QuoteFrom(instrument, bar);
            hourClosed |= closed.Contains(TimeFrame.H1);
            if (closed.Contains(_options.EvaluationTimeFrame) && _universe.Traded.Contains(instrument))
            {
                closedPrimary.Add(instrument);
            }
        }

        var marketTime = bars.Max(b => b.Bar.CloseTimeUtc);
        var converter = Converter();

        ForexOpen = IsForexOpen(marketTime);
        if (_universe.DerivedOnlyWhenForexClosed && ForexOpen)
        {
            // Forex is trading: Derived markets wait so that position slots go to Forex.
            closedPrimary.RemoveAll(i => i.AssetClass == AssetClass.SyntheticIndex);
        }

        await MonitorPositionsAsync(bars, converter, marketTime, correlationId, cancellationToken);

        foreach (var (instrument, bar) in bars)
        {
            await _learning.OnBarAsync(instrument, bar, cancellationToken);
        }

        if (hourClosed)
        {
            await _learning.RefreshAsync(marketTime, cancellationToken);
        }

        var state = await _state.UpdateAsync(s => s.RollPeriods(marketTime) with
        {
            LastBarTimeUtc = dataStatus.LastBarTimeUtc,
            LastDataReceivedUtc = dataStatus.LastReceivedUtc,
            WorkerHeartbeatUtc = _clock.UtcNow
        }, cancellationToken);

        state = await ApplyAutomaticKillSwitchAsync(state, dataStatus, correlationId, cancellationToken);

        var isStale = dataStatus.IsStale(_clock.UtcNow, TimeSpan.FromSeconds(_risk.Current.MaxMarketDataAgeSeconds));
        foreach (var instrument in closedPrimary)
        {
            await EvaluateInstrumentAsync(instrument, isStale, dataStatus, correlationId, cancellationToken);
        }

        foreach (var (instrument, _) in bars)
        {
            await PublishSnapshotAsync(instrument, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ClosedPosition>> MonitorPositionsAsync(
        IReadOnlyList<InstrumentBar> bars,
        CurrencyConverter converter,
        DateTime marketTime,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var all = new List<ClosedPosition>();
        foreach (var (instrument, bar) in bars)
        {
            var closedPositions = await _broker.ProcessBarAsync(instrument, bar, converter, cancellationToken);
            all.AddRange(closedPositions);
            foreach (var closed in closedPositions)
            {
                ClosedTradeCounter.Add(1, new KeyValuePair<string, object?>("reason", closed.Reason.ToString()));
                _logger.LogInformation(
                    "Position {ClientOrderId} {Instrument} closed by {Reason} at {ExitPrice}: P&L {Pnl} ({R}R)",
                    closed.Position.ClientOrderId, instrument.Symbol, closed.Reason, closed.ExitPrice, closed.RealizedPnl, closed.RMultiple);

                await _state.UpdateAsync(s => s.WithClosedTrade(closed.RealizedPnl, marketTime, _risk.Current, RiskOptions.IsDerived(instrument)),
                    cancellationToken);
                if (_notifier is not null)
                {
                    var broker = _broker.Descriptor;
                    var position = closed.Position;
                    await _notifier.NotifyAsync(new TradeDecisionNotification(
                        position.Id,
                        instrument.DisplayName,
                        position.Strategy,
                        DecisionState.Executed,
                        _options.Mode,
                        new DateTimeOffset(closed.ClosedAtUtc, TimeSpan.Zero),
                        [])
                    {
                        Kind = NotificationKind.TradeClosed,
                        Direction = position.Direction,
                        EntryPrice = position.EntryPrice,
                        ExitPrice = closed.ExitPrice,
                        ExitReason = closed.Reason.ToString(),
                        RealizedPnl = closed.RealizedPnl,
                        RMultiple = closed.RMultiple,
                        Currency = _options.AccountCurrency,
                        Quantity = position.Units,
                        Broker = $"{broker.Name}{(broker.IsDemo ? " (demo)" : "")} {broker.AccountId}".Trim(),
                        DedupeKey = $"closed-{position.ClientOrderId}"
                    }, cancellationToken);
                }
                await _journal.RecordAuditAsync(SystemActor, "PositionClosed",
                    $"{closed.Position.ClientOrderId} {closed.Reason} exit {closed.ExitPrice} P&L {closed.RealizedPnl:F2} R {closed.RMultiple}",
                    correlationId, cancellationToken);
            }
        }

        return all;
    }

    private async Task<TradingSystemState> ApplyAutomaticKillSwitchAsync(
        TradingSystemState state,
        MarketDataStatus dataStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (state.KillSwitchActive)
        {
            return state;
        }

        string? reason = null;
        var account = await _broker.GetAccountAsync(cancellationToken);
        if (_risk.Current.KillSwitchOnDailyLossBreach
            && -state.DailyRealizedPnl >= account.Balance * _risk.Current.MaxDailyLossPercent / 100m)
        {
            reason = $"Daily loss limit breached ({state.DailyRealizedPnl:F2}).";
        }
        else if (_risk.Current.KillSwitchOnStaleData
                 && dataStatus.IsStale(_clock.UtcNow, TimeSpan.FromSeconds(_risk.Current.MaxMarketDataAgeSeconds)))
        {
            reason = "Market data is stale.";
        }

        if (reason is null)
        {
            return state;
        }

        _logger.LogWarning("Kill switch activated automatically: {Reason}", reason);
        await _journal.RecordAuditAsync(SystemActor, "KillSwitchActivated", reason, correlationId, cancellationToken);
        return await _state.UpdateAsync(s => s.WithKillSwitch(true, reason, _clock.UtcNow), cancellationToken);
    }

    private async Task EvaluateInstrumentAsync(
        Instrument instrument,
        bool isStale,
        MarketDataStatus dataStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var series = _series[instrument];
        if (series.Closed(TimeFrame.H1).Count < _options.MinPrimaryBars
            || series.Closed(TimeFrame.H4).Count < _options.MinStructuralBars)
        {
            return; // Warming up; nothing meaningful to decide or journal yet.
        }

        using var activity = ActivitySource.StartActivity("EvaluateInstrument");
        activity?.SetTag("instrument", instrument.Symbol);

        var context = MarketContext.Build(series, _quotes[instrument], _regimeOptions, isStale);
        var signal = await _evaluator.EvaluateAsync(context, cancellationToken);
        var decisionId = Guid.NewGuid();
        var state = signal.State;
        var reasons = signal.Reasons.ToList();
        RiskDecision? risk = null;
        OrderResult? order = null;
        string? clientOrderId = null;

        if (signal.State == DecisionState.Candidate && !instrument.IsTradable)
        {
            state = DecisionState.Observe;
            reasons.Add($"{instrument.DisplayName} is analysis-only (no tradable contract at the broker); not executed.");
        }
        else if (signal.State == DecisionState.Candidate && signal.Best is { } best)
        {
            var signalBarClose = series.Closed(TimeFrame.H1)[^1].CloseTimeUtc;
            clientOrderId = IdempotencyKey.For(instrument, best.Result.Strategy, best.Setup.Direction, signalBarClose);
            var proposal = new TradeProposal(instrument, best.Setup, context.Quote, context.AverageSpread,
                best.Result.Strategy, best.Score.Total, clientOrderId);

            risk = await _riskManager.EvaluateAsync(proposal, await BuildPortfolioAsync(dataStatus, cancellationToken), cancellationToken);
            if (!risk.IsApproved)
            {
                state = DecisionState.RejectedByRisk;
                reasons.Add(risk.RejectionReason!);
            }
            else if (_options.Mode != TradingMode.Paper)
            {
                state = DecisionState.ApprovalRequired;
                reasons.Add($"{_options.Mode} mode requires approval; not executed.");
            }
            else
            {
                (risk, order) = await _execution.ExecuteAsync(
                    proposal, ct => BuildPortfolioAsync(dataStatus, ct), decisionId, correlationId, cancellationToken);

                (state, var reason) = order.Status switch
                {
                    OrderStatus.Filled => (DecisionState.Executed, $"{_broker.Descriptor.Name} order filled at {order.FillPrice}."),
                    OrderStatus.Duplicate => (DecisionState.Expired, "Duplicate signal; already executed."),
                    OrderStatus.Unknown => (DecisionState.Approved, $"Execution state unknown: {order.RejectReason}"),
                    _ => (DecisionState.RejectedByRisk, order.RejectReason ?? "Order rejected.")
                };
                reasons.Add(reason);

                if (order.Status == OrderStatus.Unknown)
                {
                    // docs/BROKER_INTEGRATION.md: never assume success or failure; block further trading until reconciled.
                    var why = $"Uncertain broker order state for {order.ClientOrderId}.";
                    _logger.LogCritical("{Reason} Kill switch activated.", why);
                    await _journal.RecordAuditAsync(SystemActor, "ExecutionUncertainty", why, correlationId, cancellationToken);
                    await _state.UpdateAsync(st => st.WithKillSwitch(true, why, _clock.UtcNow), cancellationToken);
                }
            }
        }

        await _learning.RecordAsync(signal, state, decisionId, series.Closed(TimeFrame.H1)[^1].CloseTimeUtc, cancellationToken);

        DecisionCounter.Add(1, new KeyValuePair<string, object?>("state", state.ToString()));
        _lastEvaluation[instrument] = (context.Regime, context.Primary, state, context.AsOfUtc);

        await _journal.RecordDecisionAsync(new DecisionRecord(
            decisionId,
            correlationId,
            instrument.Symbol,
            context.AsOfUtc,
            state,
            context.Regime,
            signal.Best?.Result.Strategy,
            signal.Best?.Setup.Direction,
            signal.Best?.Score.Total,
            signal.Best?.Score,
            signal.Best?.Setup,
            context.Primary,
            context.Structural,
            signal.StrategyResults,
            risk,
            reasons,
            clientOrderId,
            order), cancellationToken);

        if (_notifier is not null && state is DecisionState.Executed or DecisionState.RejectedByRisk or DecisionState.ApprovalRequired or DecisionState.Approved)
        {
            var broker = _broker.Descriptor;
            // Queued and sent in the background: notification delays or failures never affect trading.
            await _notifier.NotifyAsync(new TradeDecisionNotification(
                decisionId,
                instrument.DisplayName,
                signal.Best?.Result.Strategy,
                state,
                _options.Mode,
                new DateTimeOffset(context.AsOfUtc, TimeSpan.Zero),
                reasons)
            {
                Setup = signal.Best?.Setup,
                Score = signal.Best?.Score.Total,
                Regime = context.Regime.ToString(),
                Quantity = risk?.Units is > 0 ? risk.Units : null,
                BrokerOrderId = order?.OrderId?.ToString(),
                Broker = $"{broker.Name}{(broker.IsDemo ? " (demo)" : "")} {broker.AccountId}".Trim(),
                DedupeKey = clientOrderId ?? $"{instrument.Symbol}|{signal.Best?.Result.Strategy}|{series.Closed(TimeFrame.H1)[^1].CloseTimeUtc:O}"
            }, cancellationToken);
        }
    }

    public const string TestTradeStrategy = "TestTrade";

    /// <summary>
    /// Places a trade on request (from the dashboard) to verify the whole pipeline: the same risk checks (twice),
    /// execution, journal and notifications as a strategy trade. Direction follows the market regime; stop and target
    /// are 1.5 and 3 ATR (reward:risk 2). It is sized like a real trade.
    /// </summary>
    public async Task<TestTradeOutcome> PlaceTestTradeAsync(Instrument instrument, MarketDataStatus dataStatus, string requestedBy,
        string correlationId, CancellationToken cancellationToken, IReadOnlyCollection<Quote>? liveQuotes = null)
    {
        if (!IsReady)
        {
            return TestTradeOutcome.Failed("The trading engine is still starting.");
        }

        await _cycleLock.WaitAsync(cancellationToken);
        try
        {
            // Use live tick prices when available (bars are up to five minutes old) and hand them to the broker.
            foreach (var live in liveQuotes ?? [])
            {
                if (_series.ContainsKey(live.Instrument) && live.Ask > live.Bid)
                {
                    _quotes[live.Instrument] = live;
                }
            }

            _broker.UpdateQuotes(_quotes.Values);

            if (!_series.TryGetValue(instrument, out var series) || !_quotes.TryGetValue(instrument, out var quote))
            {
                return TestTradeOutcome.Failed($"{instrument.DisplayName} is not one of the selected markets.");
            }

            if (!instrument.IsTradable)
            {
                return TestTradeOutcome.Failed($"{instrument.DisplayName} is analysis-only at the broker.");
            }

            if (series.Indicators(TimeFrame.H1) is not { Atr: { } atr } || series.Indicators(TimeFrame.H4) is null)
            {
                return TestTradeOutcome.Failed($"Not enough history for {instrument.DisplayName} yet.");
            }

            var context = MarketContext.Build(series, quote, _regimeOptions,
                dataStatus.IsStale(_clock.UtcNow, TimeSpan.FromSeconds(_risk.Current.MaxMarketDataAgeSeconds)));
            var direction = context.Regime == MarketRegime.TrendingBearish ? Direction.Short : Direction.Long;
            var entry = direction == Direction.Long ? quote.Ask : quote.Bid;
            var sign = direction.Sign();
            // Stop 1.5 ATR away; target at the minimum R:R (at least 2) measured from the rounded stop and rounded away
            // from the entry, so rounding to the market's precision can never leave it just below the risk limit.
            var stop = instrument.RoundPrice(entry - sign * atr * 1.5m);
            var rewardToRisk = Math.Max(2m, _risk.Current.MinRewardToRisk);
            var target = Math.Round(entry + sign * Math.Abs(entry - stop) * rewardToRisk, instrument.PriceDecimals,
                direction == Direction.Long ? MidpointRounding.ToPositiveInfinity : MidpointRounding.ToNegativeInfinity);
            var setup = new TradeSetup(direction, entry, stop, target);
            var clientOrderId = $"TEST-{instrument.BaseCurrency}{(instrument.IsCurrencyPair ? instrument.QuoteCurrency : "")}-{_clock.UtcNow:yyyyMMddHHmmss}";
            var proposal = new TradeProposal(instrument, setup, quote, context.AverageSpread, TestTradeStrategy, 0, clientOrderId) { IsTestTrade = true };
            var decisionId = Guid.NewGuid();
            var reasons = new List<string> { $"Test trade requested from the dashboard by {requestedBy}." };

            var risk = await _riskManager.EvaluateAsync(proposal, await BuildPortfolioAsync(dataStatus, cancellationToken), cancellationToken);
            OrderResult? order = null;
            DecisionState state;
            if (!risk.IsApproved)
            {
                state = DecisionState.RejectedByRisk;
                reasons.Add(risk.RejectionReason!);
            }
            else
            {
                (risk, order) = await _execution.ExecuteAsync(proposal, ct => BuildPortfolioAsync(dataStatus, ct), decisionId, correlationId, cancellationToken);
                state = order.Status == OrderStatus.Filled ? DecisionState.Executed : DecisionState.RejectedByRisk;
                reasons.Add(order.Status == OrderStatus.Filled
                    ? $"{_broker.Descriptor.Name} order filled at {order.FillPrice}."
                    : order.RejectReason ?? $"Order {order.Status}.");
            }

            await _journal.RecordDecisionAsync(new DecisionRecord(decisionId, correlationId, instrument.Symbol, context.AsOfUtc, state, context.Regime,
                TestTradeStrategy, direction, 0, null, setup, context.Primary, context.Structural, [], risk, reasons, clientOrderId, order), cancellationToken);
            await _journal.RecordAuditAsync(requestedBy, "TestTrade", string.Join(" ", reasons), correlationId, cancellationToken);

            if (_notifier is not null && state == DecisionState.Executed)
            {
                var broker = _broker.Descriptor;
                await _notifier.NotifyAsync(new TradeDecisionNotification(decisionId, instrument.DisplayName, TestTradeStrategy, state, _options.Mode,
                    new DateTimeOffset(context.AsOfUtc, TimeSpan.Zero), reasons)
                {
                    Setup = setup,
                    Regime = context.Regime.ToString(),
                    Quantity = risk.Units,
                    BrokerOrderId = order?.OrderId?.ToString(),
                    Broker = $"{broker.Name}{(broker.IsDemo ? " (demo)" : "")} {broker.AccountId}".Trim(),
                    DedupeKey = clientOrderId
                }, cancellationToken);
            }

            return order is { Status: OrderStatus.Filled }
                ? new TestTradeOutcome(true, $"{Side(direction)} {instrument.DisplayName} filled at {order.FillPrice}; risk {risk.RiskAmount:F2} {_options.AccountCurrency}.",
                    clientOrderId, order.PositionId, order.FillPrice)
                : TestTradeOutcome.Failed(string.Join(" ", reasons.Skip(1)), clientOrderId);
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    /// <summary>
    /// Closes a position before its stop or target (dashboard "Close" or the end of a test trade), then reconciles that
    /// market immediately so the result is recorded, counted in the loss limits and emailed without waiting for a bar.
    /// </summary>
    public async Task<(OrderResult Result, ClosedPosition? Closed)> ClosePositionAsync(Guid positionId, string correlationId,
        CancellationToken cancellationToken)
    {
        await _cycleLock.WaitAsync(cancellationToken);
        try
        {
            var position = (await _broker.GetPositionsAsync(cancellationToken)).FirstOrDefault(p => p.Id == positionId);
            if (position is null)
            {
                return (OrderResult.Rejected(positionId.ToString(), "This position is no longer open."), null);
            }

            var result = await _broker.ClosePositionAsync(positionId.ToString(), cancellationToken);
            if (result.Status == OrderStatus.Rejected
                || !_series.TryGetValue(position.Instrument, out var series) || series.LastBar is not { } lastBar)
            {
                return (result, null);
            }

            _broker.UpdateQuotes(_quotes.Values);
            var closed = await MonitorPositionsAsync([new InstrumentBar(position.Instrument, lastBar)], Converter(), lastBar.CloseTimeUtc,
                correlationId, cancellationToken);
            return (result, closed.FirstOrDefault(c => c.Position.Id == positionId));
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private static string Side(Direction d) => d == Direction.Long ? "BUY" : "SELL";

    public async Task<PortfolioState> BuildPortfolioAsync(MarketDataStatus dataStatus, CancellationToken cancellationToken)
    {
        var state = await _state.GetAsync(cancellationToken);
        var account = await _broker.GetAccountAsync(cancellationToken);
        var positions = (await _broker.GetPositionsAsync(cancellationToken)).ToList();
        var converter = Converter();
        var unrealized = positions
            .Where(p => _quotes.ContainsKey(p.Instrument))
            .Sum(p => PaperExecutionModel.UnrealizedPnl(p, _quotes[p.Instrument], converter.QuoteToAccountRate(p.Instrument)));
        var marketTime = _quotes.Values.Select(q => q.TimestampUtc).DefaultIfEmpty(_clock.UtcNow).Max();

        return new PortfolioState(
            _options.Mode,
            account.Balance,
            account.Balance + unrealized,
            state.PnlDay == DateOnly.FromDateTime(marketTime) ? state.DailyRealizedPnl : 0,
            state.WeeklyRealizedPnl,
            positions,
            state.ConsecutiveLosses,
            state.CooldownUntilUtc,
            state.KillSwitchActive,
            state.KillSwitchReason,
            dataStatus,
            marketTime,
            _clock.UtcNow,
            converter)
        {
            DerivedDailyRealizedPnl = state.PnlDay == DateOnly.FromDateTime(marketTime) ? state.DerivedDailyRealizedPnl : 0,
            // While Forex is closed, Derived may use every position slot.
            ForexMarketOpen = IsForexOpen(marketTime)
        };
    }

    /// <summary>
    /// Publishes price, regime and indicators for the dashboard. Regime and indicators come from the market's own bars on
    /// every update, so they are shown even for markets that are not being evaluated (e.g. Derived while Forex is open)
    /// and immediately after a restart. The last decision is shown only once the market has been evaluated.
    /// </summary>
    private async Task PublishSnapshotAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        if (!_quotes.TryGetValue(instrument, out var quote) || !_series.TryGetValue(instrument, out var series))
        {
            return;
        }

        var primary = series.Indicators(TimeFrame.H1);
        var structural = series.Indicators(TimeFrame.H4);
        MarketRegime? regime = primary is not null && structural is not null
            ? RegimeClassifier.Classify(structural, primary, _regimeOptions)
            : null;
        var last = _lastEvaluation.TryGetValue(instrument, out var e) ? e : default;

        await _snapshots.PublishAsync(new MarketSnapshot(
            instrument.Symbol,
            quote.TimestampUtc,
            quote.Bid,
            quote.Ask,
            Math.Round(instrument.ToPips(quote.Spread), 2),
            regime,
            primary,
            last.Primary is null ? null : last.State,
            last.Primary is null ? null : last.Time), cancellationToken);
    }

    private CurrencyConverter Converter() =>
        new(_options.AccountCurrency, _quotes.ToDictionary(q => q.Key.Symbol, q => q.Value.Mid));

    private static Quote QuoteFrom(Instrument instrument, Candle bar) =>
        new(instrument, bar.CloseTimeUtc, instrument.RoundPrice(bar.CloseBid), instrument.RoundPrice(bar.CloseAsk));
}
