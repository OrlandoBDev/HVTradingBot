namespace HVTradingBot.Infrastructure.Configuration;

/// <summary>
/// Development convenience: reads the repository's <c>.env</c> (the file <c>./run.sh</c> creates and uses) so the API
/// and worker also start from an IDE (Rider, Visual Studio, VS Code) without copying the database password around.
/// </summary>
public static class DotEnvFile
{
    public const string FileName = ".env";

    /// <summary>Looks for <c>.env</c> in <paramref name="startDirectories"/> and their parents; null if none exists.</summary>
    public static string? Find(params string[] startDirectories)
    {
        foreach (var start in startDirectories)
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Parses KEY=value lines; blank lines and # comments are skipped, surrounding quotes removed.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            {
                value = value[1..^1];
            }

            values[line[..eq].Trim()] = value;
        }

        return values;
    }

    /// <summary>Maps the <c>.env</c> variables to the settings <c>./run.sh</c> would export.</summary>
    public static IReadOnlyDictionary<string, string?> ToSettings(IReadOnlyDictionary<string, string> env)
    {
        var settings = new Dictionary<string, string?>();
        if (env.TryGetValue("POSTGRES_PASSWORD", out var password) && password.Length > 0)
        {
            var port = env.GetValueOrDefault("POSTGRES_PORT", "5433");
            var db = env.GetValueOrDefault("POSTGRES_DB", "hvtradingbot");
            var user = env.GetValueOrDefault("POSTGRES_USER", "hvtradingbot");
            settings["ConnectionStrings:TradingDb"] = $"Host=127.0.0.1;Port={port};Database={db};Username={user};Password={password}";
        }

        Map("BROKER_PROVIDER", "Broker:Provider");
        Map("MARKET_DATA_PROVIDER", "MarketData:Provider");
        Map("DERIV_APP_ID", "Deriv:AppId");
        Map("DERIV_API_TOKEN", "Deriv:ApiToken");
        Map("DERIV_ACCOUNT_ID", "Deriv:AccountId");
        return settings;

        void Map(string variable, string key)
        {
            if (env.TryGetValue(variable, out var value) && value.Length > 0)
            {
                settings[key] = value;
            }
        }
    }
}
