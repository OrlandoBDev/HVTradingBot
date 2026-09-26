using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Signals;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Signals;

/// <summary>Lifecycle of a signal.</summary>
public static class SignalStatus
{
    public const string Pending = "Pending";         // sent; waiting for the user
    public const string Accepted = "Accepted";       // the user tapped Trade; waiting for the worker
    public const string Placing = "Placing";         // the worker is placing it
    public const string NeedsReview = "NeedsReview"; // risk rules failed that the user may accept
    public const string Placed = "Placed";           // traded
    public const string Skipped = "Skipped";         // the user said no
    public const string Expired = "Expired";         // not acted on in time
    public const string Failed = "Failed";           // could not be traded (hard rule, price moved, broker)

    /// <summary>Signals the user can still act on.</summary>
    public static readonly string[] Open = [Pending, NeedsReview];

    /// <summary>Signals that are not finished.</summary>
    public static readonly string[] Active = [Pending, Accepted, Placing, NeedsReview];
}

/// <summary>One risk rule result as stored with a signal.</summary>
public sealed record SignalCheck(string Rule, bool Passed, string Detail, bool Overridden, string Kind);

/// <summary>
/// Signals: the engine adds them, the dashboard records the user's decision, the worker places accepted ones (the
/// dashboard never talks to the broker).
/// </summary>
public sealed class SignalStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock) : ISignalStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> AddAsync(NewSignal signal, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Signals.AnyAsync(s => s.SetupId == signal.SetupId, cancellationToken))
        {
            return false;
        }

        db.Signals.Add(new SignalEntity
        {
            Id = signal.Id,
            SetupId = signal.SetupId,
            Kind = signal.Kind,
            Instrument = signal.Instrument,
            Direction = signal.Direction.ToString(),
            Strategy = signal.Strategy,
            Score = signal.Score,
            Regime = signal.Regime,
            Entry = signal.Entry,
            StopLoss = signal.StopLoss,
            TakeProfit = signal.TakeProfit,
            CreatedAtUtc = signal.CreatedAtUtc,
            ExpiresAtUtc = signal.ExpiresAtUtc,
            DecisionId = signal.DecisionId,
            Status = SignalStatus.Pending,
            Checks = Serialize(signal.Preview.Checks),
            RiskAmount = signal.Preview.IsApproved ? signal.Preview.RiskAmount : null,
            AcceptedRules = "[]"
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (DatabaseSetup.IsUniqueViolation(ex))
        {
            return false; // added concurrently
        }
    }

    public async Task<SignalEntity?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Signals.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<SignalEntity> Items, int Total)> ListAsync(bool activeOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Signals.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(s => SignalStatus.Active.Contains(s.Status));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(s => s.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>The user wants to trade it, accepting <paramref name="acceptedRules"/>. Returns a problem when it cannot be accepted.</summary>
    public Task<string?> AcceptAsync(Guid id, IReadOnlyList<string> acceptedRules, string actor, CancellationToken cancellationToken) =>
        DecideAsync(id, actor, s =>
        {
            s.Status = SignalStatus.Accepted;
            s.AcceptedRules = JsonSerializer.Serialize(acceptedRules, Json);
            s.Message = "Placing the trade…";
        }, cancellationToken);

    public Task<string?> SkipAsync(Guid id, string actor, CancellationToken cancellationToken) =>
        DecideAsync(id, actor, s =>
        {
            s.Status = SignalStatus.Skipped;
            s.Message = "Skipped.";
        }, cancellationToken);

    private async Task<string?> DecideAsync(Guid id, string actor, Action<SignalEntity> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var signal = await db.Signals.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (signal is null)
        {
            return "Signal not found.";
        }

        if (!SignalStatus.Open.Contains(signal.Status))
        {
            return $"This signal is {signal.Status.ToLowerInvariant()}.";
        }

        if (signal.ExpiresAtUtc <= clock.UtcNow)
        {
            signal.Status = SignalStatus.Expired;
            signal.Message = "Expired before you decided.";
            await db.SaveChangesAsync(cancellationToken);
            return "This signal has expired.";
        }

        change(signal);
        signal.DecidedAtUtc = clock.UtcNow;
        signal.DecidedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>Marks signals nobody acted on in time as expired. Returns how many.</summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        return await db.Signals.Where(s => SignalStatus.Open.Contains(s.Status) && s.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SignalStatus.Expired).SetProperty(s => s.Message, "Expired without a decision."),
                cancellationToken);
    }

    /// <summary>The oldest accepted signal, claimed for placing (status Placing), or null.</summary>
    public async Task<SignalEntity?> ClaimNextAcceptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var next = await db.Signals.Where(s => s.Status == SignalStatus.Accepted).OrderBy(s => s.DecidedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (next is null)
        {
            return null;
        }

        next.Status = SignalStatus.Placing;
        await db.SaveChangesAsync(cancellationToken);
        return next;
    }

    /// <summary>A signal claimed by a worker that stopped mid-placement: the broker is authoritative, so it is not retried.</summary>
    public async Task<int> FailInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Signals.Where(s => s.Status == SignalStatus.Placing)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SignalStatus.Failed)
                .SetProperty(s => s.Message, "The engine restarted while placing this signal; check Trades and the Audit log."), cancellationToken);
    }

    public async Task RecordOutcomeAsync(Guid id, SignalOutcome outcome, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var signal = await db.Signals.SingleAsync(s => s.Id == id, cancellationToken);
        signal.Status = outcome.Status switch
        {
            SignalOutcomeStatus.Placed => SignalStatus.Placed,
            SignalOutcomeStatus.NeedsReview => SignalStatus.NeedsReview,
            _ => SignalStatus.Failed
        };
        signal.Message = outcome.Message;
        if (outcome.Checks.Count > 0)
        {
            signal.Checks = Serialize(outcome.Checks);
        }

        signal.RiskAmount = outcome.RiskAmount ?? signal.RiskAmount;
        signal.PositionId = outcome.PositionId;
        signal.FillPrice = outcome.FillPrice;
        if (outcome.Status == SignalOutcomeStatus.NeedsReview)
        {
            // The review screen asks again; the signal keeps its original expiry.
            signal.AcceptedRules = "[]";
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>New signals the phone has not announced yet (oldest first), marked as announced.</summary>
    public async Task<IReadOnlyList<SignalEntity>> TakeUnannouncedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var fresh = await db.Signals.Where(s => !s.Notified).OrderBy(s => s.CreatedAtUtc).Take(20).ToListAsync(cancellationToken);
        foreach (var signal in fresh)
        {
            signal.Notified = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return fresh;
    }

    public static IReadOnlyList<SignalCheck> ReadChecks(SignalEntity signal) =>
        JsonSerializer.Deserialize<List<SignalCheck>>(signal.Checks, Json) ?? [];

    public static IReadOnlyList<string> ReadAcceptedRules(SignalEntity signal) =>
        JsonSerializer.Deserialize<List<string>>(signal.AcceptedRules, Json) ?? [];

    private static string Serialize(IEnumerable<RiskCheck> checks) =>
        JsonSerializer.Serialize(checks.Select(c => new SignalCheck(c.Rule, c.Passed, c.Detail, c.Overridden, RiskRuleKinds.Of(c.Rule).ToString())), Json);
}
