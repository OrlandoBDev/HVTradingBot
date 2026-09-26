using HVTradingBot.Hosting;
using HVTradingBot.Mobile.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.Mobile.Tests;

public class EngineSupervisorTests
{
    [Fact]
    public async Task Engine_that_stops_on_its_own_is_started_again_until_the_app_stops_it()
    {
        var created = 0;
        var supervisor = new EngineSupervisor(() =>
        {
            Interlocked.Increment(ref created);
            return HostThat(lifetime => lifetime.StopApplication());
        }, NullLogger<EngineSupervisor>.Instance) { MinRestartDelay = TimeSpan.FromMilliseconds(10), MaxRestartDelay = TimeSpan.FromMilliseconds(20) };

        using var stop = new CancellationTokenSource();
        var run = supervisor.RunAsync(stop.Token);
        await WaitUntilAsync(() => created >= 3);
        await stop.CancelAsync();
        await run;

        Assert.True(supervisor.Restarts >= 2);
        Assert.Equal(EngineState.Stopped, supervisor.State);
    }

    [Fact]
    public async Task Failed_start_is_reported_and_retried()
    {
        var attempts = 0;
        var supervisor = new EngineSupervisor(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("Database locked");
            }

            return HostThat(_ => { });
        }, NullLogger<EngineSupervisor>.Instance) { MinRestartDelay = TimeSpan.FromMilliseconds(10) };

        using var stop = new CancellationTokenSource();
        string? error = null;
        supervisor.Changed += () => error ??= supervisor.LastError;
        var run = supervisor.RunAsync(stop.Token);
        await WaitUntilAsync(() => supervisor.State == EngineState.Running);

        Assert.Equal("Database locked", error);
        Assert.Null(supervisor.LastError); // cleared once the engine runs again
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Restart_requested_by_the_worker_does_not_back_off()
    {
        var created = 0;
        var supervisor = new EngineSupervisor(() =>
        {
            Interlocked.Increment(ref created);
            return HostThat(lifetime =>
            {
                Environment.ExitCode = BrokerSettingsWatcher.RestartExitCode;
                lifetime.StopApplication();
            });
        }, NullLogger<EngineSupervisor>.Instance) { MinRestartDelay = TimeSpan.FromMinutes(10) };

        using var stop = new CancellationTokenSource();
        var run = supervisor.RunAsync(stop.Token);
        await WaitUntilAsync(() => created >= 2); // well within the 10-minute failure back-off
        await stop.CancelAsync();
        await run;

        Assert.Equal(0, Environment.ExitCode);
    }

    private static IHost HostThat(Action<IHostApplicationLifetime> onStarted)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHostedService(sp => new Starter(sp.GetRequiredService<IHostApplicationLifetime>(), onStarted));
        return builder.Build();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out");
            await Task.Delay(10);
        }
    }

    private sealed class Starter(IHostApplicationLifetime lifetime, Action<IHostApplicationLifetime> onStarted) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            lifetime.ApplicationStarted.Register(() => onStarted(lifetime));
            return Task.CompletedTask;
        }
    }
}
