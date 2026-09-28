using System.Globalization;
using System.Text;
using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Bridge;

public sealed record BridgeAccount(long Login, string Server, string Company, string Currency, decimal Balance, decimal Equity, string TradeMode);

/// <summary>An open MT5 position. <see cref="Ticket"/> is the position identifier; <see cref="Comment"/> carries our client id.</summary>
public sealed record BridgePosition(long Ticket, string Symbol, string Type, decimal Volume, decimal PriceOpen, decimal Sl, decimal Tp, decimal Profit,
    decimal Swap, long Time, string Comment, long Magic, decimal ContractSize);

/// <summary>A deal of one of our positions. Entry: IN, OUT, INOUT, OUT_BY. Reason: SL, TP, SO, CLIENT, EXPERT, OTHER. Times are UTC seconds.</summary>
public sealed record BridgeDeal(long Ticket, long PositionId, string Entry, decimal Price, decimal Profit, decimal Commission, decimal Swap, long Time,
    string Reason);

/// <summary>What the Expert Advisor did with a command.</summary>
public sealed record BridgeResult(long Id, bool Ok, int Retcode, string Message, long? Ticket, decimal? Price, decimal? Volume, decimal? Units,
    decimal? Commission);

/// <summary>One report from the Expert Advisor (every second).</summary>
public sealed record BridgeSync(string? Version, BridgeAccount Account, List<BridgePosition>? Positions, List<BridgeDeal>? Deals, List<BridgeResult>? Results);

public sealed record BridgeState(DateTime LastSeenUtc, string? Version, BridgeAccount Account, IReadOnlyList<BridgePosition> Positions,
    IReadOnlyList<BridgeDeal> Deals);

public sealed record BridgeOpen(string Symbol, bool Buy, decimal Units, decimal StopLoss, decimal TakeProfit, string ClientId);

public sealed record BridgeClose(long Ticket);

/// <summary>
/// The channel between the trading engine and the HVTradingBot Expert Advisor running in MetaTrader 5 on the same
/// computer (free, no cloud service). The Expert Advisor calls the API every second (<see cref="SyncAsync"/>): it
/// reports the account, positions, recent deals and the results of earlier commands, and receives new commands as
/// text lines (MQL5 has no JSON reader). The engine queues commands and waits for their results. A command is delivered
/// at most once: one not delivered in time is expired (nothing was sent); one delivered without a result is "unknown".
/// </summary>
public sealed class Mt5BridgeStore(IDbContextFactory<TradingDbContext> dbFactory, IClock clock)
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Done = "Done";
    public const string Expired = "Expired";
    private const int StateId = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BridgeSync? Parse(string json) => JsonSerializer.Deserialize<BridgeSync>(json, Json);

    /// <summary>Records a report and returns the commands for the Expert Advisor, one per line ("OK" first).</summary>
    public async Task<string> SyncAsync(BridgeSync sync, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.UtcNow;
        var state = await db.Mt5BridgeState.SingleOrDefaultAsync(s => s.Id == StateId, cancellationToken);
        if (state is null)
        {
            state = new Mt5BridgeStateEntity { Id = StateId, AccountJson = "{}", PositionsJson = "[]", DealsJson = "[]" };
            db.Mt5BridgeState.Add(state);
        }

        state.LastSeenUtc = now;
        state.EaVersion = sync.Version is { Length: > 32 } v ? v[..32] : sync.Version;
        state.AccountJson = JsonSerializer.Serialize(sync.Account, Json);
        state.PositionsJson = JsonSerializer.Serialize(sync.Positions ?? [], Json);
        state.DealsJson = JsonSerializer.Serialize(sync.Deals ?? [], Json);

        var resultIds = (sync.Results ?? []).Select(r => r.Id).ToList();
        var answered = await db.Mt5BridgeCommands.Where(c => resultIds.Contains(c.Id) && c.Status == Sent).ToListAsync(cancellationToken);
        foreach (var command in answered)
        {
            command.Status = Done;
            command.DoneAtUtc = now;
            command.ResultJson = JsonSerializer.Serialize(sync.Results!.Last(r => r.Id == command.Id), Json);
        }

        var pending = await db.Mt5BridgeCommands.Where(c => c.Status == Pending).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var lines = new StringBuilder("OK\n");
        foreach (var command in pending)
        {
            command.Status = Sent;
            command.SentAtUtc = now;
            lines.Append(Line(command)).Append('\n');
        }

        await db.SaveChangesAsync(cancellationToken);
        return lines.ToString();
    }

    public async Task<BridgeState?> GetStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.Mt5BridgeState.AsNoTracking().SingleOrDefaultAsync(s => s.Id == StateId, cancellationToken);
        return state is null
            ? null
            : new BridgeState(DateTime.SpecifyKind(state.LastSeenUtc, DateTimeKind.Utc), state.EaVersion,
                JsonSerializer.Deserialize<BridgeAccount>(state.AccountJson, Json)!,
                JsonSerializer.Deserialize<List<BridgePosition>>(state.PositionsJson, Json) ?? [],
                JsonSerializer.Deserialize<List<BridgeDeal>>(state.DealsJson, Json) ?? []);
    }

    public Task<long> EnqueueOpenAsync(BridgeOpen open, CancellationToken cancellationToken) => EnqueueAsync("OPEN", open, cancellationToken);

    public Task<long> EnqueueCloseAsync(BridgeClose close, CancellationToken cancellationToken) => EnqueueAsync("CLOSE", close, cancellationToken);

    /// <summary>The Expert Advisor's result, or null when none arrived within <paramref name="timeout"/>.</summary>
    public async Task<BridgeResult?> WaitForResultAsync(long id, TimeSpan timeout, TimeSpan poll, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                var command = await db.Mt5BridgeCommands.AsNoTracking().SingleAsync(c => c.Id == id, cancellationToken);
                if (command is { Status: Done, ResultJson: { } json })
                {
                    return JsonSerializer.Deserialize<BridgeResult>(json, Json);
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(poll, cancellationToken);
        }
    }

    /// <summary>Withdraws a command the Expert Advisor has not picked up. True: it was never delivered (nothing was sent).</summary>
    public async Task<bool> ExpireIfPendingAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Mt5BridgeCommands.Where(c => c.Id == id && c.Status == Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, Expired), cancellationToken) == 1;
    }

    private async Task<long> EnqueueAsync<T>(string type, T payload, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var command = new Mt5BridgeCommandEntity
        {
            Type = type, Payload = JsonSerializer.Serialize(payload, Json), Status = Pending, CreatedAtUtc = clock.UtcNow
        };
        db.Mt5BridgeCommands.Add(command);
        await db.SaveChangesAsync(cancellationToken);
        return command.Id;
    }

    /// <summary>OPEN|id|symbol|BUY or SELL|units|stop|target|clientId, or CLOSE|id|ticket.</summary>
    private static string Line(Mt5BridgeCommandEntity command)
    {
        var inv = CultureInfo.InvariantCulture;
        if (command.Type == "OPEN")
        {
            var open = JsonSerializer.Deserialize<BridgeOpen>(command.Payload, Json)!;
            return string.Join('|', "OPEN", command.Id.ToString(inv), open.Symbol, open.Buy ? "BUY" : "SELL", open.Units.ToString(inv),
                open.StopLoss.ToString(inv), open.TakeProfit.ToString(inv), open.ClientId);
        }

        var close = JsonSerializer.Deserialize<BridgeClose>(command.Payload, Json)!;
        return string.Join('|', "CLOSE", command.Id.ToString(inv), close.Ticket.ToString(inv));
    }
}
