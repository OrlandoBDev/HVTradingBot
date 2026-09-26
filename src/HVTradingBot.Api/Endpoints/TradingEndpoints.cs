using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Application.MarketData;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;

namespace HVTradingBot.Api.Endpoints;

public static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (DashboardQueries q, CancellationToken ct) => q.GetStatusAsync(ct));
        api.MapGet("/markets", (DashboardQueries q, CancellationToken ct) => q.GetMarketsAsync(ct));
        api.MapGet("/markets/names", (DashboardActions actions, CancellationToken ct) => actions.GetMarketNamesAsync(ct));
        // Stored candles for charts, e.g. /api/candles?instrument=EUR/USD&timeframe=H1&limit=200 (oldest first).
        api.MapGet("/candles", async (CandleQueryService candles, string? instrument, string? timeframe, DateTime? from, DateTime? to, int? limit,
            CancellationToken ct) =>
        {
            if (CandleQueryService.ParseQuery(instrument, timeframe, from, to, limit, out var errors) is not { } query)
            {
                return Results.ValidationProblem(errors);
            }

            var bars = await candles.GetAsync(query, ct);
            return Results.Ok(new CandleSeriesDto(query.Symbol, query.TimeFrame.ToString(),
                bars.Select(b => new CandleDto(b.OpenTimeUtc, b.Open, b.High, b.Low, b.Close, b.Spread, b.Volume)).ToList()));
        });
        api.MapGet("/decisions", (DashboardQueries q, string? state, string? instrument, int? limit, CancellationToken ct) =>
            q.GetDecisionsAsync(state, instrument, limit ?? 100, ct));
        // Paged variants (the unpaged endpoints above stay for existing clients).
        api.MapGet("/decisions/paged", (DashboardQueries q, string? state, string? instrument, int? page, int? pageSize, CancellationToken ct) =>
            q.GetDecisionsPageAsync(state, instrument, page, pageSize, ct));
        api.MapGet("/trades/paged", (DashboardQueries q, int? page, int? pageSize, CancellationToken ct) => q.GetTradeHistoryPageAsync(page, pageSize, ct));
        api.MapGet("/audit/paged", (DashboardQueries q, int? page, int? pageSize, CancellationToken ct) => q.GetAuditPageAsync(page, pageSize, ct));

        api.MapGet("/decisions/{id:guid}", async (Guid id, DashboardQueries q, CancellationToken ct) =>
            await q.GetDecisionAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());
        api.MapGet("/positions/open", (DashboardQueries q, CancellationToken ct) => q.GetOpenPositionsAsync(ct));
        // The API only records the request; the worker (the only process that talks to the broker) closes the position.
        api.MapPost("/positions/{id:guid}/close", async (Guid id, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.RequestCloseAsync(id, http.Caller(), ct)).ToHttp());

        api.MapGet("/trades", (DashboardQueries q, int? limit, CancellationToken ct) => q.GetTradeHistoryAsync(limit ?? 200, ct));
        api.MapGet("/risk", (DashboardQueries q, CancellationToken ct) => q.GetRiskStatusAsync(ct));
        api.MapGet("/performance", (DashboardQueries q, CancellationToken ct) => q.GetPerformanceAsync(ct));
        api.MapGet("/profit", (DashboardQueries q, string? tz, CancellationToken ct) => q.GetProfitSummaryAsync(tz, ct));
        api.MapGet("/audit", (DashboardQueries q, int? limit, CancellationToken ct) => q.GetAuditAsync(limit ?? 100, ct));
        api.MapGet("/learning", (DashboardActions actions, CancellationToken ct) => actions.GetLearningAsync(ct));
        api.MapGet("/news", (NewsQueries news, CancellationToken ct) => news.GetNewsAsync(ct));

        api.MapGet("/test-trades", (DashboardActions actions, CancellationToken ct) => actions.GetTestTradesAsync(ct));
        // The API only records the request; the worker (the only process that talks to the broker) carries it out.
        api.MapPost("/test-trades", async (TestTradeRequest request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.RequestTestTradeAsync(request, http.Caller(), ct)).ToHttp());

        api.MapPost("/kill-switch", async (KillSwitchRequest request, DashboardActions actions, HttpContext http, CancellationToken ct) =>
            (await actions.SetKillSwitchAsync(request, http.Caller(), ct)).ToHttp());

        api.MapPost("/backtests", async (BacktestRequest request, BacktestService service, CancellationToken ct) =>
        {
            var (run, error) = await service.RunAsync(request, ct);
            return run is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [error!] }) : Results.Ok(run);
        });
        api.MapGet("/backtests", (BacktestService service, int? limit, CancellationToken ct) => service.ListAsync(limit ?? 20, ct));

        return app;
    }
}
