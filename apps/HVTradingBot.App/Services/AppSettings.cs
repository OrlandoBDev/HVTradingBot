using HVTradingBot.App.Core.Configuration;
using Microsoft.Maui.Storage;

namespace HVTradingBot.App.Services;

/// <summary>User settings, stored with MAUI <see cref="IPreferences"/> (NSUserDefaults on macOS).</summary>
public sealed class AppSettings(IPreferences preferences)
{
    private const string RepositoryPathKey = "RepositoryPath";
    private const string StopOnQuitKey = "StopTradingOnQuit";
    private const string QuitExplainedKey = "QuitExplained";

    /// <summary>The HVTradingBot checkout the stack is built from; default <c>~/HVTradingBot</c>.</summary>
    public string RepositoryPath
    {
        get => preferences.Get(RepositoryPathKey, RepositoryEnvironment.DefaultRepositoryPath);
        set => preferences.Set(RepositoryPathKey, ExpandHome(value.Trim()));
    }

    /// <summary>"Stop trading when the app quits"; off by default so trading continues without the app.</summary>
    public bool StopTradingOnQuit
    {
        get => preferences.Get(StopOnQuitKey, false);
        set => preferences.Set(StopOnQuitKey, value);
    }

    /// <summary>Whether the user has been told that quitting leaves the stack running.</summary>
    public bool QuitExplained
    {
        get => preferences.Get(QuitExplainedKey, false);
        set => preferences.Set(QuitExplainedKey, value);
    }

    private static string ExpandHome(string path) =>
        path == "~" || path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~').TrimStart('/'))
            : path;
}
