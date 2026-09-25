using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Api.Services;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Learning;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Infrastructure.TestTrades;
using HVTradingBot.Infrastructure.Trades;
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
        api.MapPost("/positions/{id:guid}/close", async (Guid id, CloseRequestStore store, DashboardQueries queries, IDecisionJournal journal,
            HttpContext http, CancellationToken ct) =>
        {
            // The API only records the request; the worker (the only process that talks to the broker) closes the position.
            var status = await queries.GetStatusAsync(ct);
            if (!status.WorkerHealthy)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["position"] = ["The trading worker is not running."] });
            }

            var actor = $"dashboard@{http.Connection.RemoteIpAddress}";
            var (request, problem) = await store.CreateAsync(id, actor, ct);
            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["position"] = [problem!] });
            }

            await journal.RecordAuditAsync(actor, "CloseRequested", $"{request.Instrument} position {id}", http.CorrelationId(), ct);
            return Results.Ok(new { request.Id, request.Status, request.Message });
        });

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

        api.MapGet("/test-trades", async (TestTradeStore store, CancellationToken ct) =>
            (await store.RecentAsync(5, ct)).Select(ToDto));

        api.MapPost("/test-trades", async (TestTradeRequest request, TestTradeStore store, DashboardQueries queries, HttpContext http,
            CancellationToken ct) =>
        {
            // The API only records the request; the worker (the only process that talks to the broker) carries it out.
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
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["instrument"] = [problem] });
            }

            var status = await queries.GetStatusAsync(ct);
            if (!status.WorkerHealthy)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["instrument"] = ["The trading worker is not running."] });
            }

            var created = await store.CreateAsync(request.Instrument, $"dashboard@{http.Connection.RemoteIpAddress}", ct);
            return created is null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["instrument"] = ["A test trade is already in progress."] })
                : Results.Ok(ToDto(created));
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

    private static TestTradeDto ToDto(HVTradingBot.Infrastructure.Persistence.Entities.TestTradeEntity t) => new(t.Id, t.Instrument, t.Status, t.Message,
        t.RequestedBy, t.RequestedAtUtc, t.OpenedAtUtc, t.ClosedAtUtc, t.ClientOrderId, t.FillPrice, t.ExitPrice, t.RealizedPnl, t.HoldSeconds);

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
