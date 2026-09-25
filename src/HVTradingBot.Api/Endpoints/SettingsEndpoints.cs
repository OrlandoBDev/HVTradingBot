using System.Text.RegularExpressions;
using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Api.Services;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Contracts;
using HVTradingBot.Application.Trading;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Notifications;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Api.Endpoints;

/// <summary>
/// Deriv account settings for the dashboard. The API only stores settings; the worker connects to Deriv with them and
/// reports the result, so the API never talks to the broker.
/// </summary>
public static partial class SettingsEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex AppIdPattern();

    [GeneratedRegex(@"^\S{8,512}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[A-Za-z0-9]{1,32}$")]
    private static partial Regex AccountIdPattern();

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings/deriv");

        settings.MapGet("", async (DerivSettingsStore store, IConfiguration configuration, CancellationToken ct) =>
            await ToDtoAsync(store, configuration, await store.GetViewAsync(ct), ct));

        settings.MapPut("", async (DerivSettingsRequest request, DerivSettingsStore store, IDecisionJournal journal, IConfiguration configuration,
            HttpContext http, ILogger<DerivSettingsRequest> logger, CancellationToken ct) =>
        {
            var appId = request.AppId?.Trim() ?? "";
            var token = string.IsNullOrWhiteSpace(request.ApiToken) ? null : request.ApiToken.Trim();
            var accountId = string.IsNullOrWhiteSpace(request.AccountId) ? null : request.AccountId.Trim();

            var errors = new Dictionary<string, string[]>();
            if (!AppIdPattern().IsMatch(appId)) errors["appId"] = ["App ID is required (letters, digits, '-' or '_', up to 64 characters)."];
            if (token is not null && !TokenPattern().IsMatch(token)) errors["apiToken"] = ["Token must be 8-512 characters without spaces."];
            if (accountId is not null && !AccountIdPattern().IsMatch(accountId)) errors["accountId"] = ["Account ID must be letters and digits only."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            DerivSettingsView view;
            try
            {
                view = await store.SaveAsync(appId, token, accountId, Actor(http), ct);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["apiToken"] = [ex.Message] });
            }

            // Audit what changed, never the token value.
            var details = $"App ID {appId}; token {(token is null ? "unchanged" : "replaced (…" + view.TokenHint + ")")}; account {accountId ?? "first demo account"}.";
            await journal.RecordAuditAsync(Actor(http), "DerivSettingsUpdated", details, http.CorrelationId(), ct);
            logger.LogInformation("Deriv settings updated by {Actor}: {Details}", Actor(http), details);
            return Results.Ok(await ToDtoAsync(store, configuration, view, ct));
        });

        settings.MapDelete("", async (DerivSettingsStore store, IDecisionJournal journal, HttpContext http, CancellationToken ct) =>
        {
            await store.ClearAsync(Actor(http), ct);
            await journal.RecordAuditAsync(Actor(http), "DerivSettingsRemoved", "Deriv App ID and token removed from the database.", http.CorrelationId(), ct);
            return Results.NoContent();
        });

        var risk = app.MapGroup("/api/settings/risk");

        risk.MapGet("", async (RiskOptionsSource source, DashboardQueries queries, CancellationToken ct) =>
            await RiskDtoAsync(source, queries, ct));

        risk.MapPut("", async (RiskLimitsDto request, RiskOptionsSource source, RiskSettingsStore store, DashboardQueries queries,
            IDecisionJournal journal, HttpContext http, CancellationToken ct) =>
        {
            var limits = new RiskLimits(request.MaxRiskPerTradePercent, request.MaxDailyLossPercent, request.MaxWeeklyLossPercent,
                request.MaxOpenPositions, request.MinRewardToRisk, request.MaxConsecutiveLosses, request.CooldownMinutes,
                request.MaxCurrencyExposure, request.MaxCommissionShareOfRisk, request.MaxDerivedOpenPositions, request.DerivedRiskPerTradePercent,
                request.MaxDerivedDailyLossPercent, request.AssumedCommissionPercent);
            limits = limits.WithDefaultsFrom(source.Defaults);
            var errors = limits.Validate();
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key[1..], e => new[] { e.Value }));
            }

            var before = (await source.RefreshAsync(ct)).Effective;
            await store.SaveAsync(limits, Actor(http), ct);
            await journal.RecordAuditAsync(Actor(http), "RiskLimitsUpdated", $"{before} -> {limits}", http.CorrelationId(), ct);
            return Results.Ok(await RiskDtoAsync(source, queries, ct));
        });

        risk.MapDelete("", async (RiskOptionsSource source, RiskSettingsStore store, DashboardQueries queries, IDecisionJournal journal,
            HttpContext http, CancellationToken ct) =>
        {
            await store.SaveAsync(null, Actor(http), ct);
            await journal.RecordAuditAsync(Actor(http), "RiskLimitsReset", "Risk limits reset to configured defaults.", http.CorrelationId(), ct);
            return Results.Ok(await RiskDtoAsync(source, queries, ct));
        });

        var notifications = app.MapGroup("/api/settings/notifications");

        notifications.MapGet("", async (EmailSettingsStore store, CancellationToken ct) => ToDto(await store.GetViewAsync(ct)));

        notifications.MapPut("", async (NotificationSettingsRequest request, EmailSettingsStore store, IDecisionJournal journal,
            HttpContext http, CancellationToken ct) =>
        {
            var input = new EmailSettingsInput(request.Enabled, request.SmtpHost ?? "", request.SmtpPort, request.Username,
                string.IsNullOrEmpty(request.Password) ? null : request.Password, request.FromAddress, request.FromName,
                request.ToAddresses ?? [], request.OnTradeOpened, request.OnTradeClosed, request.OnOrderRejected, request.OnKillSwitch);
            var current = await store.GetViewAsync(ct);
            var errors = EmailSettingsStore.Validate(input, current.Source == "database" && current.PasswordConfigured);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
            }

            var saved = await store.SaveAsync(input, Actor(http), ct);
            var s = saved.Settings;
            await journal.RecordAuditAsync(Actor(http), "NotificationSettingsUpdated",
                $"Email {(s.Enabled ? "enabled" : "disabled")}; SMTP {s.SmtpHost}:{s.SmtpPort}; user {s.Username}; password " +
                $"{(input.Password is null ? "unchanged" : "replaced")}; recipients {string.Join(", ", s.ToAddresses)}; events " +
                $"opened={s.OnTradeOpened} closed={s.OnTradeClosed} rejected={s.OnOrderRejected} killswitch={s.OnKillSwitch}.",
                http.CorrelationId(), ct);
            return Results.Ok(ToDto(saved));
        });

        notifications.MapPost("/test", async (EmailSettingsStore store, IEmailSender sender, HttpContext http, ILogger<TestEmailResult> logger,
            CancellationToken ct) =>
        {
            var settings = (await store.GetViewAsync(ct)).Settings;
            if (!settings.IsComplete)
            {
                return Results.Ok(new TestEmailResult(false, "Save the SMTP server, username, app password and at least one recipient first."));
            }

            var message = TradeDecisionEmailFormatter.Format(
                new TradeDecisionNotification(Guid.NewGuid(), "-", null, Domain.Common.DecisionState.NoTrade, Domain.Common.TradingMode.Paper,
                    DateTimeOffset.UtcNow, [$"Requested from the dashboard ({Actor(http)})."]) { Kind = NotificationKind.Test },
                settings.SubjectPrefix);
            try
            {
                await sender.SendAsync(message, settings, ct);
                await store.RecordAttemptAsync(true, null, ct);
                return Results.Ok(new TestEmailResult(true, $"Test email sent to {string.Join(", ", settings.ToAddresses)}."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Test email failed");
                await store.RecordAttemptAsync(false, ex.Message, ct);
                return Results.Ok(new TestEmailResult(false, $"Sending failed: {ex.Message}"));
            }
        });

        var markets = app.MapGroup("/api/settings/markets");

        markets.MapGet("", async (MarketCatalogStore catalog, TradingEngineOptions engineOptions, CancellationToken ct) =>
            await MarketsDtoAsync(catalog, engineOptions, ct));

        markets.MapPut("", async (MarketSelectionRequest request, MarketCatalogStore catalog, TradingEngineOptions engineOptions,
            IDecisionJournal journal, HttpContext http, CancellationToken ct) =>
        {
            await catalog.LoadAndRegisterAsync(ct);
            try
            {
                var saved = await catalog.SaveSelectionAsync(request.Instruments ?? [], Actor(http), ct, request.DerivedOnlyWhenForexClosed);
                await journal.RecordAuditAsync(Actor(http), "MarketSelectionUpdated",
                    $"Version {saved.Version}: {string.Join(", ", saved.Instruments ?? [])}; Derived " +
                    (saved.DerivedOnlyWhenForexClosed ? "only while Forex is closed" : "always"), http.CorrelationId(), ct);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["instruments"] = [ex.Message] });
            }

            return Results.Ok(await MarketsDtoAsync(catalog, engineOptions, ct));
        });

        return app;
    }

    private static NotificationSettingsDto ToDto(EmailSettingsView v) => new(
        v.Settings.Enabled, v.Settings.SmtpHost, v.Settings.SmtpPort, v.Settings.Username, v.PasswordConfigured, v.PasswordHint,
        v.Settings.FromAddress, v.Settings.FromName, v.Settings.ToAddresses, v.Settings.OnTradeOpened, v.Settings.OnTradeClosed,
        v.Settings.OnOrderRejected, v.Settings.OnKillSwitch, v.Source, v.UpdatedAtUtc, v.UpdatedBy, v.LastAttemptUtc,
        v.LastAttemptSucceeded, v.LastError);

    private static async Task<RiskSettingsDto> RiskDtoAsync(RiskOptionsSource source, DashboardQueries queries, CancellationToken ct)
    {
        var view = await source.RefreshAsync(ct);
        var status = await queries.GetStatusAsync(ct);
        return new RiskSettingsDto(ToDto(view.Effective), ToDto(view.Defaults), view.IsCustomized, view.Version, view.UpdatedAtUtc, view.UpdatedBy,
            status.Account.Balance, status.Account.Currency);
    }

    private static RiskLimitsDto ToDto(RiskLimits l) => new(l.MaxRiskPerTradePercent, l.MaxDailyLossPercent, l.MaxWeeklyLossPercent,
        l.MaxOpenPositions, l.MinRewardToRisk, l.MaxConsecutiveLosses, l.CooldownMinutes, l.MaxCurrencyExposure, l.MaxCommissionShareOfRisk,
        l.MaxDerivedOpenPositions, l.DerivedRiskPerTradePercent, l.MaxDerivedDailyLossPercent, l.AssumedCommissionPercent);

    private static async Task<MarketSettingsDto> MarketsDtoAsync(MarketCatalogStore catalog, TradingEngineOptions engineOptions, CancellationToken ct)
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
                System.Text.Json.JsonSerializer.Deserialize<List<int>>(m.Multipliers) ?? [])).ToList());
    }

    private static async Task<DerivSettingsDto> ToDtoAsync(DerivSettingsStore store, IConfiguration configuration, DerivSettingsView view, CancellationToken ct)
    {
        var status = await store.GetStatusAsync(ct);
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

    private static string Actor(HttpContext http) => $"dashboard@{http.Connection.RemoteIpAddress}";
}
