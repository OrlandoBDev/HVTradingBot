using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;

namespace HVTradingBot.Api.Endpoints;

/// <summary>
/// Deriv account, risk, notification and market settings for the dashboard. The API only stores settings; the worker
/// connects to Deriv with them and reports the result, so the API never talks to the broker.
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings/deriv");
        settings.MapGet("", (DashboardActions actions, CancellationToken ct) => actions.GetDerivSettingsAsync(ct));
        settings.MapPut("", async (DerivSettingsRequest request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.SaveDerivSettingsAsync(request, http.Caller(), ct)).ToHttp());
        settings.MapDelete("", async (DashboardActions actions, HttpContext http, CancellationToken ct) =>
        {
            await actions.ClearDerivSettingsAsync(http.Caller(), ct);
            return Results.NoContent();
        });

        var risk = app.MapGroup("/api/settings/risk");
        risk.MapGet("", (DashboardActions actions, CancellationToken ct) => actions.GetRiskSettingsAsync(ct));
        risk.MapPut("", async (RiskLimitsDto request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.SaveRiskSettingsAsync(request, http.Caller(), ct)).ToHttp());
        risk.MapDelete("", (DashboardActions actions, HttpContext http, CancellationToken ct) => actions.ResetRiskSettingsAsync(http.Caller(), ct));

        var notifications = app.MapGroup("/api/settings/notifications");
        notifications.MapGet("", (DashboardActions actions, CancellationToken ct) => actions.GetNotificationSettingsAsync(ct));
        notifications.MapPut("", async (NotificationSettingsRequest request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.SaveNotificationSettingsAsync(request, http.Caller(), ct)).ToHttp());
        notifications.MapPost("/test", (DashboardActions actions, HttpContext http, CancellationToken ct) => actions.SendTestEmailAsync(http.Caller(), ct));

        var signals = app.MapGroup("/api/settings/signals");
        signals.MapGet("", (SignalDashboard dashboard, CancellationToken ct) => dashboard.GetSettingsAsync(ct));
        signals.MapPut("", async (SignalSettingsDto request, SignalDashboard dashboard, HttpContext http, CancellationToken ct) =>
            (await dashboard.SaveSettingsAsync(request, http.Caller(), ct)).ToHttp());

        var markets = app.MapGroup("/api/settings/markets");
        markets.MapGet("", (DashboardActions actions, CancellationToken ct) => actions.GetMarketSettingsAsync(ct));
        markets.MapPut("", async (MarketSelectionRequest request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.SaveMarketSelectionAsync(request, http.Caller(), ct)).ToHttp());

        return app;
    }
}
