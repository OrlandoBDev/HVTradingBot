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
    public decimal DerivedDailyRealizedPnl { get; init; }

    /// <summary>Signal trades' realized P&amp;L today: their own budget, apart from the bot's daily and weekly figures.</summary>
    public decimal SignalDailyRealizedPnl { get; init; }
    /// <summary>The trading capital in use (see <see cref="RiskOptions.TradingCapital"/>); null: the whole balance.</summary>
    public decimal? CapitalBase { get; init; }

    /// <summary>Realized P&amp;L of every app trade since <see cref="CapitalBase"/> was set.</summary>
    public decimal CapitalPnl { get; init; }

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
            DerivedDailyRealizedPnl = PnlDay == day ? DerivedDailyRealizedPnl : 0,
            SignalDailyRealizedPnl = PnlDay == day ? SignalDailyRealizedPnl : 0,
            PnlWeekStart = weekStart,
            WeeklyRealizedPnl = PnlWeekStart == weekStart ? WeeklyRealizedPnl : 0
        };
    }

    /// <summary>Applies a closed trade: P&amp;L windows, loss streak and cooldown.</summary>
    public TradingSystemState WithClosedTrade(decimal realizedPnl, DateTime marketTimeUtc, RiskOptions options, bool isDerived = false)
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
            DerivedDailyRealizedPnl = rolled.DerivedDailyRealizedPnl + (isDerived ? realizedPnl : 0),
            WeeklyRealizedPnl = rolled.WeeklyRealizedPnl + realizedPnl,
            CapitalPnl = rolled.CapitalPnl + realizedPnl,
            ConsecutiveLosses = losses,
            CooldownUntilUtc = cooldown
        };
    }

    /// <summary>A closed signal trade counts only against the signal budget: the bot's limits, streak and cooldown are untouched.</summary>
    public TradingSystemState WithClosedSignalTrade(decimal realizedPnl, DateTime marketTimeUtc)
    {
        var rolled = RollPeriods(marketTimeUtc);
        return rolled with
        {
            SignalDailyRealizedPnl = rolled.SignalDailyRealizedPnl + realizedPnl,
            CapitalPnl = rolled.CapitalPnl + realizedPnl
        };
    }

    /// <summary>A new trading capital starts fresh: results before it no longer count.</summary>
    public TradingSystemState WithCapital(decimal? capital) => this with { CapitalBase = capital, CapitalPnl = 0 };

    /// <summary>
    /// The balance the app sizes trades and loss limits on: the trading capital plus the app's results since it was set,
    /// never more than the real balance; the whole balance without one. <paramref name="configured"/> is the setting
    /// (it applies at once, also before the engine has started the new capital).
    /// </summary>
    public decimal SizingBalance(decimal accountBalance, decimal? configured) => configured switch
    {
        null => accountBalance,
        { } capital when capital != CapitalBase => Math.Clamp(capital, 0, accountBalance),
        { } capital => Math.Clamp(capital + CapitalPnl, 0, accountBalance)
    };

    public TradingSystemState WithKillSwitch(bool active, string reason, DateTime nowUtc) =>
        this with { KillSwitchActive = active, KillSwitchReason = reason, KillSwitchChangedUtc = nowUtc };
}
