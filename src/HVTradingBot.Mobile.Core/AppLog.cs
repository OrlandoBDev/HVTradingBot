using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

public sealed record AppLogEntry(DateTime TimestampUtc, LogLevel Level, string Category, string Message);

/// <summary>
/// Keeps the most recent log lines in memory for the app's Logs view (there is no console on a phone). Also forwards
/// every line to <see cref="Written"/>, which the Android app sends to logcat.
/// </summary>
public sealed class AppLog : ILoggerProvider
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<AppLogEntry> _entries = new();

    public event Action<AppLogEntry>? Written;

    public IReadOnlyList<AppLogEntry> Recent(int count = 200) => _entries.TakeLast(Math.Clamp(count, 1, Capacity)).ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(AppLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }

        Written?.Invoke(entry);
    }

    private sealed class Logger(AppLog log, string category) : ILogger
    {
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" ({exception.GetType().Name}: {exception.Message})";
            }

            log.Add(new AppLogEntry(DateTime.UtcNow, logLevel, _shortCategory, message));
        }
    }
}
