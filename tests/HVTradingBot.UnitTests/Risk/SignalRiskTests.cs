using HVTradingBot.Application.Signals;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Risk;

/// <summary>Signals use their own slots and loss budget, and the user may accept failed soft rules but never hard ones.</summary>
public class SignalRiskTests
{
    private static readonly RiskOptions Options = new()
    {
        MaxRiskPerTradePercent = 1, MaxOpenPositions = 2, MaxDailyLossPercent = 3, MaxExtraDerivedPositions = 0, MinUnits = 1, UnitStep = 1
    };

    private readonly RiskManager _risk = new(Options, new ExecutionCostOptions { SlippagePips = 0 });

    private static TradeProposal Signal(string instrument = "EUR/USD", IEnumerable<string>? accepted = null, bool allowLossLimit = false,
        decimal rr = 2.5m)
    {
        var proposal = Bars.Proposal(Instruments.Get(instrument), rr: rr, clientOrderId: SignalOrders.Prefix + instrument + "-1");
        return proposal with
        {
            Signal = new SignalSettings { MaxOpenPositions = 1, RiskPerTradePercent = 0.5m, DailyLossLimitPercent = 2, AllowLossLimitOverride = allowLossLimit }
                .ToLimits(accepted)
        };
    }

    private static OpenPosition Bot(string instrument, string id) =>
        Bars.Position(Instruments.Get(instrument), Direction.Long, entry: 1.1m, stop: 1.098m, target: 1.105m, units: 1000, clientOrderId: id);

    [Fact]
    public async Task A_signal_trades_when_the_bot_slots_are_full_and_is_sized_with_the_signal_risk()
    {
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [Bot("GBP/USD", "b1"), Bot("USD/JPY", "b2")]);

        var signal = await _risk.EvaluateAsync(Signal(), portfolio, CancellationToken.None);
        var bot = await _risk.EvaluateAsync(Bars.Proposal(), portfolio, CancellationToken.None);

        Assert.True(signal.IsApproved, signal.RejectionReason);
        Assert.True(signal.RiskAmount is > 45m and <= 50m, $"risk {signal.RiskAmount}"); // 0.5% of 10,000
        Assert.Contains(bot.Checks, c => c.Rule == "OpenPositions" && !c.Passed);
    }

    [Fact]
    public async Task Signal_trades_do_not_take_the_bot_slots()
    {
        var signalTrade = Bot("GBP/USD", SignalOrders.Prefix + "x");
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [signalTrade, Bot("USD/JPY", "b1")]);

        var bot = await _risk.EvaluateAsync(Bars.Proposal(), portfolio, CancellationToken.None);
        var second = await _risk.EvaluateAsync(Signal("AUD/USD"), portfolio, CancellationToken.None);

        Assert.True(bot.IsApproved, bot.RejectionReason); // one bot position of two
        Assert.Contains(second.Checks, c => c.Rule == RiskRuleKinds.SignalPositions && !c.Passed); // signal slots: 1
    }

    [Fact]
    public async Task Soft_failures_can_be_accepted_and_are_marked_overridden()
    {
        var weak = Signal(rr: 1.2m);
        var rejected = await _risk.EvaluateAsync(weak, Bars.Portfolio(), CancellationToken.None);
        var accepted = await _risk.EvaluateAsync(Signal(rr: 1.2m, accepted: ["RewardToRisk"]), Bars.Portfolio(), CancellationToken.None);

        Assert.False(rejected.IsApproved);
        Assert.Equal(RiskRuleKind.Soft, RiskRuleKinds.Of("RewardToRisk"));
        Assert.True(accepted.IsApproved, accepted.RejectionReason);
        var check = Assert.Single(accepted.Checks, c => c.Rule == "RewardToRisk");
        Assert.True(check.Overridden);
        Assert.StartsWith("Accepted by you", check.Detail);
    }

    [Fact]
    public async Task Hard_rules_block_even_when_accepted()
    {
        var decision = await _risk.EvaluateAsync(Signal(accepted: ["KillSwitch"]), Bars.Portfolio(killSwitch: true), CancellationToken.None);

        Assert.False(decision.IsApproved);
        Assert.Contains(decision.Checks, c => c.Rule == "KillSwitch" && !c.Passed && !c.Overridden);
        Assert.False(RiskRuleKinds.MayOverride("KillSwitch", Signal().Signal!));
    }

    [Fact]
    public async Task The_signal_loss_budget_blocks_unless_overrides_are_allowed()
    {
        var lost = Bars.Portfolio(balance: 10_000m) with { SignalDailyRealizedPnl = -250m }; // 2% of 10,000 is 200

        var blocked = await _risk.EvaluateAsync(Signal(accepted: [RiskRuleKinds.SignalDailyLoss]), lost, CancellationToken.None);
        var allowed = await _risk.EvaluateAsync(Signal(accepted: [RiskRuleKinds.SignalDailyLoss], allowLossLimit: true), lost, CancellationToken.None);

        Assert.False(blocked.IsApproved);
        Assert.True(allowed.IsApproved, allowed.RejectionReason);
        Assert.Contains(allowed.Checks, c => c.Rule == RiskRuleKinds.SignalDailyLoss && c.Overridden);
    }

    [Fact]
    public async Task The_bot_loss_limit_does_not_stop_signals_and_signal_losses_do_not_stop_the_bot()
    {
        var botLost = Bars.Portfolio(balance: 10_000m, dailyPnl: -400m); // bot limit 3% = 300
        var signalsLost = Bars.Portfolio(balance: 10_000m) with { SignalDailyRealizedPnl = -250m };

        Assert.True((await _risk.EvaluateAsync(Signal(), botLost, CancellationToken.None)).IsApproved);
        Assert.True((await _risk.EvaluateAsync(Bars.Proposal(), signalsLost, CancellationToken.None)).IsApproved);
    }

    [Fact]
    public async Task A_signal_on_a_market_with_an_open_position_needs_the_user_to_accept_it()
    {
        var portfolio = Bars.Portfolio(balance: 10_000m, positions: [Bot("EUR/USD", "b1")]);

        var refused = await _risk.EvaluateAsync(Signal(), portfolio, CancellationToken.None);
        var accepted = await _risk.EvaluateAsync(Signal(accepted: ["DuplicateInstrument", "CurrencyExposure"]), portfolio, CancellationToken.None);

        Assert.Contains(refused.Checks, c => c.Rule == "DuplicateInstrument" && !c.Passed);
        Assert.True(accepted.IsApproved, accepted.RejectionReason);
    }

    [Fact]
    public void Quiet_hours_may_span_midnight()
    {
        var settings = new SignalSettings { QuietHoursStart = new TimeOnly(22, 0), QuietHoursEnd = new TimeOnly(7, 0), TimeZone = "UTC" };

        Assert.True(settings.IsQuiet(new DateTime(2026, 1, 5, 23, 30, 0, DateTimeKind.Utc)));
        Assert.True(settings.IsQuiet(new DateTime(2026, 1, 5, 6, 59, 0, DateTimeKind.Utc)));
        Assert.False(settings.IsQuiet(new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc)));
        Assert.False(new SignalSettings().IsQuiet(DateTime.UtcNow));
    }

    [Fact]
    public void Settings_are_validated()
    {
        var errors = new SignalSettings { RiskPerTradePercent = 5, ExpiryMinutes = 0, QuietHoursStart = new TimeOnly(22, 0) }.Validate();

        Assert.Contains("riskPerTradePercent", errors.Keys);
        Assert.Contains("expiryMinutes", errors.Keys);
        Assert.Contains("quietHoursEnd", errors.Keys);
        Assert.Empty(new SignalSettings().Validate());
    }
}
