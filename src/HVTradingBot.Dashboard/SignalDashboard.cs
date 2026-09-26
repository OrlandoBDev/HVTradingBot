using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Signals;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Dashboard;

/// <summary>
/// The Signals page: signals the bot sent, the user's decisions (trade, with any risks accepted, or skip), results and
/// settings. Accepting only records the decision; the trading worker places the trade.
/// </summary>
public sealed class SignalDashboard(
    SignalStore store,
    SignalSettingsStore settingsStore,
    SignalSettingsSource settingsSource,
    IDbContextFactory<TradingDbContext> dbFactory,
    IDecisionJournal journal,
    ILogger<SignalDashboard> logger)
{
    /// <summary>What the user types to accept going past a loss limit.</summary>
    public const string LossLimitConfirmation = "ACCEPT";

    public async Task<PagedResult<SignalDto>> ListAsync(bool activeOnly, int? page, int? pageSize, CancellationToken ct)
    {
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var settings = await settingsSource.RefreshAsync(ct);
        var (items, total) = await store.ListAsync(activeOnly, p, size, ct);
        var results = await ResultsAsync(items, ct);
        return new PagedResult<SignalDto>(items.Select(s => ToDto(s, settings, results.GetValueOrDefault(s.Id))).ToList(), total, p, size);
    }

    public async Task<SignalDto?> GetAsync(Guid id, CancellationToken ct)
    {
        if (await store.GetAsync(id, ct) is not { } signal)
        {
            return null;
        }

        var results = await ResultsAsync([signal], ct);
        return ToDto(signal, await settingsSource.RefreshAsync(ct), results.GetValueOrDefault(signal.Id));
    }

    public async Task<DashboardResult> AcceptAsync(Guid id, AcceptSignalRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var settings = await settingsSource.RefreshAsync(ct);
        var limits = settings.ToLimits();
        var accepted = (request.AcceptedRules ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        foreach (var rule in accepted)
        {
            switch (RiskRuleKinds.Of(rule))
            {
                case RiskRuleKind.Hard:
                    return DashboardResult.Invalid("acceptedRules", $"{rule} is a safety rule and cannot be accepted.");
                case RiskRuleKind.LossLimit when !RiskRuleKinds.MayOverride(rule, limits):
                    return DashboardResult.Invalid("acceptedRules",
                        $"{rule} is a loss limit. Allow loss-limit overrides in Signal settings to accept it.");
                case RiskRuleKind.LossLimit when !string.Equals(request.Confirmation?.Trim(), LossLimitConfirmation, StringComparison.Ordinal):
                    return DashboardResult.Invalid("confirmation", $"Type {LossLimitConfirmation} to trade past a loss limit.");
            }
        }

        var problem = await store.AcceptAsync(id, accepted, caller.Actor, ct);
        if (problem == "Signal not found.")
        {
            return DashboardResult.NotFound;
        }

        if (problem is not null)
        {
            return DashboardResult.Invalid("signal", problem);
        }

        var signal = (await store.GetAsync(id, ct))!;
        await journal.RecordAuditAsync(caller.Actor, "SignalAccepted",
            $"{signal.Instrument} {signal.Direction} ({signal.Strategy}, score {signal.Score})" +
            (accepted.Count > 0 ? $"; risks accepted: {string.Join(", ", accepted)}" : ""), caller.CorrelationId, ct);
        logger.LogInformation("Signal {SignalId} accepted by {Actor}; accepted risks: {Rules}", id, caller.Actor, accepted);
        return DashboardResult.Ok(ToDto(signal, settings, null));
    }

    public async Task<DashboardResult> SkipAsync(Guid id, DashboardCaller caller, CancellationToken ct)
    {
        var problem = await store.SkipAsync(id, caller.Actor, ct);
        if (problem == "Signal not found.")
        {
            return DashboardResult.NotFound;
        }

        if (problem is not null)
        {
            return DashboardResult.Invalid("signal", problem);
        }

        var signal = (await store.GetAsync(id, ct))!;
        await journal.RecordAuditAsync(caller.Actor, "SignalSkipped", $"{signal.Instrument} {signal.Direction} ({signal.Strategy}, score {signal.Score})",
            caller.CorrelationId, ct);
        return DashboardResult.Ok(ToDto(signal, await settingsSource.RefreshAsync(ct), null));
    }

    /// <summary>Taken signals (overridden ones separately) and what the skipped and expired ones would have made.</summary>
    public async Task<SignalStatsDto> GetStatsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var counts = await db.Signals.AsNoTracking().GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(params string[] statuses) => counts.Where(c => statuses.Contains(c.Key)).Sum(c => c.Count);

        var placed = await db.Signals.AsNoTracking().Where(s => s.Status == SignalStatus.Placed && s.PositionId != null)
            .Select(s => new { s.PositionId, s.Checks }).ToListAsync(ct);
        var positionIds = placed.Select(s => s.PositionId!.Value).ToList();
        var closed = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id) && !p.IsOpen)
            .ToDictionaryAsync(p => p.Id, p => new { Pnl = p.RealizedPnl ?? 0, R = p.RMultiple ?? 0 }, ct);
        var overridden = placed.Where(s => SignalStore.ReadChecks(s.Checks).Any(c => c.Overridden)).Select(s => s.PositionId!.Value).ToHashSet();

        var missed = await db.Signals.AsNoTracking().Where(s => s.Status == SignalStatus.Skipped || s.Status == SignalStatus.Expired)
            .Join(db.SetupOutcomes.AsNoTracking().Where(o => o.RMultiple != null), s => s.SetupId, o => o.SetupId, (s, o) => o.RMultiple!.Value)
            .ToListAsync(ct);

        return new SignalStatsDto(
            Count(SignalStatus.Active),
            placed.Count,
            closed.Count,
            closed.Values.Count(c => c.Pnl > 0),
            closed.Values.Sum(c => c.Pnl),
            overridden.Count,
            closed.Where(c => overridden.Contains(c.Key)).Sum(c => c.Value.Pnl),
            Count(SignalStatus.Skipped),
            Count(SignalStatus.Expired),
            missed.Count,
            missed.Count(r => r > 0),
            Math.Round(missed.Sum(), 2),
            Math.Round(closed.Values.Sum(c => c.R), 2));
    }

    public async Task<SignalSettingsDto> GetSettingsAsync(CancellationToken ct) => ToDto(await settingsStore.GetAsync(ct));

    public async Task<DashboardResult> SaveSettingsAsync(SignalSettingsDto request, DashboardCaller caller, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var start = ParseTime(request.QuietHoursStart, "quietHoursStart", errors);
        var end = ParseTime(request.QuietHoursEnd, "quietHoursEnd", errors);
        var settings = new SignalSettings
        {
            Enabled = request.Enabled,
            SignalOnlyInstruments = (request.SignalOnlyInstruments ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().ToList(),
            NearMissEnabled = request.NearMissEnabled,
            NearMissMinScore = request.NearMissMinScore,
            MaxOpenPositions = request.MaxOpenPositions,
            RiskPerTradePercent = request.RiskPerTradePercent,
            DailyLossLimitPercent = request.DailyLossLimitPercent,
            ExpiryMinutes = request.ExpiryMinutes,
            MaxPriceMoveFraction = request.MaxPriceMoveFraction,
            OneTapFromNotification = request.OneTapFromNotification,
            AllowLossLimitOverride = request.AllowLossLimitOverride,
            QuietHoursStart = start,
            QuietHoursEnd = end,
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? null : request.TimeZone.Trim()
        };
        foreach (var (field, message) in settings.Validate())
        {
            errors.TryAdd(field, [message]);
        }

        if (errors.Count > 0)
        {
            return DashboardResult.Invalid(errors);
        }

        var saved = await settingsStore.SaveAsync(settings, caller.Actor, ct);
        await settingsSource.RefreshAsync(ct);
        await journal.RecordAuditAsync(caller.Actor, "SignalSettingsUpdated",
            $"Version {saved.Version}: {(settings.Enabled ? "on" : "off")}; signals-only markets: " +
            (settings.SignalOnlyInstruments.Count == 0 ? "none" : string.Join(", ", settings.SignalOnlyInstruments)) +
            $"; near misses {(settings.NearMissEnabled ? $"from score {settings.NearMissMinScore}" : "off")}; " +
            $"{settings.MaxOpenPositions} slot(s), {settings.RiskPerTradePercent}% per trade, {settings.DailyLossLimitPercent}% daily loss; " +
            $"loss-limit override {(settings.AllowLossLimitOverride ? "allowed" : "off")}", caller.CorrelationId, ct);
        return DashboardResult.Ok(ToDto(saved));
    }

    /// <summary>Profit for placed signals, and what-if results (the learning system's virtual trades) for the others.</summary>
    private async Task<Dictionary<Guid, SignalResultDto>> ResultsAsync(IReadOnlyList<SignalEntity> signals, CancellationToken ct)
    {
        var results = new Dictionary<Guid, SignalResultDto>();
        if (signals.Count == 0)
        {
            return results;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var positionIds = signals.Where(s => s.PositionId != null).Select(s => s.PositionId!.Value).ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var setupIds = signals.Where(s => s.PositionId == null).Select(s => s.SetupId).ToList();
        var outcomes = await db.SetupOutcomes.AsNoTracking().Where(o => setupIds.Contains(o.SetupId)).ToDictionaryAsync(o => o.SetupId, ct);

        foreach (var signal in signals)
        {
            if (signal.PositionId is { } positionId && positions.TryGetValue(positionId, out var position))
            {
                results[signal.Id] = new SignalResultDto(true, position.IsOpen, position.RealizedPnl, position.RMultiple, position.ExitReason);
            }
            else if (signal.Status is SignalStatus.Skipped or SignalStatus.Expired or SignalStatus.Failed
                     && outcomes.TryGetValue(signal.SetupId, out var outcome))
            {
                results[signal.Id] = new SignalResultDto(false, outcome.RMultiple is null, null, outcome.RMultiple,
                    outcome.RMultiple is null ? null : outcome.Status);
            }
        }

        return results;
    }

    private static TimeOnly? ParseTime(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeOnly.TryParseExact(value.Trim(), ["HH:mm", "HH:mm:ss"], out var time))
        {
            return time;
        }

        errors[field] = ["Use a time such as 22:00."];
        return null;
    }

    private static SignalDto ToDto(SignalEntity s, SignalSettings settings, SignalResultDto? result)
    {
        var limits = settings.ToLimits();
        var risk = Math.Abs(s.Entry - s.StopLoss);
        return new SignalDto(s.Id, s.SetupId, s.Kind, s.Instrument, s.Direction, s.Strategy, s.Score, s.Regime, s.Entry, s.StopLoss, s.TakeProfit,
            risk == 0 ? 0 : Math.Round(Math.Abs(s.TakeProfit - s.Entry) / risk, 2), s.CreatedAtUtc, s.ExpiresAtUtc, s.Status, s.Message,
            SignalStore.ReadChecks(s).Select(c => new SignalCheckDto(c.Rule, c.Passed, c.Detail, c.Overridden, c.Kind,
                !c.Passed && RiskRuleKinds.MayOverride(c.Rule, limits))).ToList(),
            s.RiskAmount, SignalStore.ReadAcceptedRules(s), s.DecidedAtUtc, s.DecidedBy, s.PositionId, s.FillPrice, s.DecisionId, result);
    }

    private static SignalSettingsDto ToDto(SignalSettingsView v) => new(
        v.Settings.Enabled, v.Settings.SignalOnlyInstruments, v.Settings.NearMissEnabled, v.Settings.NearMissMinScore, v.Settings.MaxOpenPositions,
        v.Settings.RiskPerTradePercent, v.Settings.DailyLossLimitPercent, v.Settings.ExpiryMinutes, v.Settings.MaxPriceMoveFraction,
        v.Settings.OneTapFromNotification, v.Settings.AllowLossLimitOverride, v.Settings.QuietHoursStart?.ToString("HH:mm"),
        v.Settings.QuietHoursEnd?.ToString("HH:mm"), v.Settings.TimeZone, v.Version, v.UpdatedAtUtc, v.UpdatedBy);
}
