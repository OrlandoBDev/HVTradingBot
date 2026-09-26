namespace HVTradingBot.Domain.Risk;

/// <summary>Signal trades: setups sent to the user as signals and placed only when the user accepts them.</summary>
public static class SignalOrders
{
    /// <summary>Client order ids of signal trades start with this, so they are told apart from the bot's own trades.</summary>
    public const string Prefix = "SIG-";

    public static bool IsSignal(string clientOrderId) => clientOrderId.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>
/// Limits for signal trades, separate from the bot's: signal positions never use the bot's slots and their losses count
/// against their own daily budget, so accepting signals does not change what the bot may do.
/// </summary>
/// <param name="MaxOpenPositions">Signal trades that may be open at once.</param>
/// <param name="RiskPerTradePercent">Risk per signal trade as a percentage of equity (used for sizing).</param>
/// <param name="DailyLossLimitPercent">Signal trades pause for the day once their losses reach this share of the balance.</param>
/// <param name="AllowLossLimitOverride">Whether the user may accept a trade beyond the signal daily loss limit.</param>
public sealed record SignalLimits(int MaxOpenPositions, decimal RiskPerTradePercent, decimal DailyLossLimitPercent, bool AllowLossLimitOverride)
{
    /// <summary>Failed rules the user has seen and accepted; they pass when they may be overridden (see <see cref="RiskRuleKinds"/>).</summary>
    public IReadOnlySet<string> AcceptedRules { get; init; } = new HashSet<string>();
}

public enum RiskRuleKind
{
    /// <summary>Never overridable: protects against broken data, broken orders or the emergency stop.</summary>
    Hard,

    /// <summary>A loss limit: overridable only when the user has allowed it in the signal settings.</summary>
    LossLimit,

    /// <summary>A judgement call the user may accept for a signal trade.</summary>
    Soft
}

public static class RiskRuleKinds
{
    public const string AlreadyExecuted = "AlreadyExecuted";
    public const string SignalPositions = "SignalPositions";
    public const string SignalDailyLoss = "SignalDailyLoss";

    private static readonly HashSet<string> Hard = new(StringComparer.Ordinal)
    {
        "TradingMode", "KillSwitch", "MarketDataFreshness", "Tradable", "Sizing", AlreadyExecuted
    };

    private static readonly HashSet<string> LossLimits = new(StringComparer.Ordinal)
    {
        SignalDailyLoss, "DailyLoss", "WeeklyLoss", "DerivedDailyLoss"
    };

    public static RiskRuleKind Of(string rule) =>
        Hard.Contains(rule) ? RiskRuleKind.Hard : LossLimits.Contains(rule) ? RiskRuleKind.LossLimit : RiskRuleKind.Soft;

    public static bool MayOverride(string rule, SignalLimits limits) => Of(rule) switch
    {
        RiskRuleKind.Soft => true,
        RiskRuleKind.LossLimit => limits.AllowLossLimitOverride,
        _ => false
    };
}
