using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Api.Services;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Learning;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Contracts;

namespace HVTradingBot.Api.Endpoints;

public static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (DashboardQueries q, CancellationToken ct) => q.GetStatusAsync(ct));
        api.MapGet("/markets", (DashboardQueries q, CancellationToken ct) => q.GetMarketsAsync(ct));
        api.MapGet("/decisions", (DashboardQueries q, string? state, string? instrument, int? limit, CancellationToken ct) =>
            q.GetDecisionsAsync(state, instrument, limit ?? 100, ct));
        api.MapGet("/decisions/{id:guid}", async (Guid id, DashboardQueries q, CancellationToken ct) =>
            await q.GetDecisionAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());
        api.MapGet("/positions/open", (DashboardQueries q, CancellationToken ct) => q.GetOpenPositionsAsync(ct));
        api.MapGet("/trades", (DashboardQueries q, int? limit, CancellationToken ct) => q.GetTradeHistoryAsync(limit ?? 200, ct));
        api.MapGet("/risk", (DashboardQueries q, CancellationToken ct) => q.GetRiskStatusAsync(ct));
        api.MapGet("/performance", (DashboardQueries q, CancellationToken ct) => q.GetPerformanceAsync(ct));
        api.MapGet("/audit", (DashboardQueries q, int? limit, CancellationToken ct) => q.GetAuditAsync(limit ?? 100, ct));

        api.MapGet("/learning", async (IVirtualTradeStore store, LearningOptions options, IClock clock, CancellationToken ct) =>
        {
            var now = clock.UtcNow;
            var outcomes = await store.GetOutcomesAsync(now.AddDays(-options.LookbackDays), ct);
            var model = StrategyPerformanceModel.Compute(outcomes, now, options);
            return new
            {
                options.Enabled,
                options.LookbackDays,
                options.PriorStrength,
                options.MaxBoost,
                options.MaxPenalty,
                options.DisableAfterSamples,
                options.DisableBelowR,
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
                    })
            };
        });

        api.MapPost("/kill-switch", SetKillSwitchAsync);

        api.MapPost("/backtests", async (BacktestRequest request, BacktestService service, CancellationToken ct) =>
        {
            var (run, error) = await service.RunAsync(request, ct);
            return run is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [error!] }) : Results.Ok(run);
        });
        api.MapGet("/backtests", (BacktestService service, int? limit, CancellationToken ct) => service.ListAsync(limit ?? 20, ct));

        return app;
    }

    /// <summary>
    /// Activating the kill switch always succeeds. Deactivating requires a reason and is audited.
    /// The worker reads this state before every risk check and again immediately before execution.
    /// </summary>
    private static async Task<IResult> SetKillSwitchAsync(
        KillSwitchRequest request,
        ITradingStateStore state,
        IDecisionJournal journal,
        IClock clock,
        HttpContext http,
        ILogger<KillSwitchRequest> logger,
        CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            if (!request.Active)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A reason is required to deactivate the kill switch."] });
            }

            reason = "Manual activation from dashboard.";
        }

        if (reason.Length > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason must be at most 500 characters."] });
        }

        var actor = $"dashboard@{http.Connection.RemoteIpAddress}";
        var updated = await state.UpdateAsync(s => s.WithKillSwitch(request.Active, reason, clock.UtcNow), ct);
        await journal.RecordAuditAsync(actor, request.Active ? "KillSwitchActivated" : "KillSwitchDeactivated", reason, http.CorrelationId(), ct);
        logger.LogWarning("Kill switch set to {Active} by {Actor}: {Reason}", request.Active, actor, reason);
        return Results.Ok(new { updated.KillSwitchActive, updated.KillSwitchReason, updated.KillSwitchChangedUtc });
    }
}
