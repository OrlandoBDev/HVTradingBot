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
