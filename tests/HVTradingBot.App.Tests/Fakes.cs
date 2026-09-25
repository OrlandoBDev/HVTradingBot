using System.Net;
using HVTradingBot.App.Core.IO;
using HVTradingBot.App.Core.Processes;
using HVTradingBot.App.Core.Startup;
using Microsoft.Extensions.Time.Testing;

namespace HVTradingBot.App.Tests;

internal sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> PrivateFiles { get; } = new(StringComparer.Ordinal);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public string ReadAllText(string path) =>
        Files.TryGetValue(path, out var contents) ? contents : throw new FileNotFoundException(path);

    public void CreatePrivateFile(string path, string contents)
    {
        if (!Files.TryAdd(path, contents))
        {
            throw new IOException($"{path} exists");
        }

        PrivateFiles.Add(path);
    }
}

/// <summary>Answers each command with the first matching rule; records every call.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<ProcessCommand, bool> Match, Func<ProcessCommand, ProcessResult> Result)> rules = [];

    public List<ProcessCommand> Calls { get; } = [];

    public FakeProcessRunner On(Func<ProcessCommand, bool> match, Func<ProcessCommand, ProcessResult> result)
    {
        rules.Add((match, result));
        return this;
    }

    public FakeProcessRunner On(string argumentsStartWith, int exitCode, params string[] output) =>
        On(c => Joined(c).StartsWith(argumentsStartWith, StringComparison.Ordinal), _ => new ProcessResult(exitCode, output));

    public static string Joined(ProcessCommand c) => string.Join(' ', c.Arguments);

    public Task<ProcessResult> RunAsync(ProcessCommand command, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Calls)
        {
            Calls.Add(command);
        }

        var rule = rules.FirstOrDefault(r => r.Match(command));
        if (rule.Result is null)
        {
            throw new InvalidOperationException($"Unexpected command: {command}");
        }

        var result = rule.Result(command);
        foreach (var line in result.Output)
        {
            onOutput?.Invoke(line);
        }

        return Task.FromResult(result);
    }
}

internal sealed class FakePortProbe(params int[] portsInUse) : IPortProbe
{
    public Task<bool> IsInUseAsync(int port, CancellationToken cancellationToken) => Task.FromResult(portsInUse.Contains(port));
}

/// <summary>Answers requests by path; a missing route throws <see cref="HttpRequestException"/> (connection refused).</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);

    public List<Uri> Requests { get; } = [];

    public FakeHttpHandler Route(string path, HttpStatusCode status, string body = "") =>
        Route(path, () => new HttpResponseMessage(status) { Content = new StringContent(body) });

    public FakeHttpHandler Route(string path, Func<HttpResponseMessage> response)
    {
        Routes[path] = response;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        return Routes.TryGetValue(request.RequestUri!.AbsolutePath, out var response)
            ? Task.FromResult(response())
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
    }
}

internal static class FakeTime
{
    /// <summary>Advances fake time in <paramref name="step"/>s until <paramref name="task"/> completes.</summary>
    public static async Task<T> DriveAsync<T>(this FakeTimeProvider time, Task<T> task, TimeSpan step, int maxSteps = 1000)
    {
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
        {
            // Let the task reach its next Task.Delay before moving time on.
            await Task.Delay(1);
            time.Advance(step);
        }

        return await task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
