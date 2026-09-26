using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

/// <summary>A response for the dashboard: HTTP-style status code and a JSON body (null for 204).</summary>
public sealed record LocalApiResponse(int Status, string? Body);

/// <summary>
/// The dashboard's API, answered in-process: the web dashboard runs unchanged in the app's WebView and sends its
/// requests (same paths, same JSON) through the WebView bridge to this router instead of over HTTP. There is no
/// server, no open port and no login; the phone's own lock screen protects the app.
/// </summary>
public sealed partial class LocalApi
{
    /// <summary>The API's JSON conventions (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Audit identity for requests from the app.</summary>
    public const string Actor = "android-app";

    private readonly List<Route> _routes = [];
    private readonly ILogger<LocalApi> _logger;

    public LocalApi(DashboardQueries queries, DashboardActions actions, BacktestService backtests, EngineSupervisor engine, AppLog log,
        ILogger<LocalApi> logger)
    {
        _logger = logger;

        // The app has no dashboard login.
        Get("/api/auth/me", _ => Ok(new AuthStatusDto(true, "This phone", false)));
        Post("/api/auth/logout", _ => Task.FromResult(DashboardResult.NoContent));

        Get("/api/status", r => Ok(queries.GetStatusAsync(r.Ct)));
        Get("/api/markets", r => Ok(queries.GetMarketsAsync(r.Ct)));
        Get("/api/markets/names", r => Ok(actions.GetMarketNamesAsync(r.Ct)));
        Get("/api/decisions", r => Ok(queries.GetDecisionsAsync(r.Query("state"), r.Query("instrument"), r.Int("limit") ?? 100, r.Ct)));
        Get("/api/decisions/paged", r => Ok(queries.GetDecisionsPageAsync(r.Query("state"), r.Query("instrument"), r.Int("page"), r.Int("pageSize"), r.Ct)));
        Get("/api/trades/paged", r => Ok(queries.GetTradeHistoryPageAsync(r.Int("page"), r.Int("pageSize"), r.Ct)));
        Get("/api/audit/paged", r => Ok(queries.GetAuditPageAsync(r.Int("page"), r.Int("pageSize"), r.Ct)));
        Get("/api/decisions/{id}", async r =>
            await queries.GetDecisionAsync(r.Guid("id"), r.Ct) is { } d ? DashboardResult.Ok(d) : DashboardResult.NotFound);
        Get("/api/positions/open", r => Ok(queries.GetOpenPositionsAsync(r.Ct)));
        Post("/api/positions/{id}/close", r => actions.RequestCloseAsync(r.Guid("id"), r.Caller, r.Ct));
        Get("/api/trades", r => Ok(queries.GetTradeHistoryAsync(r.Int("limit") ?? 200, r.Ct)));
        Get("/api/risk", r => Ok(queries.GetRiskStatusAsync(r.Ct)));
        Get("/api/performance", r => Ok(queries.GetPerformanceAsync(r.Ct)));
        Get("/api/profit", r => Ok(queries.GetProfitSummaryAsync(r.Ct)));
        Get("/api/audit", r => Ok(queries.GetAuditAsync(r.Int("limit") ?? 100, r.Ct)));
        Get("/api/learning", r => Ok(actions.GetLearningAsync(r.Ct)));
        Get("/api/test-trades", r => Ok(actions.GetTestTradesAsync(r.Ct)));
        Post("/api/test-trades", r => actions.RequestTestTradeAsync(r.Body<TestTradeRequest>(), r.Caller, r.Ct));
        Post("/api/kill-switch", r => actions.SetKillSwitchAsync(r.Body<KillSwitchRequest>(), r.Caller, r.Ct));
        Post("/api/backtests", async r =>
        {
            var (run, error) = await backtests.RunAsync(r.Body<BacktestRequest>(), r.Ct);
            return run is null ? DashboardResult.Invalid("request", error!) : DashboardResult.Ok(run);
        });
        Get("/api/backtests", r => Ok(backtests.ListAsync(r.Int("limit") ?? 20, r.Ct)));

        Get("/api/settings/deriv", r => Ok(actions.GetDerivSettingsAsync(r.Ct)));
        Put("/api/settings/deriv", r => actions.SaveDerivSettingsAsync(r.Body<DerivSettingsRequest>(), r.Caller, r.Ct));
        Delete("/api/settings/deriv", async r =>
        {
            await actions.ClearDerivSettingsAsync(r.Caller, r.Ct);
            return DashboardResult.NoContent;
        });
        Get("/api/settings/risk", r => Ok(actions.GetRiskSettingsAsync(r.Ct)));
        Put("/api/settings/risk", r => actions.SaveRiskSettingsAsync(r.Body<RiskLimitsDto>(), r.Caller, r.Ct));
        Delete("/api/settings/risk", r => Ok(actions.ResetRiskSettingsAsync(r.Caller, r.Ct)));
        Get("/api/settings/notifications", r => Ok(actions.GetNotificationSettingsAsync(r.Ct)));
        Put("/api/settings/notifications", r => actions.SaveNotificationSettingsAsync(r.Body<NotificationSettingsRequest>(), r.Caller, r.Ct));
        Post("/api/settings/notifications/test", r => Ok(actions.SendTestEmailAsync(r.Caller, r.Ct)));
        Get("/api/settings/markets", r => Ok(actions.GetMarketSettingsAsync(r.Ct)));
        Put("/api/settings/markets", r => actions.SaveMarketSelectionAsync(r.Body<MarketSelectionRequest>(), r.Caller, r.Ct));

        // App only: the engine's state (the web dashboard's equivalent is the Docker/Mac app status) and recent log lines.
        Get("/api/app/engine", _ => Ok(new { State = engine.State.ToString(), engine.LastError, engine.Restarts }));
        Get("/api/app/logs", r => Ok(log.Recent(r.Int("count") ?? 200)));
    }

    /// <summary>Handles one dashboard request. Never throws: failures become 4xx/5xx responses like the API's.</summary>
    public async Task<LocalApiResponse> HandleAsync(string method, string url, string? body, CancellationToken cancellationToken)
    {
        var queryStart = url.IndexOf('?');
        var path = (queryStart < 0 ? url : url[..queryStart]).TrimEnd('/');
        var query = ParseQuery(queryStart < 0 ? "" : url[(queryStart + 1)..]);

        var pathMatched = false;
        foreach (var route in _routes)
        {
            var match = route.Pattern.Match(path);
            if (!match.Success)
            {
                continue;
            }

            pathMatched = true;
            if (!string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var request = new Request(match, query, body, new DashboardCaller(Actor, Guid.NewGuid().ToString("N")), cancellationToken);
                return ToResponse(await route.Handler(request));
            }
            catch (BadRequestException ex)
            {
                return Problem(400, ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Problem(499, "Request cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard request {Method} {Path} failed", method, path);
                return Problem(500, ex.Message);
            }
        }

        return pathMatched ? Problem(405, $"{method} is not supported for {path}.") : Problem(404, $"{path} was not found.");
    }

    private static LocalApiResponse ToResponse(DashboardResult result) => result switch
    {
        DashboardResult.OkResult ok => new LocalApiResponse(200, JsonSerializer.Serialize(ok.Value, Json)),
        DashboardResult.NoContentResult => new LocalApiResponse(204, null),
        DashboardResult.NotFoundResult => Problem(404, "Not found."),
        // Same shape as ASP.NET Core's ValidationProblem, which the dashboard shows next to the form.
        DashboardResult.InvalidResult invalid => new LocalApiResponse(400, JsonSerializer.Serialize(
            new { title = "One or more validation errors occurred.", status = 400, errors = invalid.Errors }, Json)),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
    };

    private static LocalApiResponse Problem(int status, string title) =>
        new(status, JsonSerializer.Serialize(new { title, status }, Json));

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? part : part[..eq]).Replace('+', ' '));
            values[key] = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }

        return values;
    }

    private static Task<DashboardResult> Ok<T>(T value) => Task.FromResult(DashboardResult.Ok(value));

    private static async Task<DashboardResult> Ok<T>(Task<T> value) => DashboardResult.Ok(await value);

    private void Get(string template, Func<Request, Task<DashboardResult>> handler) => Add("GET", template, handler);

    private void Post(string template, Func<Request, Task<DashboardResult>> handler) => Add("POST", template, handler);

    private void Put(string template, Func<Request, Task<DashboardResult>> handler) => Add("PUT", template, handler);

    private void Delete(string template, Func<Request, Task<DashboardResult>> handler) => Add("DELETE", template, handler);

    private void Add(string method, string template, Func<Request, Task<DashboardResult>> handler)
    {
        var pattern = "^" + ParameterPattern().Replace(Regex.Escape(template).Replace(@"\{", "{"), "(?<$1>[^/]+)") + "$";
        _routes.Add(new Route(method, new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), handler));
    }

    [GeneratedRegex(@"\{(\w+)}")]
    private static partial Regex ParameterPattern();

    private sealed record Route(string Method, Regex Pattern, Func<Request, Task<DashboardResult>> Handler);

    private sealed class BadRequestException(string message) : Exception(message);

    private sealed record Request(Match Match, IReadOnlyDictionary<string, string> Values, string? RawBody, DashboardCaller Caller, CancellationToken Ct)
    {
        public string? Query(string name) => Values.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

        public int? Int(string name) => Query(name) is { } value
            ? int.TryParse(value, out var number) ? number : throw new BadRequestException($"'{name}' must be a number.")
            : null;

        public Guid Guid(string name) => System.Guid.TryParse(Match.Groups[name].Value, out var id)
            ? id
            : throw new BadRequestException($"'{name}' must be an id.");

        public T Body<T>()
        {
            try
            {
                return JsonSerializer.Deserialize<T>(string.IsNullOrEmpty(RawBody) ? "null" : RawBody, Json)
                       ?? throw new BadRequestException("A request body is required.");
            }
            catch (JsonException ex)
            {
                throw new BadRequestException($"The request body is not valid: {ex.Message}");
            }
        }
    }
}
