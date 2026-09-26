using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Settings;

/// <summary>The risk limits that can be changed from the dashboard.</summary>
public sealed record RiskLimits(
    decimal MaxRiskPerTradePercent,
    decimal MaxDailyLossPercent,
    decimal MaxWeeklyLossPercent,
    int MaxOpenPositions,
    decimal MinRewardToRisk,
    int MaxConsecutiveLosses,
    int CooldownMinutes,
    int MaxCurrencyExposure,
    decimal MaxCommissionShareOfRisk,
    int? MaxDerivedOpenPositions = null,
    decimal? DerivedRiskPerTradePercent = null,
    decimal? MaxDerivedDailyLossPercent = null,
    decimal? AssumedCommissionPercent = null,
    int? MaxExtraDerivedPositions = null,
    int? HighScoreOverrideMinScore = null)
{
    public static RiskLimits From(RiskOptions o) => new(o.MaxRiskPerTradePercent, o.MaxDailyLossPercent, o.MaxWeeklyLossPercent,
        o.MaxOpenPositions, o.MinRewardToRisk, o.MaxConsecutiveLosses, o.CooldownMinutes, o.MaxCurrencyExposure, o.MaxCommissionShareOfRisk,
        o.MaxDerivedOpenPositions, o.DerivedRiskPerTradePercent, o.MaxDerivedDailyLossPercent, o.AssumedCommissionPercent,
        o.MaxExtraDerivedPositions, o.HighScoreOverrideMinScore);

    /// <summary>Limits saved before the Derived limits existed take the configured Derived defaults.</summary>
    public RiskLimits WithDefaultsFrom(RiskOptions defaults) => this with
    {
        MaxDerivedOpenPositions = MaxDerivedOpenPositions ?? defaults.MaxDerivedOpenPositions,
        DerivedRiskPerTradePercent = DerivedRiskPerTradePercent ?? defaults.DerivedRiskPerTradePercent,
        MaxDerivedDailyLossPercent = MaxDerivedDailyLossPercent ?? defaults.MaxDerivedDailyLossPercent,
        AssumedCommissionPercent = AssumedCommissionPercent ?? defaults.AssumedCommissionPercent,
        MaxExtraDerivedPositions = MaxExtraDerivedPositions ?? defaults.MaxExtraDerivedPositions,
        HighScoreOverrideMinScore = HighScoreOverrideMinScore ?? defaults.HighScoreOverrideMinScore
    };

    public RiskOptions ApplyTo(RiskOptions defaults)
    {
        var o = defaults.Clone();
        o.MaxRiskPerTradePercent = MaxRiskPerTradePercent;
        o.MaxDailyLossPercent = MaxDailyLossPercent;
        o.MaxWeeklyLossPercent = MaxWeeklyLossPercent;
        o.MaxOpenPositions = MaxOpenPositions;
        o.MinRewardToRisk = MinRewardToRisk;
        o.MaxConsecutiveLosses = MaxConsecutiveLosses;
        o.CooldownMinutes = CooldownMinutes;
        o.MaxCurrencyExposure = MaxCurrencyExposure;
        o.MaxCommissionShareOfRisk = MaxCommissionShareOfRisk;
        o.MaxDerivedOpenPositions = MaxDerivedOpenPositions ?? defaults.MaxDerivedOpenPositions;
        o.DerivedRiskPerTradePercent = DerivedRiskPerTradePercent ?? defaults.DerivedRiskPerTradePercent;
        o.MaxDerivedDailyLossPercent = MaxDerivedDailyLossPercent ?? defaults.MaxDerivedDailyLossPercent;
        o.AssumedCommissionPercent = AssumedCommissionPercent ?? defaults.AssumedCommissionPercent;
        o.MaxExtraDerivedPositions = MaxExtraDerivedPositions ?? defaults.MaxExtraDerivedPositions;
        o.HighScoreOverrideMinScore = HighScoreOverrideMinScore ?? defaults.HighScoreOverrideMinScore;
        return o;
    }

    /// <summary>
    /// Deliberately narrower than the configuration ranges: the dashboard can tune limits but not switch them off.
    /// </summary>
    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        void Check(bool ok, string field, string message)
        {
            if (!ok) errors[field] = message;
        }

        Check(MaxRiskPerTradePercent is >= 0.1m and <= 3m, nameof(MaxRiskPerTradePercent), "Risk per trade must be between 0.1% and 3%.");
        Check(MaxDailyLossPercent is >= 0.5m and <= 10m, nameof(MaxDailyLossPercent), "Daily loss limit must be between 0.5% and 10%.");
        Check(MaxWeeklyLossPercent is >= 1m and <= 25m, nameof(MaxWeeklyLossPercent), "Weekly loss limit must be between 1% and 25%.");
        Check(MaxDailyLossPercent >= MaxRiskPerTradePercent, nameof(MaxDailyLossPercent), "Daily loss limit must be at least the risk per trade.");
        Check(MaxWeeklyLossPercent >= MaxDailyLossPercent, nameof(MaxWeeklyLossPercent), "Weekly loss limit must be at least the daily limit.");
        Check(MaxOpenPositions is >= 1 and <= 10, nameof(MaxOpenPositions), "Open positions must be between 1 and 10.");
        Check(MinRewardToRisk is >= 1m and <= 5m, nameof(MinRewardToRisk), "Minimum reward:risk must be between 1 and 5.");
        Check(MaxConsecutiveLosses is >= 1 and <= 10, nameof(MaxConsecutiveLosses), "Consecutive losses must be between 1 and 10.");
        Check(CooldownMinutes is >= 0 and <= 1440, nameof(CooldownMinutes), "Cooldown must be between 0 and 1440 minutes.");
        Check(MaxCurrencyExposure is >= 1 and <= 5, nameof(MaxCurrencyExposure), "Currency exposure must be between 1 and 5.");
        Check(MaxCommissionShareOfRisk is >= 0.05m and <= 0.5m, nameof(MaxCommissionShareOfRisk), "Fee cap must be between 5% and 50% of the risk.");
        if (MaxDerivedOpenPositions is { } derivedPositions)
        {
            Check(derivedPositions >= 0 && derivedPositions <= MaxOpenPositions, nameof(MaxDerivedOpenPositions),
                "Derived positions must be between 0 (no Derived trading) and the total open-position limit.");
        }

        if (DerivedRiskPerTradePercent is { } derivedRisk)
        {
            Check(derivedRisk >= 0.1m && derivedRisk <= MaxRiskPerTradePercent, nameof(DerivedRiskPerTradePercent),
                "Derived risk per trade must be between 0.1% and the risk per trade.");
        }

        if (MaxExtraDerivedPositions is { } extra)
        {
            Check(extra >= 0 && extra <= 5, nameof(MaxExtraDerivedPositions), "High-score extra positions must be between 0 (off) and 5.");
        }

        if (HighScoreOverrideMinScore is { } minScore)
        {
            Check(minScore >= 75 && minScore <= 100, nameof(HighScoreOverrideMinScore), "Override score must be between 75 and 100.");
        }

        if (AssumedCommissionPercent is { } commission)
        {
            Check(commission >= 0m && commission <= 0.5m, nameof(AssumedCommissionPercent), "Assumed commission must be between 0% and 0.5% of the position value.");
        }

        if (MaxDerivedDailyLossPercent is { } derivedDaily)
        {
            Check(derivedDaily >= 0.1m && derivedDaily <= MaxDailyLossPercent, nameof(MaxDerivedDailyLossPercent),
                "Derived daily loss must be between 0.1% and the daily loss limit.");
        }
        return errors;
    }
}

public sealed record RiskSettingsView(RiskLimits Effective, RiskLimits Defaults, bool IsCustomized, int Version, DateTime? UpdatedAtUtc, string? UpdatedBy);

public sealed class RiskSettingsStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    private const int RowId = 1;

    public async Task<(RiskLimits? Limits, int Version, DateTime? UpdatedAtUtc, string? UpdatedBy)> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.RiskSettings.AsNoTracking().SingleOrDefaultAsync(r => r.Id == RowId, cancellationToken);
        return row is null
            ? (null, 0, null, null)
            : (row.Limits == "null" ? null : JsonSerializer.Deserialize<RiskLimits>(row.Limits), row.Version, row.UpdatedAtUtc, row.UpdatedBy);
    }

    /// <summary>Saves limits (null = back to configured defaults) and bumps the version.</summary>
    public async Task SaveAsync(RiskLimits? limits, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.RiskSettings.SingleOrDefaultAsync(r => r.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new RiskSettingsEntity { Id = RowId, Limits = "null" };
            db.RiskSettings.Add(row);
        }

        row.Limits = JsonSerializer.Serialize(limits);
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Risk limits in force: configured defaults overridden by the Settings page. <see cref="RefreshAsync"/> swaps in a
/// new immutable snapshot when the stored version changes; invalid stored values are ignored (defaults stay).
/// </summary>
public sealed class RiskOptionsSource(RiskOptions configured, RiskSettingsStore store, ILogger<RiskOptionsSource> logger) : IRiskOptionsSource
{
    private volatile RiskOptions _current = configured.Clone();
    private int _version = -1;

    public RiskOptions Current => _current;

    public RiskOptions Defaults => configured;

    public async Task<RiskSettingsView> RefreshAsync(CancellationToken cancellationToken)
    {
        var (limits, version, updatedAt, updatedBy) = await store.GetAsync(cancellationToken);
        if (version != _version)
        {
            limits = limits?.WithDefaultsFrom(configured);
            if (limits is not null && limits.Validate().Count > 0)
            {
                logger.LogError("Stored risk limits (version {Version}) are invalid; keeping configured defaults", version);
                limits = null;
            }

            _current = limits?.ApplyTo(configured) ?? configured.Clone();
            if (_version != -1)
            {
                logger.LogWarning("Risk limits changed (version {Version}): {Limits}", version, RiskLimits.From(_current));
            }

            _version = version;
        }

        return new RiskSettingsView(RiskLimits.From(_current), RiskLimits.From(configured), limits is not null, version, updatedAt, updatedBy);
    }
}
