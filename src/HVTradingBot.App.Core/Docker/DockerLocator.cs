using HVTradingBot.App.Core.IO;

namespace HVTradingBot.App.Core.Docker;

/// <summary>Where <c>docker</c> is and the <c>PATH</c> every child process gets.</summary>
/// <param name="DockerPath">Full path of the <c>docker</c> executable.</param>
/// <param name="PathVariable">
/// <c>PATH</c> for child processes. <c>docker compose</c> needs <c>docker-credential-desktop</c> and the compose
/// plugin next to <c>docker</c>, and GUI apps on macOS do not inherit the shell's <c>PATH</c>.
/// </param>
public sealed record DockerInstallation(string DockerPath, string PathVariable)
{
    /// <summary>Environment passed to every child process.</summary>
    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string> { ["PATH"] = PathVariable };
}

/// <summary>Finds the <c>docker</c> CLI of Docker Desktop (Homebrew or the app bundle) without relying on the shell.</summary>
public sealed class DockerLocator(IFileSystem files)
{
    /// <summary>Searched in this order.</summary>
    public static readonly IReadOnlyList<string> SearchDirectories =
    [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/Applications/Docker.app/Contents/Resources/bin"
    ];

    /// <summary>Always on the child <c>PATH</c> (what a macOS GUI app gets by default).</summary>
    public static readonly IReadOnlyList<string> SystemDirectories = ["/usr/bin", "/bin", "/usr/sbin", "/sbin"];

    public const string InstallUrl = "https://www.docker.com/products/docker-desktop/";

    /// <summary>The Docker installation, or null when <c>docker</c> is in none of <see cref="SearchDirectories"/>.</summary>
    /// <param name="inheritedPath">The app's own <c>PATH</c> (kept at the end of the child <c>PATH</c>).</param>
    public DockerInstallation? Locate(string? inheritedPath)
    {
        var docker = SearchDirectories.Select(d => Path.Combine(d, "docker")).FirstOrDefault(files.FileExists);
        return docker is null ? null : new DockerInstallation(docker, BuildPath(inheritedPath));
    }

    /// <summary>The Docker directories first, then the system directories, then the inherited entries; no duplicates.</summary>
    public static string BuildPath(string? inheritedPath)
    {
        var inherited = (inheritedPath ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(':', SearchDirectories.Concat(SystemDirectories).Concat(inherited).Distinct(StringComparer.Ordinal));
    }
}
