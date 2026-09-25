using System.Net.Sockets;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Processes;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Core.Startup;

/// <summary>Tells whether something listens on a local TCP port.</summary>
public interface IPortProbe
{
    Task<bool> IsInUseAsync(int port, CancellationToken cancellationToken);
}

/// <summary>Connects to 127.0.0.1; a refused connection means the port is free.</summary>
public sealed class TcpPortProbe(TimeProvider time) : IPortProbe
{
    public async Task<bool> IsInUseAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", port, linked.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Nobody accepted within the timeout (e.g. a firewall dropping packets): nothing usable is listening.
            return false;
        }
    }
}

/// <summary>
/// Startup step 4: a native <c>dotnet run</c> of the worker or API, or another program on <c>API_PORT</c>, must not run
/// alongside the Docker stack. Two workers would trade the same Deriv account.
/// </summary>
public sealed class ConflictDetector(IProcessRunner runner, IPortProbe ports, ILogger<ConflictDetector> logger)
{
    /// <summary>
    /// Matches the apphost (<c>.../HVTradingBot.Worker</c>) and <c>dotnet HVTradingBot.Worker.dll</c>, not directory
    /// names such as <c>src/HVTradingBot.Worker/</c>. Containers run inside Docker Desktop's VM and never match.
    /// </summary>
    public const string NativeProcessPattern = @"HVTradingBot\.(Worker|Api)(\.dll)?( |$)";

    public static ProcessCommand NativeProcessQuery(DockerInstallation docker) =>
        new("/usr/bin/pgrep", ["-f", "-l", NativeProcessPattern], null, docker.Environment);

    /// <summary>A message explaining the conflict, or null when it is safe to start the stack.</summary>
    public async Task<string?> FindConflictAsync(ComposeCommands commands, int apiPort, CancellationToken cancellationToken)
    {
        var native = await runner.RunAsync(NativeProcessQuery(commands.Docker), null, cancellationToken);
        switch (native.ExitCode)
        {
            case 0:
                var processes = native.Output.Where(l => l.Length > 0).ToArray();
                logger.LogWarning("Native HVTradingBot processes are running: {Processes}", string.Join("; ", processes));
                return "HVTradingBot is already running outside Docker (./run.sh or dotnet run). Stop it first; two workers " +
                       "would trade the same Deriv account.\n" + string.Join('\n', processes);
            case 1: // pgrep: nothing matched
                break;
            default:
                throw new InvalidOperationException(
                    $"pgrep failed with exit code {native.ExitCode}: {string.Join(' ', native.Output)}");
        }

        if (!await ports.IsInUseAsync(apiPort, cancellationToken))
        {
            return null;
        }

        var owners = await runner.RunAsync(commands.ContainersPublishing(apiPort), null, cancellationToken);
        if (!owners.Succeeded)
        {
            throw new InvalidOperationException(
                $"docker ps failed with exit code {owners.ExitCode}: {string.Join(' ', owners.Output)}");
        }

        if (owners.Output.Any(l => l.Trim() == $"{ComposeCommands.ProjectName}/{ComposeCommands.ApiService}"))
        {
            logger.LogInformation("Port {ApiPort} is held by the api container; the stack is already running", apiPort);
            return null;
        }

        var container = owners.Output.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        var owner = container switch
        {
            null => "another program",
            "/" => "a Docker container", // not started by docker compose, so no project/service labels
            _ => $"the container {container}"
        };
        logger.LogWarning("Port {ApiPort} is held by {Owner}", apiPort, owner);
        return $"Port {apiPort} (API_PORT in .env) is already in use by {owner}. Stop it, or change API_PORT in .env.";
    }
}
