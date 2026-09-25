using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Risk;

public class RiskManagerTests
{
    private readonly RiskOptions _options = new();
    private readonly RiskManager _risk = new(new RiskOptions(), new ExecutionCostOptions());

    private Task<RiskDecision> Evaluate(TradeProposal? proposal = null, PortfolioState? portfolio = null) =>
        _risk.EvaluateAsync(proposal ?? Bars.Proposal(), portfolio ?? Bars.Portfolio(), CancellationToken.None);

    private static void AssertRejectedBy(RiskDecision decision, string rule)
    {
        Assert.False(decision.IsApproved);
        Assert.Equal(0, decision.Units);
        Assert.Contains(decision.Checks, c => c.Rule == rule && !c.Passed);
        Assert.Contains(rule, decision.RejectionReason);
    }

    [Fact]
    public async Task Valid_proposal_is_approved_and_risk_stays_within_limit()
    {
        var decision = await Evaluate();

        Assert.True(decision.IsApproved, decision.RejectionReason);
        Assert.True(decision.Units >= _options.MinUnits);
        Assert.True(decision.RiskAmount <= 100_000m * _options.MaxRiskPerTradePercent / 100m);
        Assert.Equal(0, decision.Units % 1000);
    }

    [Fact]
    public async Task Position_size_for_20_pip_stop_risks_about_half_a_percent()
    {
        // Loss per unit = 20.2 pips incl. slippage (0.00202) + 0.05% commission on 1.1000 (0.00055) = 0.00257
        // 500 USD / 0.00257 = 194,552 -> 194,000 units
        var decision = await Evaluate();
        Assert.Equal(194_000m, decision.Units);
        Assert.Equal(498.58m, decision.RiskAmount);
    }

    [Fact]
    public async Task Jpy_quoted_pair_is_converted_to_account_currency()
    {
        var proposal = Bars.Proposal(Instruments.UsdJpy, entry: 150m, stopPips: 20);
        var decision = await Evaluate(proposal);

        Assert.True(decision.IsApproved, decision.RejectionReason);
        // Loss per unit = 0.202 JPY / 150 + 0.05% of 150 JPY / 150 = 0.0018467 USD -> 500 / that = 270,758 -> 270,000
        Assert.Equal(270_000m, decision.Units);
        Assert.True(decision.RiskAmount <= 500m);
    }

    [Fact]
    public async Task Kill_switch_blocks_trading() => AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(killSwitch: true)), "KillSwitch");

    [Fact]
    public async Task Stale_market_data_blocks_trading() =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(lastDataReceived: Bars.Start.AddMinutes(-5))), "MarketDataFreshness");

    [Fact]
    public async Task Reward_to_risk_below_minimum_is_rejected() =>
        AssertRejectedBy(await Evaluate(Bars.Proposal(rr: 1.5m)), "RewardToRisk");

    [Fact]
    public async Task Wide_spread_is_rejected() =>
        AssertRejectedBy(await Evaluate(Bars.Proposal(spread: 0.0005m, averageSpread: 0.0005m)), "Spread");

    [Fact]
    public async Task Abnormal_spread_versus_average_is_rejected() =>
        AssertRejectedBy(await Evaluate(Bars.Proposal(spread: 0.00025m, averageSpread: 0.00008m)), "Spread");

    [Fact]
    public async Task Max_open_positions_is_enforced()
    {
        var positions = new[]
        {
            Bars.Position(Instruments.GbpUsd, Direction.Long, clientOrderId: "a"),
            Bars.Position(Instruments.UsdJpy, Direction.Long, clientOrderId: "b"),
            Bars.Position(Instruments.AudUsd, Direction.Short, clientOrderId: "c")
        };
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(positions: positions)), "OpenPositions");
    }

    [Fact]
    public async Task Second_position_on_same_instrument_is_rejected() =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(positions: [Bars.Position(Instruments.EurUsd, Direction.Short)])), "DuplicateInstrument");

    [Fact]
    public async Task Daily_loss_limit_blocks_new_trades() =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(dailyPnl: -2_000m)), "DailyLoss");

    [Fact]
    public async Task Weekly_loss_limit_blocks_new_trades() =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(weeklyPnl: -5_000m)), "WeeklyLoss");

    [Fact]
    public async Task Cooldown_after_consecutive_losses_blocks_trading() =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(cooldownUntil: Bars.Start.AddHours(1))), "Cooldown");

    [Fact]
    public async Task Correlated_currency_exposure_is_limited()
    {
        // Long GBP/USD and long AUD/USD are both short USD; a long EUR/USD would make USD exposure -3.
        var positions = new[]
        {
            Bars.Position(Instruments.GbpUsd, Direction.Long, clientOrderId: "a"),
            Bars.Position(Instruments.AudUsd, Direction.Long, clientOrderId: "b")
        };
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(positions: positions)), "CurrencyExposure");
    }

    [Theory]
    [InlineData(TradingMode.Approval)]
    [InlineData(TradingMode.Auto)]
    public async Task Only_paper_mode_can_execute(TradingMode mode) =>
        AssertRejectedBy(await Evaluate(portfolio: Bars.Portfolio(mode: mode)), "TradingMode");

    [Fact]
    public async Task Stop_too_wide_for_budget_is_rejected() =>
        AssertRejectedBy(await Evaluate(Bars.Proposal(stopPips: 6000)), "Sizing");

    [Fact]
    public async Task All_failing_rules_are_reported_not_just_the_first()
    {
        var decision = await Evaluate(Bars.Proposal(rr: 1m), Bars.Portfolio(killSwitch: true, dailyPnl: -5_000m));
        Assert.True(decision.Checks.Count(c => !c.Passed) >= 3);
    }
}
