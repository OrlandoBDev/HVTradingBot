using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Trading;

public class TradingSystemStateTests
{
    private readonly RiskOptions _risk = new() { MaxConsecutiveLosses = 3, CooldownMinutes = 60 };

    [Fact]
    public void Default_state_is_paper_mode_with_kill_switch_off()
    {
        var state = new TradingSystemState();
        Assert.Equal(HVTradingBot.Domain.Common.TradingMode.Paper, state.Mode);
        Assert.False(state.KillSwitchActive);
    }

    [Fact]
    public void Daily_pnl_resets_on_a_new_day_but_weekly_accumulates()
    {
        var monday = Bars.Start.AddHours(10);
        var state = new TradingSystemState()
            .WithClosedTrade(-100, monday, _risk)
            .WithClosedTrade(-50, monday.AddDays(1), _risk);

        Assert.Equal(-50, state.DailyRealizedPnl);
        Assert.Equal(-150, state.WeeklyRealizedPnl);
    }

    [Fact]
    public void Weekly_pnl_resets_on_monday()
    {
        var friday = Bars.Start.AddDays(4);
        var state = new TradingSystemState().WithClosedTrade(-100, friday, _risk).RollPeriods(friday.AddDays(3));
        Assert.Equal(0, state.WeeklyRealizedPnl);
    }

    [Fact]
    public void Consecutive_losses_trigger_cooldown_and_a_win_resets_the_streak()
    {
        var t = Bars.Start;
        var state = new TradingSystemState().WithClosedTrade(-1, t, _risk).WithClosedTrade(-1, t, _risk);
        Assert.Equal(2, state.ConsecutiveLosses);
        Assert.Null(state.CooldownUntilUtc);

        Assert.Equal(0, state.WithClosedTrade(5, t, _risk).ConsecutiveLosses);

        var cooled = state.WithClosedTrade(-1, t, _risk);
        Assert.Equal(t.AddMinutes(60), cooled.CooldownUntilUtc);
    }

    [Fact]
    public void Stale_detection_uses_wall_clock_age()
    {
        var status = new MarketDataStatus(Bars.Start, Bars.Start);
        Assert.False(status.IsStale(Bars.Start.AddSeconds(29), TimeSpan.FromSeconds(30)));
        Assert.True(status.IsStale(Bars.Start.AddSeconds(31), TimeSpan.FromSeconds(30)));
        Assert.True(new MarketDataStatus(null, null).IsStale(Bars.Start, TimeSpan.FromSeconds(30)));
    }
}
