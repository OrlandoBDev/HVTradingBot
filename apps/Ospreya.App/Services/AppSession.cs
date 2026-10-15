using Ospreya.App.Core.Docker;
using Ospreya.App.Core.Processes;
using Ospreya.App.Core.Startup;
using Microsoft.Extensions.Logging;

namespace Ospreya.App.Services;

/// <summary>What the last successful startup produced: the dashboard URL and the controls for that stack.</summary>
public sealed class AppSession(IProcessRunner runner, ILoggerFactory loggers)
{
    public StartupResult? Startup { get; private set; }

    public StackController? Stack { get; private set; }

    public Uri? ApiBaseUri => Startup?.ApiBaseUri;

    public void SetStarted(StartupResult result)
    {
        Startup = result;
        Stack = result.Commands is { } commands
            ? new StackController(runner, commands, loggers.CreateLogger<StackController>())
            : null;
    }
}
