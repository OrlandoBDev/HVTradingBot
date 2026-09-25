using System.Globalization;
using System.Security.Cryptography;
using HVTradingBot.App.Core.IO;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Configuration;

/// <summary>
/// The repository checkout the stack is built from and its <c>.env</c> (the same file <c>./run.sh</c> creates).
/// </summary>
public sealed class RepositoryEnvironment(IFileSystem files, ILogger<RepositoryEnvironment> logger)
{
    public const string ComposeFileName = "docker-compose.yml";
    public const string EnvFileName = ".env";
    public const string EnvExampleFileName = ".env.example";
    public const int DefaultApiPort = 5080;

    /// <summary>Default checkout location: <c>~/HVTradingBot</c>.</summary>
    public static string DefaultRepositoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HVTradingBot");

    public bool HasComposeFile(string repositoryPath) => files.FileExists(Path.Combine(repositoryPath, ComposeFileName));

    /// <summary>
    /// Creates <c>.env</c> from <c>.env.example</c> with a random <c>POSTGRES_PASSWORD</c> and mode 600 when it is missing,
    /// like <c>ensure_env</c> in <c>run.sh</c>. Returns true when the file was created.
    /// </summary>
    /// <exception cref="FileNotFoundException">Neither <c>.env</c> nor <c>.env.example</c> exists.</exception>
    public bool EnsureEnvFile(string repositoryPath)
    {
        var envPath = Path.Combine(repositoryPath, EnvFileName);
        if (files.FileExists(envPath))
        {
            return false;
        }

        var examplePath = Path.Combine(repositoryPath, EnvExampleFileName);
        if (!files.FileExists(examplePath))
        {
            throw new FileNotFoundException($"{EnvExampleFileName} is missing, so {EnvFileName} cannot be created.", examplePath);
        }

        files.CreatePrivateFile(envPath, WithPassword(files.ReadAllText(examplePath), NewPassword()));
        logger.LogInformation("Created {EnvFile} with a random database password", envPath);
        return true;
    }

    /// <summary>The variables in the repository's <c>.env</c>; empty when it does not exist.</summary>
    public IReadOnlyDictionary<string, string> ReadEnvFile(string repositoryPath)
    {
        var envPath = Path.Combine(repositoryPath, EnvFileName);
        return files.FileExists(envPath) ? Parse(files.ReadAllText(envPath)) : new Dictionary<string, string>();
    }

    /// <summary><c>API_PORT</c> from <c>.env</c>, or <see cref="DefaultApiPort"/> when unset or invalid.</summary>
    public int ReadApiPort(string repositoryPath) => ApiPort(ReadEnvFile(repositoryPath));

    public static int ApiPort(IReadOnlyDictionary<string, string> env) =>
        env.TryGetValue("API_PORT", out var value)
        && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
        && port is > 0 and <= 65535
            ? port
            : DefaultApiPort;

    /// <summary>48 hex characters, the same shape as <c>openssl rand -hex 24</c>.</summary>
    public static string NewPassword() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    /// <summary>Replaces the <c>POSTGRES_PASSWORD=</c> line (appends one if the template has none).</summary>
    public static string WithPassword(string template, string password)
    {
        var newline = template.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = template.Split(newline).ToList();
        var index = lines.FindIndex(l => l.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines[index] = $"POSTGRES_PASSWORD={password}";
        }
        else
        {
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            lines.Add($"POSTGRES_PASSWORD={password}");
            lines.Add("");
        }

        return string.Join(newline, lines);
    }

    /// <summary>Parses KEY=value lines; blank lines and # comments are skipped, surrounding quotes removed.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string contents)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in contents.Split('\n'))
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
}
