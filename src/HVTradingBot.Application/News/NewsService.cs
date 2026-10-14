using HVTradingBot.Domain.News;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Application.News;

/// <summary>A provider of economic calendar events and market headlines (see <see cref="NewsProvider"/>).</summary>
public interface INewsSource
{
    string Name { get; }

    /// <summary>
    /// True for sources generated in market time (the simulated news), which are refreshed as market time passes;
    /// real sources are refreshed by the wall clock only, so a fast replay never floods them.
    /// </summary>
    bool FollowsMarketTime => false;

    /// <summary>
    /// Calendar events around <paramref name="nowUtc"/> and headlines published up to it. Throws when the source cannot
    /// be read at all; a source that reads only part (e.g. headlines but not the calendar) says so in the snapshot.
    /// </summary>
    Task<NewsSnapshot> FetchAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

/// <summary>Used when news is switched off (News:Enabled false or News:Provider None).</summary>
public sealed class NoNewsSource : INewsSource
{
    public string Name => "none";

    public Task<NewsSnapshot> FetchAsync(DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult(NewsSnapshot.Empty);
}

/// <summary>
/// Keeps the latest news snapshot and refreshes it every <see cref="NewsOptions.RefreshMinutes"/> of wall-clock time,
/// or of market time for sources that follow it (<see cref="INewsSource.FollowsMarketTime"/>). A failed refresh keeps
/// the previous calendar for up to a day, since release times do not change, and is retried after a minute.
/// News problems never stop the engine; they only lose the news input.
/// </summary>
public sealed class NewsService(INewsSource source, NewsOptions options, ILogger<NewsService> logger, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeepCalendarFor = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile NewsSnapshot _current = NewsSnapshot.Empty;
    private DateTime? _lastAttemptMarketUtc;
    private DateTime? _lastAttemptWallUtc;
    private bool _lastFailed;

    public NewsOptions Options => options;

    public bool IsActive => options.Enabled && options.Provider != NewsProvider.None;

    public NewsSnapshot Current => _current;

    public async Task<NewsSnapshot> RefreshIfDueAsync(DateTime marketTimeUtc, CancellationToken cancellationToken)
    {
        if (!IsActive)
        {
            return _current;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var wall = _time.GetUtcNow().UtcDateTime;
            var interval = _lastFailed ? RetryAfterFailure : TimeSpan.FromMinutes(options.RefreshMinutes);
            var due = _lastAttemptMarketUtc is not { } lastMarket
                      || wall - _lastAttemptWallUtc >= interval
                      || (source.FollowsMarketTime && Math.Abs((marketTimeUtc - lastMarket).Ticks) >= interval.Ticks);
            if (!due)
            {
                return _current;
            }

            _lastAttemptMarketUtc = marketTimeUtc;
            _lastAttemptWallUtc = wall;
            try
            {
                var snapshot = await source.FetchAsync(marketTimeUtc, cancellationToken);
                _current = snapshot;
                _lastFailed = false;
                logger.LogDebug("News refreshed from {Source}: {Events} calendar events, {Headlines} headlines, calendar available {Calendar}",
                    source.Name, snapshot.Events.Count, snapshot.Headlines.Count, snapshot.CalendarAvailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _lastFailed = true;
                var previous = _current;
                var keepCalendar = previous.UpdatedUtc is { } updated && marketTimeUtc - updated <= KeepCalendarFor && previous.CalendarAvailable;
                _current = new NewsSnapshot(keepCalendar ? previous.Events : [], previous.Headlines, previous.UpdatedUtc ?? marketTimeUtc,
                    keepCalendar, previous.Source);
                logger.LogWarning(ex, "News refresh from {Source} failed; {Kept}", source.Name,
                    keepCalendar ? "keeping the previous calendar" : "the economic calendar is unavailable");
            }

            return _current;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Inputs for one evaluation cycle, or null when news and trends are switched off.</summary>
    public MarketIntelligenceInputs? Inputs(IReadOnlyDictionary<string, decimal> currencyTrend) =>
        options.Enabled ? new MarketIntelligenceInputs(IsActive ? _current : NewsSnapshot.Empty, currencyTrend, options) : null;
}
