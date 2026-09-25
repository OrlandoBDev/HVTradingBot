using System.Text.Json;

namespace HVTradingBot.App.Core.Health;

/// <summary>
/// Parses the JSON written by the API's <c>/health/ready</c> endpoint (see <c>Program.cs</c> and
/// <c>WorkerHealthCheck</c> in HVTradingBot.Api):
/// <code>{"status":"Degraded","checks":[{"name":"database","status":"Healthy","description":null},
///   {"name":"trading-worker","status":"Degraded","description":"Market data is stale; new trades are blocked."}]}</code>
/// The endpoint answers 200 for Healthy/Degraded and 503 for Unhealthy, with this body either way.
/// </summary>
public static class ReadyResponseParser
{
    public const string WorkerCheckName = "trading-worker";
    public const string DatabaseCheckName = "database";

    /// <summary>Prefix of <c>WorkerHealthCheck</c>'s kill-switch description (the other Degraded case is stale data).</summary>
    public const string KillSwitchPrefix = "Kill switch active";

    /// <exception cref="JsonException">The body is not the expected JSON.</exception>
    public static WorkerStatus Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Find(root, "checks") is not { ValueKind: JsonValueKind.Array } checks)
        {
            throw new JsonException("The /health/ready response has no \"checks\" array.");
        }

        (string Status, string? Description)? worker = null;
        var databaseHealthy = true;
        foreach (var check in checks.EnumerateArray())
        {
            var name = GetString(check, "name");
            var status = GetString(check, "status") ?? "Unhealthy";
            var description = GetString(check, "description");
            if (name == WorkerCheckName)
            {
                worker = (status, description);
            }
            else if (name == DatabaseCheckName)
            {
                databaseHealthy = status == "Healthy";
            }
        }

        if (worker is not { } w)
        {
            throw new JsonException($"The /health/ready response has no \"{WorkerCheckName}\" check.");
        }

        var state = w.Status switch
        {
            "Healthy" => WorkerState.Running,
            "Degraded" when w.Description?.StartsWith(KillSwitchPrefix, StringComparison.Ordinal) == true => WorkerState.KillSwitchActive,
            "Degraded" => WorkerState.DataStale,
            _ => WorkerState.Offline
        };
        var detail = w.Description ?? (databaseHealthy ? w.Status : "Database unavailable");
        return new WorkerStatus(state, detail, databaseHealthy);
    }

    private static string? GetString(JsonElement element, string property) =>
        Find(element, property) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>ASP.NET Core writes camelCase; PascalCase is accepted too in case the serializer options change.</summary>
    private static JsonElement? Find(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value;
            }
        }

        return null;
    }
}
