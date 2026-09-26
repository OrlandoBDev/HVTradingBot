using System.Text.Json;
using System.Text.RegularExpressions;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Learning;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Notifications;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.TestTrades;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.Extensions.Configuration;

namespace HVTradingBot.Dashboard;

/// <summary>
/// Everything the dashboard can change: kill switch, close and test-trade requests, settings. Requests to the broker
/// are only recorded here; the trading worker (the only component that talks to the broker) carries them out.
/// </summary>
public sealed partial class DashboardActions(
    DashboardQueries queries,
    ITradingStateStore state,
    IDecisionJournal journal,
    IClock clock,
    CloseRequestStore closeRequests,
    TestTradeStore testTrades,
    IVirtualTradeStore virtualTrades,
    LearningOptions learningOptions,
    DerivSettingsStore derivSettings,
    RiskOptionsSource riskSource,
    RiskSettingsStore riskSettings,
    EmailSettingsStore emailSettings,
    IEmailSender emailSender,
    MarketCatalogStore catalog,
    TradingEngineOptions engineOptions,
    IConfiguration configuration,
    ILogger<DashboardActions> logger)
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex AppIdPattern();

    [GeneratedRegex(@"^\S{8,512}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[A-Za-z0-9]{1,32}$")]
    private static partial Regex AccountIdPattern();

    /// <summary>
    /// Readable names of every known market (including ones no longer selected, so older trades get names too), keyed by
    /// our symbol and by the broker's own id (contracts opened outside the app may carry either). Read from the stored
    /// catalog, so it is complete even before the trading worker has registered the catalog in this process.
    /// </summary>
    public async Task<Dictionary<string, string>> GetMarketNamesAsync(CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var market in await catalog.GetCatalogAsync(ct))
        {
            names.TryAdd(market.Symbol, market.Name);
            names.TryAdd(market.BrokerSymbol, market.Name);
        }

        foreach (var instrument in Instruments.All)
        {
            names.TryAdd(instrument.Symbol, instrument.DisplayName);
            if (instrument.BrokerSymbol is { } brokerSymbol)
            {
                names.TryAdd(brokerSymbol, instrument.DisplayName);
            }
        }

        return names;
    }

    public async Task<DashboardResult> RequestCloseAsync(Guid positionId, DashboardCaller caller, CancellationToken ct)
    {
        var status = await queries.GetStatusAsync(ct);
        if (!status.WorkerHealthy)
        {
            return DashboardResult.Invalid("position", "The trading worker is not running.");
        }

        var (request, problem) = await closeRequests.CreateAsync(positionId, caller.Actor, ct);
        if (request is null)
        {
            return DashboardResult.Invalid("position", problem!);
        }

        await journal.RecordAuditAsync(caller.Actor, "CloseRequested", $"{request.Instrument} position {positionId}", caller.CorrelationId, ct);
        return DashboardResult.Ok(new { request.Id, request.Status, request.Message });
    }

    public async Task<object> GetLearningAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var outcomes = await virtualTrades.GetOutcomesAsync(now.AddDays(-learningOptions.LookbackDays), ct);
        var model = StrategyPerformanceModel.Compute(outcomes, now, learningOptions);
        var context = StrategyPerformanceModel.ComputeContext(outcomes, now, learningOptions);
        return new
        {
            learningOptions.Enabled,
            learningOptions.LookbackDays,
            learningOptions.PriorStrength,
            learningOptions.MaxBoost,
            learningOptions.MaxPenalty,
            learningOptions.DisableAfterSamples,
            learningOptions.DisableBelowR,
            Outcomes = outcomes.Count,
            Combinations = model.Values
                .OrderByDescending(p => p.Samples)
                .Select(p => new
                {
                    p.Key.Strategy,
                    Regime = p.Key.Regime.ToString(),
                    AssetClass = p.Key.AssetClass.ToString(),
                    p.Samples,
                    p.WinRate,
                    p.AverageR,
                    p.ShrunkR,
                    p.ScoreAdjustment,
                    p.Disabled
                }),
            learningOptions.ContextMaxBoost,
            learningOptions.ContextMaxPenalty,
            // What news and cross-market trend conditions have been worth to each strategy.
            Conditions = context.Values
                .OrderBy(c => c.Key.Factor, StringComparer.Ordinal)
                .ThenBy(c => c.Key.Strategy, StringComparer.Ordinal)
                .ThenBy(c => c.Key.Value, StringComparer.Ordinal)
                .Select(c => new
                {
                    c.Key.Strategy,
                    c.Key.Factor,
                    Condition = c.Key.Value,
                    c.Samples,
                    c.WinRate,
                    c.AverageR,
                    c.BaselineR,
                    c.ShrunkExcessR,
                    c.ScoreAdjustment
                })
        };
    }

    public async Task<IReadOnlyList<TestTradeDto>> GetTestTradesAsync(CancellationToken ct) =>
        (await testTrades.RecentAsync(5, ct)).Select(ToDto).ToList();

    public async Task<DashboardResult> RequestTestTradeAsync(TestTradeRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var market = (await queries.GetMarketsAsync(ct)).FirstOrDefault(m => m.Instrument == request.Instrument);
        string? problem = market switch
        {
            null => "Choose one of the selected markets.",
            { IsTradable: false } => $"{market.DisplayName} is analysis-only and cannot be traded.",
            { IsLoading: true } => $"{market.DisplayName} is still loading.",
            { IsOpen: false } => $"{market.DisplayName} is closed right now.",
            _ => null
        };
        if (problem is not null)
        {
            return DashboardResult.Invalid("instrument", problem);
        }

        var status = await queries.GetStatusAsync(ct);
        if (!status.WorkerHealthy)
        {
            return DashboardResult.Invalid("instrument", "The trading worker is not running.");
        }

        var created = await testTrades.CreateAsync(request.Instrument, caller.Actor, ct);
        return created is null
            ? DashboardResult.Invalid("instrument", "A test trade is already in progress.")
            : DashboardResult.Ok(ToDto(created));
    }

    /// <summary>
    /// Activating the kill switch always succeeds. Deactivating requires a reason and is audited.
    /// The worker reads this state before every risk check and again immediately before execution.
    /// </summary>
    public async Task<DashboardResult> SetKillSwitchAsync(KillSwitchRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            if (!request.Active)
            {
                return DashboardResult.Invalid("reason", "A reason is required to deactivate the kill switch.");
            }

            reason = "Manual activation from dashboard.";
        }

        if (reason.Length > 500)
        {
            return DashboardResult.Invalid("reason", "Reason must be at most 500 characters.");
        }

        var updated = await state.UpdateAsync(s => s.WithKillSwitch(request.Active, reason, clock.UtcNow), ct);
        await journal.RecordAuditAsync(caller.Actor, request.Active ? "KillSwitchActivated" : "KillSwitchDeactivated", reason, caller.CorrelationId, ct);
        logger.LogWarning("Kill switch set to {Active} by {Actor}: {Reason}", request.Active, caller.Actor, reason);
        return DashboardResult.Ok(new { updated.KillSwitchActive, updated.KillSwitchReason, updated.KillSwitchChangedUtc });
    }

    // ---- Deriv account ----

    public async Task<DerivSettingsDto> GetDerivSettingsAsync(CancellationToken ct) =>
        await ToDtoAsync(await derivSettings.GetViewAsync(ct), ct);

    public async Task<DashboardResult> SaveDerivSettingsAsync(DerivSettingsRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var appId = request.AppId?.Trim() ?? "";
        var token = string.IsNullOrWhiteSpace(request.ApiToken) ? null : request.ApiToken.Trim();
        var accountId = string.IsNullOrWhiteSpace(request.AccountId) ? null : request.AccountId.Trim();

        var errors = new Dictionary<string, string[]>();
        if (!AppIdPattern().IsMatch(appId)) errors["appId"] = ["App ID is required (letters, digits, '-' or '_', up to 64 characters)."];
        if (token is not null && !TokenPattern().IsMatch(token)) errors["apiToken"] = ["Token must be 8-512 characters without spaces."];
        if (accountId is not null && !AccountIdPattern().IsMatch(accountId)) errors["accountId"] = ["Account ID must be letters and digits only."];
        if (errors.Count > 0) return DashboardResult.Invalid(errors);

        DerivSettingsView view;
        try
        {
            view = await derivSettings.SaveAsync(appId, token, accountId, caller.Actor, ct);
        }
        catch (ArgumentException ex)
        {
            return DashboardResult.Invalid("apiToken", ex.Message);
        }

        // Audit what changed, never the token value.
        var details = $"App ID {appId}; token {(token is null ? "unchanged" : "replaced (…" + view.TokenHint + ")")}; account {accountId ?? "first demo account"}.";
        await journal.RecordAuditAsync(caller.Actor, "DerivSettingsUpdated", details, caller.CorrelationId, ct);
        logger.LogInformation("Deriv settings updated by {Actor}: {Details}", caller.Actor, details);
        return DashboardResult.Ok(await ToDtoAsync(view, ct));
    }

    public async Task ClearDerivSettingsAsync(DashboardCaller caller, CancellationToken ct)
    {
        await derivSettings.ClearAsync(caller.Actor, ct);
        await journal.RecordAuditAsync(caller.Actor, "DerivSettingsRemoved", "Deriv App ID and token removed from the database.", caller.CorrelationId, ct);
    }

    // ---- Risk limits ----

    public async Task<RiskSettingsDto> GetRiskSettingsAsync(CancellationToken ct)
    {
        var view = await riskSource.RefreshAsync(ct);
        var status = await queries.GetStatusAsync(ct);
        return new RiskSettingsDto(ToDto(view.Effective), ToDto(view.Defaults), view.IsCustomized, view.Version, view.UpdatedAtUtc, view.UpdatedBy,
            status.Account.Balance, status.Account.Currency);
    }

    public async Task<DashboardResult> SaveRiskSettingsAsync(RiskLimitsDto request, DashboardCaller caller, CancellationToken ct)
    {
        var limits = new RiskLimits(request.MaxRiskPerTradePercent, request.MaxDailyLossPercent, request.MaxWeeklyLossPercent,
            request.MaxOpenPositions, request.MinRewardToRisk, request.MaxConsecutiveLosses, request.CooldownMinutes,
            request.MaxCurrencyExposure, request.MaxCommissionShareOfRisk, request.MaxDerivedOpenPositions, request.DerivedRiskPerTradePercent,
            request.MaxDerivedDailyLossPercent, request.AssumedCommissionPercent, request.MaxExtraDerivedPositions,
            request.HighScoreOverrideMinScore);
        limits = limits.WithDefaultsFrom(riskSource.Defaults);
        var errors = limits.Validate();
        if (errors.Count > 0)
        {
            return DashboardResult.Invalid(errors.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key[1..], e => new[] { e.Value }));
        }

        var before = (await riskSource.RefreshAsync(ct)).Effective;
        await riskSettings.SaveAsync(limits, caller.Actor, ct);
        await journal.RecordAuditAsync(caller.Actor, "RiskLimitsUpdated", $"{before} -> {limits}", caller.CorrelationId, ct);
        return DashboardResult.Ok(await GetRiskSettingsAsync(ct));
    }

    public async Task<RiskSettingsDto> ResetRiskSettingsAsync(DashboardCaller caller, CancellationToken ct)
    {
        await riskSettings.SaveAsync(null, caller.Actor, ct);
        await journal.RecordAuditAsync(caller.Actor, "RiskLimitsReset", "Risk limits reset to configured defaults.", caller.CorrelationId, ct);
        return await GetRiskSettingsAsync(ct);
    }

    // ---- Email notifications ----

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken ct) => ToDto(await emailSettings.GetViewAsync(ct));

    public async Task<DashboardResult> SaveNotificationSettingsAsync(NotificationSettingsRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var input = new EmailSettingsInput(request.Enabled, request.SmtpHost ?? "", request.SmtpPort, request.Username,
            string.IsNullOrEmpty(request.Password) ? null : request.Password, request.FromAddress, request.FromName,
            request.ToAddresses ?? [], request.OnTradeOpened, request.OnTradeClosed, request.OnOrderRejected, request.OnKillSwitch);
        var current = await emailSettings.GetViewAsync(ct);
        var errors = EmailSettingsStore.Validate(input, current.Source == "database" && current.PasswordConfigured);
        if (errors.Count > 0)
        {
            return DashboardResult.Invalid(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
        }

        var saved = await emailSettings.SaveAsync(input, caller.Actor, ct);
        var s = saved.Settings;
        await journal.RecordAuditAsync(caller.Actor, "NotificationSettingsUpdated",
            $"Email {(s.Enabled ? "enabled" : "disabled")}; SMTP {s.SmtpHost}:{s.SmtpPort}; user {s.Username}; password " +
            $"{(input.Password is null ? "unchanged" : "replaced")}; recipients {string.Join(", ", s.ToAddresses)}; events " +
            $"opened={s.OnTradeOpened} closed={s.OnTradeClosed} rejected={s.OnOrderRejected} killswitch={s.OnKillSwitch}.",
            caller.CorrelationId, ct);
        return DashboardResult.Ok(ToDto(saved));
    }

    public async Task<TestEmailResult> SendTestEmailAsync(DashboardCaller caller, CancellationToken ct)
    {
        var settings = (await emailSettings.GetViewAsync(ct)).Settings;
        if (!settings.IsComplete)
        {
            return new TestEmailResult(false, "Save the SMTP server, username, app password and at least one recipient first.");
        }

        var message = TradeDecisionEmailFormatter.Format(
            new TradeDecisionNotification(Guid.NewGuid(), "-", null, Domain.Common.DecisionState.NoTrade, Domain.Common.TradingMode.Paper,
                DateTimeOffset.UtcNow, [$"Requested from the dashboard ({caller.Actor})."]) { Kind = NotificationKind.Test },
            settings.SubjectPrefix);
        try
        {
            await emailSender.SendAsync(message, settings, ct);
            await emailSettings.RecordAttemptAsync(true, null, ct);
            return new TestEmailResult(true, $"Test email sent to {string.Join(", ", settings.ToAddresses)}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Test email failed");
            await emailSettings.RecordAttemptAsync(false, ex.Message, ct);
            return new TestEmailResult(false, $"Sending failed: {ex.Message}");
        }
    }

    // ---- Market selection ----

    public async Task<MarketSettingsDto> GetMarketSettingsAsync(CancellationToken ct)
    {
        var items = await catalog.GetCatalogAsync(ct);
        var selection = await catalog.GetSelectionAsync(ct);
        return new MarketSettingsDto(
            selection.Instruments ?? engineOptions.Instruments,
            selection.DerivedOnlyWhenForexClosed,
            selection.Instruments is null,
            selection.Version,
            selection.AppliedVersion,
            selection.UpdatedAtUtc,
            selection.UpdatedBy,
            MarketCatalogStore.MaxSelectedInstruments,
            items.Select(m => new MarketCatalogItemDto(m.Symbol, m.BrokerSymbol, m.Name, m.Market, m.Submarket, m.AssetClass, m.IsTradable, m.IsOpen,
                JsonSerializer.Deserialize<List<int>>(m.Multipliers) ?? [])).ToList());
    }

    public async Task<DashboardResult> SaveMarketSelectionAsync(MarketSelectionRequest request, DashboardCaller caller, CancellationToken ct)
    {
        await catalog.LoadAndRegisterAsync(ct);
        try
        {
            var saved = await catalog.SaveSelectionAsync(request.Instruments ?? [], caller.Actor, ct, request.DerivedOnlyWhenForexClosed);
            await journal.RecordAuditAsync(caller.Actor, "MarketSelectionUpdated",
                $"Version {saved.Version}: {string.Join(", ", saved.Instruments ?? [])}; Derived " +
                (saved.DerivedOnlyWhenForexClosed ? "only while Forex is closed" : "always"), caller.CorrelationId, ct);
        }
        catch (ArgumentException ex)
        {
            return DashboardResult.Invalid("instruments", ex.Message);
        }

        return DashboardResult.Ok(await GetMarketSettingsAsync(ct));
    }

    private static TestTradeDto ToDto(TestTradeEntity t) => new(t.Id, t.Instrument, t.Status, t.Message,
        t.RequestedBy, t.RequestedAtUtc, t.OpenedAtUtc, t.ClosedAtUtc, t.ClientOrderId, t.FillPrice, t.ExitPrice, t.RealizedPnl, t.HoldSeconds);

    private static NotificationSettingsDto ToDto(EmailSettingsView v) => new(
        v.Settings.Enabled, v.Settings.SmtpHost, v.Settings.SmtpPort, v.Settings.Username, v.PasswordConfigured, v.PasswordHint,
        v.Settings.FromAddress, v.Settings.FromName, v.Settings.ToAddresses, v.Settings.OnTradeOpened, v.Settings.OnTradeClosed,
        v.Settings.OnOrderRejected, v.Settings.OnKillSwitch, v.Source, v.UpdatedAtUtc, v.UpdatedBy, v.LastAttemptUtc,
        v.LastAttemptSucceeded, v.LastError);

    private static RiskLimitsDto ToDto(RiskLimits l) => new(l.MaxRiskPerTradePercent, l.MaxDailyLossPercent, l.MaxWeeklyLossPercent,
        l.MaxOpenPositions, l.MinRewardToRisk, l.MaxConsecutiveLosses, l.CooldownMinutes, l.MaxCurrencyExposure, l.MaxCommissionShareOfRisk,
        l.MaxDerivedOpenPositions, l.DerivedRiskPerTradePercent, l.MaxDerivedDailyLossPercent, l.AssumedCommissionPercent,
        l.MaxExtraDerivedPositions, l.HighScoreOverrideMinScore);

    private async Task<DerivSettingsDto> ToDtoAsync(DerivSettingsView view, CancellationToken ct)
    {
        var status = await derivSettings.GetStatusAsync(ct);
        return new DerivSettingsDto(
            configuration["Broker:Provider"] ?? "Deriv",
            configuration["Deriv:AccountType"] ?? "Demo",
            view.AppId,
            view.TokenConfigured,
            view.TokenHint,
            view.AccountId,
            view.Source,
            view.UpdatedAtUtc,
            view.UpdatedBy,
            status is null
                ? null
                : new DerivConnectionDto(status.State.ToString(), status.Message, status.SettingsVersion == view.Version, status.ConnectedAccountId,
                    status.Accounts.Select(a => new DerivAccountDto(a.AccountId, a.AccountType, a.Currency)).ToList(), status.CheckedAtUtc));
    }
}
