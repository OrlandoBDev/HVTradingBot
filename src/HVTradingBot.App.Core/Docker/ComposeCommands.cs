using HVTradingBot.App.Core.Processes;

namespace HVTradingBot.App.Core.Docker;

/// <summary>Builds the <c>docker</c> / <c>docker compose</c> command lines for the stack in one repository checkout.</summary>
public sealed class ComposeCommands(DockerInstallation docker, string repositoryPath)
{
    /// <summary>The compose project name (<c>name:</c> in <c>docker-compose.yml</c>).</summary>
    public const string ProjectName = "hvtradingbot";

    public const string ApiService = "api";
    public const string WorkerService = "worker";
    public const int LogTailLines = 200;

    public string RepositoryPath { get; } = repositoryPath;

    public DockerInstallation Docker { get; } = docker;

    /// <summary><c>docker info</c>: succeeds only when the Docker daemon is running.</summary>
    public ProcessCommand Info() => DockerCommand("info", "--format", "{{.ServerVersion}}");

    /// <summary>Starts Docker Desktop.</summary>
    public ProcessCommand OpenDockerDesktop() => new("/usr/bin/open", ["-a", "Docker"], null, Docker.Environment);

    /// <summary>Builds the images if needed and starts postgres, api and worker in the background.</summary>
    public ProcessCommand Up() => Compose("up", "-d", "--build");

    /// <summary>Stops the containers (trading stops; data and containers are kept).</summary>
    public ProcessCommand Stop() => Compose("stop");

    /// <summary>Starts the containers created by an earlier <see cref="Up"/>.</summary>
    public ProcessCommand Start() => Compose("start");

    public ProcessCommand RestartWorker() => Compose("restart", WorkerService);

    /// <summary>The last <see cref="LogTailLines"/> log lines of one service.</summary>
    public ProcessCommand Logs(string service)
    {
        if (service is not (ApiService or WorkerService))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only the api and worker logs are shown.");
        }

        return Compose("logs", "--no-color", "--tail", LogTailLines.ToString(System.Globalization.CultureInfo.InvariantCulture), service);
    }

    /// <summary>Lists running containers publishing <paramref name="hostPort"/> as <c>project/service</c> lines.</summary>
    public ProcessCommand ContainersPublishing(int hostPort) => DockerCommand(
        "ps", "--filter", $"publish={hostPort}",
        "--format", "{{.Label \"com.docker.compose.project\"}}/{{.Label \"com.docker.compose.service\"}}");

    private ProcessCommand Compose(params string[] arguments) =>
        DockerCommand(["compose", "--project-directory", RepositoryPath, .. arguments]);

    private ProcessCommand DockerCommand(params string[] arguments) =>
        new(Docker.DockerPath, arguments, RepositoryPath, Docker.Environment);
}
