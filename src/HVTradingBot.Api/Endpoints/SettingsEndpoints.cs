using System.Text.RegularExpressions;
using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Api.Services;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Contracts;
using HVTradingBot.Application.Trading;
using HVTradingBot.Infrastructure.Markets;
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
                request.MaxCurrencyExposure, request.MaxCommissionShareOfRisk);
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

        var markets = app.MapGroup("/api/settings/markets");

        markets.MapGet("", async (MarketCatalogStore catalog, TradingEngineOptions engineOptions, CancellationToken ct) =>
            await MarketsDtoAsync(catalog, engineOptions, ct));

        markets.MapPut("", async (MarketSelectionRequest request, MarketCatalogStore catalog, TradingEngineOptions engineOptions,
            IDecisionJournal journal, HttpContext http, CancellationToken ct) =>
        {
            await catalog.LoadAndRegisterAsync(ct);
            try
            {
                var saved = await catalog.SaveSelectionAsync(request.Instruments ?? [], Actor(http), ct);
                await journal.RecordAuditAsync(Actor(http), "MarketSelectionUpdated",
                    $"Version {saved.Version}: {string.Join(", ", saved.Instruments ?? [])}", http.CorrelationId(), ct);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["instruments"] = [ex.Message] });
            }

            return Results.Ok(await MarketsDtoAsync(catalog, engineOptions, ct));
        });

        return app;
    }

    private static async Task<RiskSettingsDto> RiskDtoAsync(RiskOptionsSource source, DashboardQueries queries, CancellationToken ct)
    {
        var view = await source.RefreshAsync(ct);
        var status = await queries.GetStatusAsync(ct);
        return new RiskSettingsDto(ToDto(view.Effective), ToDto(view.Defaults), view.IsCustomized, view.Version, view.UpdatedAtUtc, view.UpdatedBy,
            status.Account.Balance, status.Account.Currency);
    }

    private static RiskLimitsDto ToDto(RiskLimits l) => new(l.MaxRiskPerTradePercent, l.MaxDailyLossPercent, l.MaxWeeklyLossPercent,
        l.MaxOpenPositions, l.MinRewardToRisk, l.MaxConsecutiveLosses, l.CooldownMinutes, l.MaxCurrencyExposure, l.MaxCommissionShareOfRisk);

    private static async Task<MarketSettingsDto> MarketsDtoAsync(MarketCatalogStore catalog, TradingEngineOptions engineOptions, CancellationToken ct)
    {
        var items = await catalog.GetCatalogAsync(ct);
        var selection = await catalog.GetSelectionAsync(ct);
        return new MarketSettingsDto(
            selection.Instruments ?? engineOptions.Instruments,
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
