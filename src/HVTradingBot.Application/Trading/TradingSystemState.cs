using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Risk;

namespace HVTradingBot.Application.Trading;

/// <summary>Durable system/risk state shared by the worker and the API.</summary>
public sealed record TradingSystemState
{
    public TradingMode Mode { get; init; } = TradingMode.Paper;
    public bool KillSwitchActive { get; init; }
    public string? KillSwitchReason { get; init; }
    public DateTime? KillSwitchChangedUtc { get; init; }
    public int ConsecutiveLosses { get; init; }
    public DateTime? CooldownUntilUtc { get; init; }
    public DateOnly? PnlDay { get; init; }
    public decimal DailyRealizedPnl { get; init; }
    public DateOnly? PnlWeekStart { get; init; }
    public decimal WeeklyRealizedPnl { get; init; }
    public DateTime? LastBarTimeUtc { get; init; }
    public DateTime? LastDataReceivedUtc { get; init; }
    public DateTime? WorkerHeartbeatUtc { get; init; }
    public string? BrokerName { get; init; }
    public string? BrokerAccountId { get; init; }
    public bool BrokerIsDemo { get; init; }
    public string? MarketDataSource { get; init; }

    /// <summary>Resets daily/weekly P&amp;L when market time crosses into a new day or ISO week (Monday start).</summary>
    public TradingSystemState RollPeriods(DateTime marketTimeUtc)
    {
        var day = DateOnly.FromDateTime(marketTimeUtc);
        var weekStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return this with
        {
            PnlDay = day,
            DailyRealizedPnl = PnlDay == day ? DailyRealizedPnl : 0,
            PnlWeekStart = weekStart,
            WeeklyRealizedPnl = PnlWeekStart == weekStart ? WeeklyRealizedPnl : 0
        };
    }

    /// <summary>Applies a closed trade: P&amp;L windows, loss streak and cooldown.</summary>
    public TradingSystemState WithClosedTrade(decimal realizedPnl, DateTime marketTimeUtc, RiskOptions options)
    {
        var rolled = RollPeriods(marketTimeUtc);
        var losses = realizedPnl < 0 ? rolled.ConsecutiveLosses + 1 : 0;
        var cooldown = rolled.CooldownUntilUtc;
        if (losses >= options.MaxConsecutiveLosses)
        {
            cooldown = marketTimeUtc.AddMinutes(options.CooldownMinutes);
            losses = 0;
        }

        return rolled with
        {
            DailyRealizedPnl = rolled.DailyRealizedPnl + realizedPnl,
            WeeklyRealizedPnl = rolled.WeeklyRealizedPnl + realizedPnl,
            ConsecutiveLosses = losses,
            CooldownUntilUtc = cooldown
        };
    }

    public TradingSystemState WithKillSwitch(bool active, string reason, DateTime nowUtc) =>
        this with { KillSwitchActive = active, KillSwitchReason = reason, KillSwitchChangedUtc = nowUtc };
}
