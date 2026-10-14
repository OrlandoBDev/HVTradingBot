using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Backtesting;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Signals;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Decisions;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.UnitTests.Trading;

/// <summary>Placing a signal the user accepted: re-priced at the live quote, checked by the signal risk rules, journaled.</summary>
public class SignalTradeTests
{
    private static readonly DateTime End = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Quote Live = new(Instruments.EurUsd, End, 1.10000m, 1.10008m);

    private sealed class Store : ISignalStore
    {
        public List<NewSignal> Added { get; } = [];

        public Task<bool> AddAsync(NewSignal signal, CancellationToken cancellationToken)
        {
            if (Added.Any(s => s.SetupId == signal.SetupId))
            {
                return Task.FromResult(false);
            }

            Added.Add(signal);
            return Task.FromResult(true);
        }
    }

    private sealed record Setup(TradingEngine Engine, InMemoryJournal Journal, InMemorySimulatedBroker Broker, InMemoryStateStore State,
        MarketDataStatus Status, Store Signals, IReadOnlyList<Candle> Bars, ReplayClock Clock);

    private static async Task<Setup> Ready(SignalSettings? settings = null, int historyBars = 0)
    {
        var costs = new ExecutionCostOptions();
        var broker = new InMemorySimulatedBroker("USD", 10_000m, costs);
        var journal = new InMemoryJournal { KeepDecisions = true };
        var state = new InMemoryStateStore();
        var clock = new ReplayClock();
        var options = new RiskOptions { MinUnits = 1, UnitStep = 1, MinRewardToRisk = 2m };
        var risk = new RiskManager(options, costs);
        var learning = new LearningService(new InMemoryVirtualTradeStore(), new LearningOptions(), costs, NullLogger<LearningService>.Instance);
        var store = new Store();
        var engine = new TradingEngine(new SignalEvaluator(StrategyCatalog.CreateDefault(), new ScoringOptions(), learning), risk, broker,
            new ExecutionService(broker, risk, NullLogger<ExecutionService>.Instance), state, journal, journal, clock, learning,
            TradingUniverse.From([Instruments.EurUsd], "USD"), new TradingEngineOptions(), new FixedRiskOptions(options), new RegimeOptions(),
            NullLogger<TradingEngine>.Instance, signals: store, signalSettings: new FixedSignalSettings(settings ?? new SignalSettings()));

        var bars = MarketSeriesGenerator.Generate(Instruments.EurUsd, 4, End, 20);
        var history = historyBars > 0 ? bars.Count - historyBars : bars.Count - 1;
        await engine.InitializeAsync(new Dictionary<Instrument, IReadOnlyList<Candle>> { [Instruments.EurUsd] = bars.Take(history).ToList() },
            CancellationToken.None);
        var last = bars[history - 1];
        clock.UtcNow = last.CloseTimeUtc;
        return new Setup(engine, journal, broker, state, new MarketDataStatus(last.CloseTimeUtc, last.CloseTimeUtc), store, bars.Skip(history).ToList(), clock);
    }

    private static SignalRequest Request(decimal entry = 1.10008m, decimal stop = 1.09800m, decimal target = 1.10500m, IReadOnlyList<string>? accepted = null) =>
        new(Guid.NewGuid(), SignalOrders.Prefix + "EURUSD-Test-L-1", "EUR/USD", Direction.Long, "Test", 70, entry, stop, target, accepted ?? [], "tester");

    [Fact]
    public async Task An_accepted_signal_is_placed_as_a_signal_trade_and_audited()
    {
        var s = await Ready();

        var outcome = await s.Engine.PlaceSignalTradeAsync(Request(), s.Status, "c1", CancellationToken.None, [Live]);

        Assert.Equal(SignalOutcomeStatus.Placed, outcome.Status);
        var position = Assert.Single(await s.Broker.GetPositionsAsync(CancellationToken.None));
        Assert.StartsWith(SignalOrders.Prefix, position.ClientOrderId);
        Assert.Equal(outcome.PositionId, position.Id);
        Assert.Contains(s.Journal.Audit, a => a.Action == "SignalTraded");
        Assert.Equal(DecisionState.Executed, s.Journal.Decisions.Single(d => d.ClientOrderId == position.ClientOrderId).State);
        // Sized with the signal risk (0.5% of 10,000), not the bot's.
        Assert.True(outcome.RiskAmount is > 0 and <= 50m, $"risk {outcome.RiskAmount}");
    }

    [Fact]
    public async Task A_signal_whose_price_has_moved_too_far_is_refused()
    {
        var s = await Ready();

        // The ask 1.10008 has covered more than a third of the way from the 1.09900 entry to the 1.10200 target.
        var outcome = await s.Engine.PlaceSignalTradeAsync(Request(entry: 1.09900m, stop: 1.09700m, target: 1.10200m), s.Status, "c1",
            CancellationToken.None, [Live]);

        Assert.Equal(SignalOutcomeStatus.Failed, outcome.Status);
        Assert.Contains("moved too far", outcome.Message);
        Assert.Empty(await s.Broker.GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_soft_rule_comes_back_for_review_and_trades_once_accepted()
    {
        var s = await Ready();
        var weak = Request(target: 1.10250m); // R:R about 1.2

        var review = await s.Engine.PlaceSignalTradeAsync(weak, s.Status, "c1", CancellationToken.None, [Live]);
        var accepted = await s.Engine.PlaceSignalTradeAsync(weak with { AcceptedRules = ["RewardToRisk"] }, s.Status, "c2", CancellationToken.None, [Live]);

        Assert.Equal(SignalOutcomeStatus.NeedsReview, review.Status);
        Assert.Contains(review.Checks, c => c is { Rule: "RewardToRisk", Passed: false });
        Assert.Equal(SignalOutcomeStatus.Placed, accepted.Status);
        Assert.Equal(["RewardToRisk"], accepted.OverriddenRules);
        Assert.Contains(s.Journal.Audit, a => a.Action == "SignalTradedWithOverrides");
    }

    [Fact]
    public async Task The_kill_switch_can_not_be_accepted()
    {
        var s = await Ready();
        await s.State.UpdateAsync(st => st.WithKillSwitch(true, "test", End), CancellationToken.None);

        var outcome = await s.Engine.PlaceSignalTradeAsync(Request(accepted: ["KillSwitch"]), s.Status, "c1", CancellationToken.None, [Live]);

        Assert.Equal(SignalOutcomeStatus.Failed, outcome.Status);
        Assert.Contains("KillSwitch", outcome.Message);
        Assert.Empty(await s.Broker.GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_signals_only_market_is_never_traded_by_the_bot()
    {
        var s = await Ready(new SignalSettings { SignalOnlyInstruments = ["EUR/USD"] }, historyBars: 2000);

        foreach (var bar in s.Bars)
        {
            s.Clock.UtcNow = bar.CloseTimeUtc;
            await s.Engine.ProcessBarsAsync([new InstrumentBar(Instruments.EurUsd, bar)], new MarketDataStatus(bar.CloseTimeUtc, bar.CloseTimeUtc), "t",
                CancellationToken.None);
        }

        Assert.Empty(await s.Broker.GetPositionsAsync(CancellationToken.None));
        Assert.DoesNotContain(s.Journal.Decisions, d => d.State == DecisionState.Executed);
        // Every candidate became a signal; the same setup (one per hour) is sent once however often it is re-evaluated.
        var candidates = s.Journal.Decisions.Where(d => d.State == DecisionState.ApprovalRequired).ToList();
        Assert.NotEmpty(s.Signals.Added);
        Assert.All(candidates, d => Assert.Contains(d.Reasons, r => r.Contains("as a signal")));
        Assert.Equal(s.Signals.Added.Count, s.Signals.Added.Select(a => a.SetupId).Distinct().Count());
        Assert.All(s.Signals.Added, a =>
        {
            Assert.Equal(SignalKinds.SignalsOnlyMarket, a.Kind);
            Assert.Equal(a.CreatedAtUtc.AddMinutes(10), a.ExpiresAtUtc);
        });
    }

    [Fact]
    public async Task Near_misses_are_sent_from_automatic_markets_when_enabled()
    {
        var s = await Ready(new SignalSettings { NearMissEnabled = true, NearMissMinScore = 50 }, historyBars: 2000);

        foreach (var bar in s.Bars)
        {
            s.Clock.UtcNow = bar.CloseTimeUtc;
            await s.Engine.ProcessBarsAsync([new InstrumentBar(Instruments.EurUsd, bar)], new MarketDataStatus(bar.CloseTimeUtc, bar.CloseTimeUtc), "t",
                CancellationToken.None);
        }

        Assert.NotEmpty(s.Signals.Added);
        Assert.All(s.Signals.Added, a =>
        {
            Assert.Equal(SignalKinds.NearMiss, a.Kind);
            Assert.InRange(a.Score, 50, 74);
        });
    }
}
