namespace HVTradingBot.App.Core.Processes;

/// <summary>
/// The last <see cref="Capacity"/> output lines for the startup screen's log view. Thread-safe: lines arrive from
/// process output threads while the UI reads <see cref="Text"/> on a timer, which keeps a long image build from
/// re-rendering the label once per line.
/// </summary>
public sealed class OutputLog(int capacity = 500)
{
    private readonly Queue<string> lines = new();
    private readonly Lock gate = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>Incremented on every change, so a UI timer can skip unchanged refreshes.</summary>
    public long Version { get; private set; }

    public void Append(string line)
    {
        lock (gate)
        {
            lines.Enqueue(line);
            while (lines.Count > Capacity)
            {
                lines.Dequeue();
            }

            Version++;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            lines.Clear();
            Version++;
        }
    }

    public string Text
    {
        get
        {
            lock (gate)
            {
                return string.Join('\n', lines);
            }
        }
    }
}
