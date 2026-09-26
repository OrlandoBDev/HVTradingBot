using System.ComponentModel.DataAnnotations;

namespace HVTradingBot.Domain.Risk;

/// <summary>Risk limits. Defaults are the placeholders from docs/RISK_MANAGEMENT.md and must be configured deliberately.</summary>
public sealed class RiskOptions
{
    public RiskOptions Clone() => (RiskOptions)MemberwiseClone();

    [Range(0.01, 5)] public decimal MaxRiskPerTradePercent { get; set; } = 0.5m;
    [Range(0.1, 20)] public decimal MaxDailyLossPercent { get; set; } = 2m;
    [Range(0.1, 50)] public decimal MaxWeeklyLossPercent { get; set; } = 5m;
    [Range(1, 20)] public int MaxOpenPositions { get; set; } = 3;
    [Range(0.5, 10)] public decimal MinRewardToRisk { get; set; } = 2m;
    [Range(0.1, 50)] public decimal MaxSpreadPips { get; set; } = 3m;
    [Range(1, 10)] public decimal MaxSpreadToAverageMultiple { get; set; } = 2.5m;
    [Range(0, 20)] public decimal MaxSlippagePips { get; set; } = 1.5m;
    /// <summary>The stop must be at least this many spreads away, so costs cannot dominate the risk.</summary>
    [Range(1, 100)] public decimal MinStopSpreadMultiple { get; set; } = 5m;
    /// <summary>Maximum same-direction positions exposed to any single currency.</summary>
    [Range(1, 10)] public int MaxCurrencyExposure { get; set; } = 2;
    [Range(1, 20)] public int MaxConsecutiveLosses { get; set; } = 3;
    [Range(0, 10080)] public int CooldownMinutes { get; set; } = 240;
    [Range(1, 3600)] public int MaxMarketDataAgeSeconds { get; set; } = 30;
    [Range(1, 1_000_000)] public decimal MinUnits { get; set; } = 1_000m;
    /// <summary>Positions are rounded down to a multiple of this size (1,000 = micro lot; 1 for brokers that size by stake).</summary>
    [Range(1, 100_000)] public decimal UnitStep { get; set; } = 1_000m;
    [Range(1_000, 100_000_000)] public decimal MaxUnits { get; set; } = 1_000_000m;
    /// <summary>
    /// Derived (synthetic) markets: at most this many in the normal slots while Forex is open, so they cannot take the
    /// slots Forex needs. While Forex is closed Derived may use every slot (see <see cref="DerivedSlots"/>).
    /// </summary>
    [Range(0, 20)] public int MaxDerivedOpenPositions { get; set; } = 1;

    /// <summary>Derived (synthetic) markets: risk per trade (% of equity), typically lower than for Forex.</summary>
    [Range(0.01, 5)] public decimal DerivedRiskPerTradePercent { get; set; } = 0.5m;

    /// <summary>Derived (synthetic) markets: after losing this much in a day, only Derived trading pauses until the next day.</summary>
    [Range(0.1, 20)] public decimal MaxDerivedDailyLossPercent { get; set; } = 1m;

    /// <summary>
    /// High-score extras, shared by every market type (the name predates Forex extras and is kept so saved settings
    /// still apply). A candidate scoring at least <see cref="HighScoreOverrideMinScore"/> may open when the position
    /// limits are full, on a market with no open position (Derived may also add to a market it already trades), up to
    /// this many extra positions in total. An extra must fit in today's remaining loss budget (Derived: also the
    /// Derived budget) if every open trade and the new one hit their stops. 0 turns it off.
    /// </summary>
    [Range(0, 10)] public int MaxExtraDerivedPositions { get; set; } = 2;

    /// <summary>Most positions open at once: the normal limit plus the high-score extras.</summary>
    public int MaxTotalPositions => MaxOpenPositions + MaxExtraDerivedPositions;

    /// <summary>
    /// Derived positions allowed in the normal slots: <see cref="MaxDerivedOpenPositions"/> while Forex trades, so Derived
    /// cannot crowd it out; every slot while Forex is closed (weekends, the daily break).
    /// </summary>
    public int DerivedSlots(bool forexOpen) => forexOpen ? Math.Min(MaxDerivedOpenPositions, MaxOpenPositions) : MaxOpenPositions;

    /// <summary>Minimum score for the high-score override.</summary>
    [Range(75, 100)] public int HighScoreOverrideMinScore { get; set; } = 90;

    public static bool IsDerived(MarketData.Instrument instrument) => instrument.AssetClass == MarketData.AssetClass.SyntheticIndex;

    /// <summary>Risk per trade that applies to <paramref name="instrument"/>.</summary>
    public decimal RiskPercentFor(MarketData.Instrument instrument) =>
        IsDerived(instrument) ? Math.Min(DerivedRiskPerTradePercent, MaxRiskPerTradePercent) : MaxRiskPerTradePercent;

    /// <summary>
    /// Broker commission assumed per trade, round trip, as % of the position value. Used for position sizing, for
    /// paper trading and backtests, and for planning Deriv orders before Deriv quotes the exact commission.
    /// </summary>
    [Range(0, 1)] public decimal AssumedCommissionPercent { get; set; } = 0.05m;

    /// <summary>Orders are not sent when the broker's quoted commission exceeds this share of the amount at risk.</summary>
    [Range(0.01, 1)] public decimal MaxCommissionShareOfRisk { get; set; } = 0.25m;
    public bool KillSwitchOnDailyLossBreach { get; set; } = true;
    public bool KillSwitchOnStaleData { get; set; }
}

/// <summary>
/// The risk limits in force. Limits can change at runtime (Settings page); every evaluation reads one complete,
/// immutable snapshot so a single decision never mixes old and new values.
/// </summary>
public interface IRiskOptionsSource
{
    RiskOptions Current { get; }
}

public sealed class FixedRiskOptions(RiskOptions options) : IRiskOptionsSource
{
    public RiskOptions Current { get; } = options;
}
