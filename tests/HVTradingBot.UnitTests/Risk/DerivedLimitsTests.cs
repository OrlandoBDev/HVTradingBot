using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Risk;

public class DerivedLimitsTests
{
    private static readonly Instrument Vol75 = Instruments.Register(new Instrument("R_75X", "R_75X", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility 75 test"
    });

    private static readonly RiskOptions Options = new()
    {
        MaxRiskPerTradePercent = 1, MaxOpenPositions = 3, MaxDerivedOpenPositions = 1, DerivedRiskPerTradePercent = 0.5m,
        MaxDerivedDailyLossPercent = 1, MaxDailyLossPercent = 3, MinUnits = 1, UnitStep = 1
    };

    private readonly RiskManager _risk = new(Options, new ExecutionCostOptions { SlippagePips = 0 });

    private static TradeProposal DerivedProposal() =>
        new(Vol75, new TradeSetup(Direction.Long, 1000m, 990m, 1030m), new Quote(Vol75, Bars.Start, 999.9m, 1000.1m), 0.2m, "Test", 80, "R75-1");

    [Fact]
    public async Task Derived_trades_use_the_lower_risk_per_trade()
    {
        var derived = await _risk.EvaluateAsync(DerivedProposal(), Bars.Portfolio(balance: 10_000m), CancellationToken.None);
        var forex = await _risk.EvaluateAsync(Bars.Proposal(), Bars.Portfolio(balance: 10_000m), CancellationToken.None);

        Assert.True(derived.IsApproved, derived.RejectionReason);
        Assert.Equal(50m, derived.RiskAmount);          // 0.5% of 10,000 over a 10-point stop = 5 units
        Assert.True(forex.RiskAmount is > 95m and <= 100m); // 1% for Forex
    }

    [Fact]
    public async Task A_second_derived_position_is_refused_but_forex_still_fits()
    {
        var open = new[] { Bars.Position(Vol75, Direction.Long, entry: 1000m, stop: 990m, target: 1030m, units: 5, clientOrderId: "d1") };
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: open);

        var derived = await _risk.EvaluateAsync(DerivedProposal() with { ClientOrderId = "R75-2" }, portfolio, CancellationToken.None);
        var forex = await _risk.EvaluateAsync(Bars.Proposal(), portfolio, CancellationToken.None);

        Assert.Contains(derived.Checks, c => c.Rule == "DerivedPositions" && !c.Passed);
        Assert.True(forex.IsApproved, forex.RejectionReason);
    }

    private static readonly Instrument Vol10 = Instruments.Register(new Instrument("R_10X", "R_10X", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility 10 test"
    });

    private static OpenPosition OpenOn(Instrument instrument, string clientOrderId) =>
        Bars.Position(instrument, Direction.Long, entry: 1000m, stop: 990m, target: 1030m, units: 5, clientOrderId: clientOrderId);

    [Fact]
    public async Task Test_trade_may_use_a_derived_market_when_every_derived_slot_is_taken()
    {
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [OpenOn(Vol10, "d1")]);
        var test = DerivedProposal() with { Score = 0, ClientOrderId = "TEST-1", IsTestTrade = true };

        var decision = await _risk.EvaluateAsync(test, portfolio, CancellationToken.None);
        var normal = await _risk.EvaluateAsync(test with { IsTestTrade = false }, portfolio, CancellationToken.None);

        Assert.True(decision.IsApproved, decision.RejectionReason);
        Assert.DoesNotContain(decision.Checks, c => c.Rule == "HighScoreOverride");
        Assert.False(normal.IsApproved); // the same trade from a strategy stays blocked
    }

    [Fact]
    public async Task Test_trade_still_obeys_the_other_rules()
    {
        // Three open positions (one Derived, two Forex) fill MaxOpenPositions = 3.
        var full = Bars.Portfolio(balance: 10_000m, positions:
        [
            OpenOn(Vol10, "d1"),
            Bars.Position(Instruments.GbpUsd, Direction.Long, entry: 1.25m, stop: 1.24m, target: 1.28m, units: 1000, clientOrderId: "f1"),
            Bars.Position(Instruments.UsdJpy, Direction.Long, entry: 150m, stop: 149m, target: 153m, units: 1000, clientOrderId: "f2")
        ]);
        var test = DerivedProposal() with { Score = 0, ClientOrderId = "TEST-2", IsTestTrade = true };

        var tooMany = await _risk.EvaluateAsync(test, full, CancellationToken.None);
        var killed = await _risk.EvaluateAsync(test, Bars.Portfolio(balance: 10_000m) with { KillSwitchActive = true, KillSwitchReason = "manual" },
            CancellationToken.None);

        Assert.Contains(tooMany.Checks, c => c.Rule == "OpenPositions" && !c.Passed);
        Assert.Contains(killed.Checks, c => c.Rule == "KillSwitch" && !c.Passed);
    }

    private static OpenPosition OpenDerived(string clientOrderId, decimal risk = 50m) =>
        Bars.Position(Vol75, Direction.Long, entry: 1000m, stop: 990m, target: 1030m, units: 5, clientOrderId: clientOrderId) with { InitialRiskAmount = risk };

    private static TradeProposal HighScore(string clientOrderId) => DerivedProposal() with { Score = 90, ClientOrderId = clientOrderId };

    [Fact]
    public async Task High_score_derived_trade_is_added_on_the_same_market_without_using_a_forex_slot()
    {
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1")]);

        var extra = await _risk.EvaluateAsync(HighScore("R75-2"), portfolio, CancellationToken.None);

        Assert.True(extra.IsApproved, extra.RejectionReason);
        Assert.Contains(extra.Checks, c => c.Rule == "HighScoreOverride" && c.Passed);

        // With the extra open, Forex still has two of its three slots.
        var withExtra = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1"), OpenDerived("d2")]);
        var forex = await _risk.EvaluateAsync(Bars.Proposal(), withExtra, CancellationToken.None);
        Assert.True(forex.IsApproved, forex.RejectionReason);
        Assert.Contains(forex.Checks, c => c.Rule == "OpenPositions" && c.Detail == "1/3 open.");
    }

    [Fact]
    public async Task High_score_extra_must_fit_in_the_remaining_derived_loss_budget()
    {
        // 1% of 10,000 = 100 budget, 50 already lost today, 50 at risk in the open position: no room for another.
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1")]) with { DerivedDailyRealizedPnl = -50m };

        var extra = await _risk.EvaluateAsync(HighScore("R75-2"), portfolio, CancellationToken.None);

        Assert.False(extra.IsApproved);
        Assert.Contains(extra.Checks, c => c.Rule == "HighScoreOverride" && !c.Passed);
    }

    [Fact]
    public async Task High_score_extras_are_capped_and_can_be_turned_off()
    {
        var options = Options.Clone();
        options.MaxExtraDerivedPositions = 1;
        var risk = new RiskManager(options, new ExecutionCostOptions { SlippagePips = 0 });
        var full = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1", 10m), OpenDerived("d2", 10m)]);
        Assert.Contains((await risk.EvaluateAsync(HighScore("R75-3"), full, CancellationToken.None)).Checks, c => c.Rule == "HighScoreOverride" && !c.Passed);

        options.MaxExtraDerivedPositions = 0;
        var one = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1", 10m)]);
        Assert.False((await risk.EvaluateAsync(HighScore("R75-2"), one, CancellationToken.None)).IsApproved);
    }

    [Fact]
    public async Task Low_score_derived_and_high_score_forex_keep_the_normal_rules()
    {
        var derivedOpen = Bars.Portfolio(balance: 10_000m, positions: [OpenDerived("d1")]);
        var lowScore = await _risk.EvaluateAsync(DerivedProposal() with { ClientOrderId = "R75-2" }, derivedOpen, CancellationToken.None);
        Assert.Contains(lowScore.Checks, c => c.Rule == "HighScoreOverride" && !c.Passed);

        var forexOpen = Bars.Portfolio(balance: 10_000m, positions: [Bars.Position(Instruments.EurUsd, Direction.Long, units: 1000m)]);
        var forex = await _risk.EvaluateAsync(Bars.Proposal() with { Score = 95 }, forexOpen, CancellationToken.None);
        Assert.Contains(forex.Checks, c => c.Rule == "DuplicateInstrument" && !c.Passed);
        Assert.DoesNotContain(forex.Checks, c => c.Rule == "HighScoreOverride");
    }

    [Fact]
    public async Task Derived_daily_loss_pauses_only_derived()
    {
        var portfolio = Bars.Portfolio(balance: 10_000m, dailyPnl: -100m) with { DerivedDailyRealizedPnl = -100m };

        var derived = await _risk.EvaluateAsync(DerivedProposal(), portfolio, CancellationToken.None);
        var forex = await _risk.EvaluateAsync(Bars.Proposal(), portfolio, CancellationToken.None);

        Assert.Contains(derived.Checks, c => c.Rule == "DerivedDailyLoss" && !c.Passed);
        Assert.True(forex.IsApproved, forex.RejectionReason);
    }

    [Fact]
    public void Derived_losses_are_tracked_per_day()
    {
        var day1 = Bars.Start.AddHours(12);
        var state = new TradingSystemState()
            .WithClosedTrade(-40m, day1, Options, isDerived: true)
            .WithClosedTrade(-30m, day1, Options, isDerived: false);

        Assert.Equal(-40m, state.DerivedDailyRealizedPnl);
        Assert.Equal(-70m, state.DailyRealizedPnl);
        Assert.Equal(0m, state.RollPeriods(day1.AddDays(1)).DerivedDailyRealizedPnl);
    }

    [Theory]
    [InlineData(4, 0.5, 1, "MaxDerivedOpenPositions")]   // more Derived slots than total positions
    [InlineData(1, 2.0, 1, "DerivedRiskPerTradePercent")] // Derived risk above the normal risk per trade
    [InlineData(1, 0.5, 5, "MaxDerivedDailyLossPercent")] // Derived daily loss above the overall daily limit
    public void Derived_limits_stay_inside_the_overall_limits(int positions, double risk, double daily, string field)
    {
        var limits = new RiskLimits(1, 3, 8, 3, 2, 3, 240, 2, 0.3m, positions, (decimal)risk, (decimal)daily);
        Assert.Contains(field, limits.Validate().Keys);
    }

    [Fact]
    public void Limits_saved_before_derived_settings_existed_get_the_defaults()
    {
        var old = new RiskLimits(1, 3, 8, 3, 2, 3, 240, 2, 0.3m);
        var applied = old.WithDefaultsFrom(Options).ApplyTo(Options);

        Assert.Equal(1, applied.MaxDerivedOpenPositions);
        Assert.Equal(0.5m, applied.DerivedRiskPerTradePercent);
        Assert.Empty(old.WithDefaultsFrom(Options).Validate());
    }
}
