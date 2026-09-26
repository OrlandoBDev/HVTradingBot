using System.Globalization;
using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Contracts;
using HVTradingBot.Dashboard;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Signals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Mobile.Core;

/// <summary>What changed in learning (the Learning page reloads when <see cref="Version"/> changes).</summary>
public sealed record LearningPulse(int Resolved, int Open, DateTime? LastResolvedUtc, string Version);

/// <summary>What changed in signals (the Signals page reloads when <see cref="Version"/> changes).</summary>
public sealed record SignalPulse(int Waiting, string Version);

/// <summary>
/// Pushes the dashboard status every two seconds (what SignalR does for the web dashboard) and a learning pulse
/// whenever a setup is added or resolved, so the dashboard and the notification update without polling. Also raises
/// the phone alerts for signals: new ones, ones that came back for a risk review, and ones that could not be traded.
/// </summary>
public sealed class LiveUpdates(
    DashboardQueries queries,
    IDbContextFactory<TradingDbContext> dbFactory,
    SignalStore signals,
    SignalSettingsSource signalSettings,
    IClock clock,
    ILogger<LiveUpdates> logger)
{
    public const string SignalsEvent = "signals";

    // Signals with a notification on the phone, and accepted ones waiting for the worker's outcome.
    private readonly HashSet<Guid> _alerted = [];
    private readonly HashSet<Guid> _placing = [];

    public SignalPulse? Signals { get; private set; }

    /// <summary>A signal to show on the phone.</summary>
    public event Action<SignalAlert>? SignalAlerted;

    /// <summary>A signal was decided or expired: its notification can go.</summary>
    public event Action<Guid>? SignalCleared;

    /// <summary>A one-off message about a signal (e.g. it could not be traded).</summary>
    public event Action<PhoneNotification>? SignalNotice;

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public const string StatusEvent = "status";
    public const string LearningEvent = "learning";

    public SystemStatusDto? Status { get; private set; }

    public LearningPulse? Learning { get; private set; }

    /// <summary>(event name, JSON payload) for the dashboard.</summary>
    public event Action<string, string>? Published;

    public event Action<SystemStatusDto>? StatusChanged;

    public event Action<LearningPulse>? LearningChanged;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PublishOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Best effort, like the web dashboard's broadcaster: the next tick tries again.
                logger.LogWarning(ex, "Live update failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    internal async Task PublishOnceAsync(CancellationToken cancellationToken)
    {
        var status = await queries.GetStatusAsync(cancellationToken);
        Status = status;
        StatusChanged?.Invoke(status);
        Published?.Invoke(StatusEvent, JsonSerializer.Serialize(status, LocalApi.Json));

        var learning = await LearningPulseAsync(cancellationToken);
        if (learning.Version != Learning?.Version)
        {
            Learning = learning;
            LearningChanged?.Invoke(learning);
            Published?.Invoke(LearningEvent, JsonSerializer.Serialize(learning, LocalApi.Json));
        }

        await PublishSignalsAsync(cancellationToken);
    }

    private async Task PublishSignalsAsync(CancellationToken cancellationToken)
    {
        var settings = await signalSettings.RefreshAsync(cancellationToken);
        var now = clock.UtcNow;
        foreach (var signal in await signals.TakeUnannouncedAsync(cancellationToken))
        {
            // Quiet hours only silence the phone; the signal is still on the Signals page.
            if (signal.Status == SignalStatus.Pending && signal.ExpiresAtUtc > now && !settings.IsQuiet(now))
            {
                Alert(NewSignalAlert(signal, settings));
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.Signals.AsNoTracking().GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var waiting = await db.Signals.CountAsync(s => SignalStatus.Open.Contains(s.Status) && s.ExpiresAtUtc > now, cancellationToken);
        var pulse = new SignalPulse(waiting, string.Join(",", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}:{c.Count}")));
        if (pulse != Signals)
        {
            Signals = pulse;
            Published?.Invoke(SignalsEvent, JsonSerializer.Serialize(pulse, LocalApi.Json));
        }

        foreach (var id in await db.Signals.Where(s => s.Status == SignalStatus.Accepted || s.Status == SignalStatus.Placing).Select(s => s.Id)
                     .ToListAsync(cancellationToken))
        {
            _placing.Add(id);
        }

        var watched = _alerted.Concat(_placing).Distinct().ToList();
        if (watched.Count == 0)
        {
            return;
        }

        var current = await db.Signals.AsNoTracking().Where(s => watched.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        foreach (var id in watched)
        {
            if (!current.TryGetValue(id, out var signal))
            {
                Clear(id);
                _placing.Remove(id);
                continue;
            }

            if (_placing.Contains(id) && signal.Status is not (SignalStatus.Accepted or SignalStatus.Placing))
            {
                _placing.Remove(id);
                var market = Instruments.DisplayNameOf(signal.Instrument);
                if (signal.Status == SignalStatus.NeedsReview && signal.ExpiresAtUtc > now)
                {
                    // Placing found new risks: the user decides again (never one-tap).
                    Alert(new SignalAlert(signal.Id, $"Review signal: {Verb(signal)} {market}",
                        $"{signal.Message} Open it to see the risks and decide.", false, signal.ExpiresAtUtc));
                    continue;
                }

                if (signal.Status == SignalStatus.Failed)
                {
                    Clear(id);
                    Notice(new PhoneNotification($"Signal not traded: {market}", signal.Message ?? "It could not be placed.",
                        HVTradingBot.Application.Notifications.NotificationKind.OrderRejected));
                    continue;
                }
            }

            if (!_placing.Contains(id) && !SignalStatus.Open.Contains(signal.Status) || signal.ExpiresAtUtc <= now)
            {
                Clear(id);
            }
        }
    }

    private void Alert(SignalAlert alert)
    {
        _alerted.Add(alert.SignalId);
        Raise(() => SignalAlerted?.Invoke(alert));
    }

    private void Clear(Guid id)
    {
        if (_alerted.Remove(id))
        {
            Raise(() => SignalCleared?.Invoke(id));
        }
    }

    private void Notice(PhoneNotification notification) => Raise(() => SignalNotice?.Invoke(notification));

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            // A notification problem must never stop live updates.
            logger.LogWarning(ex, "Signal notification failed");
        }
    }

    internal static SignalAlert NewSignalAlert(SignalEntity signal, SignalSettings settings)
    {
        var inv = CultureInfo.InvariantCulture;
        var checks = SignalStore.ReadChecks(signal);
        var warnings = checks.Count(c => !c.Passed);
        var risk = Math.Abs(signal.Entry - signal.StopLoss);
        var rewardToRisk = risk == 0 ? 0 : Math.Abs(signal.TakeProfit - signal.Entry) / risk;
        var expires = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(signal.ExpiresAtUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
        var text = string.Join(" · ", new[]
        {
            signal.Kind == SignalKinds.NearMiss ? $"Near miss, score {signal.Score}" : $"Score {signal.Score}",
            signal.Strategy,
            string.Create(inv, $"entry {signal.Entry} · stop {signal.StopLoss} · target {signal.TakeProfit} · R:R {rewardToRisk:0.0}"),
            warnings switch { 0 => "all risk rules pass", 1 => "1 risk warning", _ => $"{warnings} risk warnings" },
            $"expires {expires.ToString("HH:mm", inv)}"
        });
        return new SignalAlert(signal.Id, $"Signal: {Verb(signal)} {Instruments.DisplayNameOf(signal.Instrument)}", text,
            settings.OneTapFromNotification && warnings == 0, signal.ExpiresAtUtc);
    }

    private static string Verb(SignalEntity signal) => signal.Direction == "Long" ? "buy" : "sell";

    private async Task<LearningPulse> LearningPulseAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var total = await db.SetupOutcomes.CountAsync(cancellationToken);
        var resolved = await db.SetupOutcomes.CountAsync(o => o.ClosedAtUtc != null, cancellationToken);
        var last = await db.SetupOutcomes.MaxAsync(o => o.ClosedAtUtc, cancellationToken);
        return new LearningPulse(resolved, total - resolved, last, $"{total}-{resolved}-{last?.Ticks}");
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
