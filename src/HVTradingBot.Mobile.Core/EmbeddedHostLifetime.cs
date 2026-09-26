using Microsoft.Extensions.Hosting;

namespace HVTradingBot.Mobile.Core;

/// <summary>
/// The engine host lives inside the app: it stops when the app says so (or when the worker asks for a restart), not on
/// console or process signals, which is what the default console lifetime would listen to.
/// </summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
