using System.Text.Json;

namespace HVTradingBot.Mobile.Core;

/// <summary>
/// Live updates for the dashboard in the WebView (what SignalR does in the browser): the latest status and learning
/// pulse, each with a sequence number. The dashboard long-polls "/api/app/events?after=N" and gets whatever is newer,
/// or waits for the next update.
/// </summary>
public sealed class EventFeed
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long Sequence, string Json)> _latest = new();
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _sequence;

    public EventFeed(LiveUpdates live) => live.Published += Publish;

    internal EventFeed()
    {
    }

    internal void Publish(string name, string json)
    {
        TaskCompletionSource released;
        lock (_gate)
        {
            _latest[name] = (++_sequence, json);
            released = _next;
            _next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        released.TrySetResult();
    }

    /// <summary>
    /// Events newer than <paramref name="after"/> (latest per name), waiting up to <paramref name="timeout"/> for one.
    /// A client whose sequence is from before an app restart (larger than ours) gets everything.
    /// </summary>
    public async Task<string> WaitAsync(long after, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task next;
        lock (_gate)
        {
            if (after > _sequence)
            {
                after = 0;
            }

            if (_sequence > after)
            {
                return Snapshot(after);
            }

            next = _next.Task;
        }

        await Task.WhenAny(next, Task.Delay(timeout, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Snapshot(after);
        }
    }

    private string Snapshot(long after)
    {
        // The payloads are already JSON: write them in place instead of serializing strings.
        var events = _latest.Where(e => e.Value.Sequence > after).OrderBy(e => e.Value.Sequence)
            .Select(e => $"{{\"name\":{JsonSerializer.Serialize(e.Key)},\"data\":{e.Value.Json}}}");
        return $"{{\"sequence\":{_sequence},\"events\":[{string.Join(',', events)}]}}";
    }
}
