using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Risk;

/// <summary>High-score extras for every market type, and Derived using every slot while Forex is closed.</summary>
public class HighScoreExtrasTests
{
    private static readonly Instrument VolA = Instruments.Register(new Instrument("R_EXA", "R_EXA", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility A test"
    });

    private static readonly Instrument VolB = Instruments.Register(new Instrument("R_EXB", "R_EXB", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility B test"
    });

    private static readonly Instrument VolC = Instruments.Register(new Instrument("R_EXC", "R_EXC", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility C test"
    });

    private static readonly Instrument VolD = Instruments.Register(new Instrument("R_EXD", "R_EXD", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility D test"
    });

    private static readonly RiskOptions Options = new()
    {
        MaxRiskPerTradePercent = 1, MaxOpenPositions = 3, MaxDerivedOpenPositions = 1, DerivedRiskPerTradePercent = 0.5m,
        MaxDerivedDailyLossPercent = 1, MaxDailyLossPercent = 3, MaxExtraDerivedPositions = 2, HighScoreOverrideMinScore = 90,
        MaxCurrencyExposure = 3, MinUnits = 1, UnitStep = 1
    };

    private readonly RiskManager _risk = new(Options, new ExecutionCostOptions { SlippagePips = 0 });

    private Task<RiskDecision> Evaluate(TradeProposal proposal, PortfolioState portfolio) =>
        _risk.EvaluateAsync(proposal, portfolio, CancellationToken.None);

    private static OpenPosition Forex(Instrument instrument, string id, decimal risk = 10m) =>
        Bars.Position(instrument, Direction.Long, entry: 1.1m, stop: 1.09m, target: 1.13m, units: 1000m, clientOrderId: id) with { InitialRiskAmount = risk };

    private static OpenPosition Derived(Instrument instrument, string id, decimal risk = 10m) =>
        Bars.Position(instrument, Direction.Long, entry: 1000m, stop: 990m, target: 1030m, units: 1m, clientOrderId: id) with { InitialRiskAmount = risk };

    private static TradeProposal DerivedProposal(Instrument instrument, int score = 80) =>
        new(instrument, new TradeSetup(Direction.Long, 1000m, 990m, 1030m), new Quote(instrument, Bars.Start, 999.9m, 1000.1m), 0.2m, "Test", score,
            $"{instrument.Symbol}-new");

    private static PortfolioState Portfolio(bool forexOpen = true, decimal dailyPnl = 0, params OpenPosition[] positions) =>
        Bars.Portfolio(balance: 10_000m, positions: positions, dailyPnl: dailyPnl) with { ForexMarketOpen = forexOpen };

    /// <summary>Three Forex positions fill the normal slots.</summary>
    private static OpenPosition[] FullForex() =>
        [Forex(Instruments.GbpUsd, "f1"), Forex(Instruments.UsdJpy, "f2"), Forex(Instruments.AudUsd, "f3")];

    [Fact]
    public async Task High_score_forex_trade_opens_on_a_new_market_when_every_slot_is_full()
    {
        var decision = await Evaluate(Bars.Proposal() with { Score = 92 }, Portfolio(positions: FullForex()));

        Assert.True(decision.IsApproved, decision.RejectionReason);
        Assert.Contains(decision.Checks, c => c is { Rule: "HighScoreOverride", Passed: true });
    }

    [Fact]
    public async Task High_score_forex_extra_works_across_market_types()
    {
        // The normal slots are full with a Derived trade among them; a high-score Forex trade still gets an extra.
        var positions = new[] { Derived(VolA, "d1"), Forex(Instruments.GbpUsd, "f1"), Forex(Instruments.UsdJpy, "f2") };
        var decision = await Evaluate(Bars.Proposal() with { Score = 90 }, Portfolio(positions: positions));

        Assert.True(decision.IsApproved, decision.RejectionReason);
    }

    [Fact]
    public async Task Below_the_override_score_the_normal_limit_applies()
    {
        var decision = await Evaluate(Bars.Proposal() with { Score = 89 }, Portfolio(positions: FullForex()));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c is { Rule: "OpenPositions", Passed: false });
        Assert.Contains(decision.Checks, c => c is { Rule: "HighScoreOverride", Passed: false });
    }

    [Fact]
    public async Task Forex_never_adds_a_second_trade_on_the_same_market()
    {
        var positions = new[] { Forex(Instruments.EurUsd, "f0"), Forex(Instruments.GbpUsd, "f1"), Forex(Instruments.UsdJpy, "f2") };
        var decision = await Evaluate(Bars.Proposal() with { Score = 99 }, Portfolio(positions: positions));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c is { Rule: "DuplicateInstrument", Passed: false });
    }

    [Fact]
    public async Task Extra_must_fit_in_the_remaining_daily_loss_budget()
    {
        // 3% of 10,000 = 300; 250 already lost today leaves 50, less than the three open trades already risk.
        var decision = await Evaluate(Bars.Proposal() with { Score = 95 }, Portfolio(dailyPnl: -250m, positions: FullForex()));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c.Rule == "HighScoreOverride" && !c.Passed && c.Detail.Contains("loss budget"));
    }

    [Fact]
    public async Task At_most_five_positions_are_open_at_once()
    {
        var positions = FullForex().Concat([Forex(Instruments.UsdCad, "f4"), Forex(Instruments.Get("EUR/GBP"), "f5")]).ToArray();
        var decision = await Evaluate(Bars.Proposal() with { Score = 99 }, Portfolio(positions: positions));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c.Rule == "OpenPositions" && !c.Passed && c.Detail.Contains("Maximum of 5"));
    }

    [Fact]
    public async Task Extras_are_one_shared_pool()
    {
        // One Derived extra in use (two Derived while Forex is open): one extra is left, which a Forex trade may take.
        var oneExtra = new[] { Derived(VolA, "d1"), Derived(VolB, "d2"), Forex(Instruments.GbpUsd, "f1"), Forex(Instruments.UsdJpy, "f2") };
        var forex = await Evaluate(Bars.Proposal() with { Score = 95 }, Portfolio(positions: oneExtra));
        Assert.True(forex.IsApproved, forex.RejectionReason);

        // Both extras in use by Derived: another high-score trade is refused although only four positions are open.
        var twoExtras = new[] { Derived(VolA, "d1"), Derived(VolB, "d2"), Derived(VolC, "d3"), Forex(Instruments.GbpUsd, "f1") };
        var derived = await Evaluate(DerivedProposal(VolD, score: 95), Portfolio(positions: twoExtras));
        Assert.False(derived.IsApproved);
        Assert.Contains(derived.Checks, c => c.Rule == "HighScoreOverride" && c.Detail.Contains("in use"));
    }

    [Fact]
    public async Task While_forex_is_closed_derived_may_use_every_slot()
    {
        var twoOpen = new[] { Derived(VolA, "d1", 20m), Derived(VolB, "d2", 20m) };

        var closed = await Evaluate(DerivedProposal(VolC), Portfolio(forexOpen: false, positions: twoOpen));
        var open = await Evaluate(DerivedProposal(VolC), Portfolio(forexOpen: true, positions: twoOpen));

        Assert.True(closed.IsApproved, closed.RejectionReason);
        Assert.DoesNotContain(closed.Checks, c => c.Rule == "HighScoreOverride");
        Assert.Contains(open.Checks, c => c is { Rule: "DerivedPositions", Passed: false });
    }

    [Fact]
    public async Task While_forex_is_closed_five_derived_trades_is_the_ceiling()
    {
        var three = new[] { Derived(VolA, "d1", 5m), Derived(VolB, "d2", 5m), Derived(VolC, "d3", 5m) };
        var fourth = await Evaluate(DerivedProposal(VolD, score: 80), Portfolio(forexOpen: false, positions: three));
        var fourthHighScore = await Evaluate(DerivedProposal(VolD, score: 95), Portfolio(forexOpen: false, positions: three));

        Assert.False(fourth.IsApproved);
        Assert.True(fourthHighScore.IsApproved, fourthHighScore.RejectionReason);
    }

    [Fact]
    public async Task Every_derived_trade_after_the_first_must_fit_the_derived_loss_budget()
    {
        // 1% of 10,000 = 100 Derived budget; 60 at risk in the open Derived trade + 50 for the new one is too much.
        var decision = await Evaluate(DerivedProposal(VolB), Portfolio(forexOpen: false, positions: [Derived(VolA, "d1", 60m)]));

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c is { Rule: "DerivedRiskBudget", Passed: false });
    }

    [Fact]
    public async Task When_forex_reopens_derived_trades_keep_running_and_forex_gets_the_free_slots()
    {
        var threeDerived = new[] { Derived(VolA, "d1"), Derived(VolB, "d2"), Derived(VolC, "d3") };

        var forex = await Evaluate(Bars.Proposal(), Portfolio(forexOpen: true, positions: threeDerived));
        var moreDerived = await Evaluate(DerivedProposal(VolD), Portfolio(forexOpen: true, positions: threeDerived));

        Assert.True(forex.IsApproved, forex.RejectionReason);
        Assert.Contains(forex.Checks, c => c.Rule == "OpenPositions" && c.Detail == "1/3 open.");
        Assert.False(moreDerived.IsApproved);
    }

    [Fact]
    public async Task Extras_can_be_turned_off()
    {
        var options = Options.Clone();
        options.MaxExtraDerivedPositions = 0;
        var risk = new RiskManager(options, new ExecutionCostOptions { SlippagePips = 0 });

        var decision = await risk.EvaluateAsync(Bars.Proposal() with { Score = 99 }, Portfolio(positions: FullForex()), CancellationToken.None);

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c.Rule == "HighScoreOverride" && c.Detail.Contains("off"));
    }
}
