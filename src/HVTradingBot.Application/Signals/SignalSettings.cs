using HVTradingBot.Domain.Risk;

namespace HVTradingBot.Application.Signals;

/// <summary>
/// How signals work, set on the dashboard. A signal is a setup sent to the user instead of (or in addition to) the
/// bot's automatic trading; it is traded only when the user accepts it, and never uses the bot's slots or loss budget.
/// </summary>
public sealed record SignalSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Markets whose setups are sent as signals instead of being traded automatically.</summary>
    public IReadOnlyList<string> SignalOnlyInstruments { get; init; } = [];

    /// <summary>Also send setups that score just below the automatic threshold, on any market.</summary>
    public bool NearMissEnabled { get; init; }

    public int NearMissMinScore { get; init; } = 60;

    public int MaxOpenPositions { get; init; } = 2;

    public decimal RiskPerTradePercent { get; init; } = 0.5m;

    public decimal DailyLossLimitPercent { get; init; } = 2m;

    /// <summary>A signal not acted on within this many minutes expires.</summary>
    public int ExpiryMinutes { get; init; } = 10;

    /// <summary>A signal is not traded once the price has moved this share of the way to its stop or target.</summary>
    public decimal MaxPriceMoveFraction { get; init; } = 0.33m;

    /// <summary>Show a "Trade" button on the phone notification (otherwise the notification opens the signal).</summary>
    public bool OneTapFromNotification { get; init; }

    /// <summary>Allow accepting a signal trade beyond the signal daily loss limit (needs a typed confirmation).</summary>
    public bool AllowLossLimitOverride { get; init; }

    /// <summary>No phone notifications between these local times (signals still appear on the Signals page).</summary>
    public TimeOnly? QuietHoursStart { get; init; }

    public TimeOnly? QuietHoursEnd { get; init; }

    /// <summary>IANA time zone for the quiet hours, sent by the dashboard that saved the settings.</summary>
    public string? TimeZone { get; init; }

    public SignalLimits ToLimits(IEnumerable<string>? acceptedRules = null) =>
        new(MaxOpenPositions, RiskPerTradePercent, DailyLossLimitPercent, AllowLossLimitOverride)
        {
            AcceptedRules = (acceptedRules ?? []).ToHashSet(StringComparer.Ordinal)
        };

    public bool IsSignalOnly(string instrument) => Enabled && SignalOnlyInstruments.Contains(instrument, StringComparer.Ordinal);

    /// <summary>True during the quiet hours (which may span midnight).</summary>
    public bool IsQuiet(DateTime utcNow)
    {
        if (QuietHoursStart is not { } start || QuietHoursEnd is not { } end || start == end)
        {
            return false;
        }

        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(TimeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out var found))
        {
            zone = found;
        }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));
        return start < end ? local >= start && local < end : local >= start || local < end;
    }

    /// <summary>Errors per field (camelCase), empty when valid.</summary>
    public Dictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (NearMissMinScore is < 40 or > 100) errors["nearMissMinScore"] = "Between 40 and 100.";
        if (MaxOpenPositions is < 0 or > 10) errors["maxOpenPositions"] = "Between 0 and 10.";
        if (RiskPerTradePercent is < 0.05m or > 2m) errors["riskPerTradePercent"] = "Between 0.05% and 2%.";
        if (DailyLossLimitPercent is < 0.1m or > 10m) errors["dailyLossLimitPercent"] = "Between 0.1% and 10%.";
        if (ExpiryMinutes is < 1 or > 120) errors["expiryMinutes"] = "Between 1 and 120 minutes.";
        if (MaxPriceMoveFraction is < 0.05m or > 1m) errors["maxPriceMoveFraction"] = "Between 0.05 and 1.";
        if ((QuietHoursStart is null) != (QuietHoursEnd is null)) errors["quietHoursEnd"] = "Set both start and end, or neither.";
        if (!string.IsNullOrWhiteSpace(TimeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _)) errors["timeZone"] = "Unknown time zone.";
        return errors;
    }
}

/// <summary>The current signal settings (refreshed from the database while the engine runs).</summary>
public interface ISignalSettingsSource
{
    SignalSettings Current { get; }
}

public sealed class FixedSignalSettings(SignalSettings settings) : ISignalSettingsSource
{
    public SignalSettings Current { get; } = settings;
}
