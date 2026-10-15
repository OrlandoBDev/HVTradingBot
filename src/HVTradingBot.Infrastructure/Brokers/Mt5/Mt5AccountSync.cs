using System.Globalization;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers.Mt5;

/// <summary>
/// Copies every position on the MT5 account into broker_contracts, whether this app opened it or not (by hand in
/// MetaTrader, the phone app, another Expert Advisor), so the dashboard can show the account's open positions and trade
/// history. Open positions come from the gateway's position list; closed ones from the account's deals (the bridge needs
/// Expert Advisor 1.1 for those). Read-only: nothing here buys or sells.
/// </summary>
public sealed class Mt5AccountSync(
    IMt5Gateway gateway,
    Mt5Broker broker,
    Mt5SettingsStore settings,
    IDbContextFactory<TradingDbContext> dbFactory,
    IClock clock,
    ILogger<Mt5AccountSync> logger) : IBrokerAccountSync
{
    /// <summary>How far back the first history sync reaches (the bridge reports its HistoryDays input at most).</summary>
    public static readonly TimeSpan InitialHistory = TimeSpan.FromDays(35);

    private const string EntryIn = "DEAL_ENTRY_IN";

    public string BrokerName => Mt5Broker.BrokerName;

    public async Task<bool> SyncOpenAsync(CancellationToken cancellationToken)
    {
        if (broker.Descriptor.AccountId is not { } accountId)
        {
            return false; // the trading engine has not connected to the account yet
        }

        var suffix = await settings.GetSymbolSuffixAsync(cancellationToken);
        var positions = await gateway.GetPositionsAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.BrokerContracts.Where(c => c.BrokerAccountId == accountId && c.IsOpen).ToDictionaryAsync(c => c.ContractId, cancellationToken);
        var live = new HashSet<string>();
        foreach (var p in positions)
        {
            live.Add(p.Id);
            if (!stored.TryGetValue(p.Id, out var row))
            {
                row = await db.BrokerContracts.SingleOrDefaultAsync(c => c.ContractId == p.Id, cancellationToken) ?? Add(db, p.Id, accountId);
            }

            Describe(row, p.Symbol, suffix, p.IsBuy, p.Volume);
            row.EntrySpot = p.OpenPrice;
            row.CurrentSpot = p.CurrentPrice ?? row.CurrentSpot;
            row.StopLoss = p.StopLoss;
            row.TakeProfit = p.TakeProfit;
            row.Profit = p.Profit + p.Swap + p.Commission;
            row.PurchaseTimeUtc = p.OpenedAtUtc;
            row.IsOpen = true;
            row.UpdatedAtUtc = clock.UtcNow;
        }

        // Gone from the list: closed (by us, a stop or target, or elsewhere). The history sync records the result.
        var closed = false;
        foreach (var row in stored.Values.Where(r => !live.Contains(r.ContractId)))
        {
            closed = true;
            row.IsOpen = false;
            row.SellTimeUtc ??= clock.UtcNow;
            row.UpdatedAtUtc = clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    /// <summary>Records closed positions from the account's deals (the last 35 days at first, then since the latest known close).</summary>
    public async Task SyncHistoryAsync(CancellationToken cancellationToken)
    {
        if (broker.Descriptor.AccountId is not { } accountId)
        {
            return;
        }

        var suffix = await settings.GetSymbolSuffixAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.BrokerContracts.Where(c => c.BrokerAccountId == accountId && !c.IsOpen).MaxAsync(c => c.SellTimeUtc, cancellationToken);
        var from = latest?.AddDays(-1) ?? clock.UtcNow - InitialHistory;
        var deals = await gateway.GetAccountDealsAsync(from, cancellationToken);
        var live = (await gateway.GetPositionsAsync(cancellationToken)).Select(p => p.Id).ToHashSet();

        var groups = deals.Where(d => d.PositionId is not ("" or "0") && !live.Contains(d.PositionId)).GroupBy(d => d.PositionId).ToList();
        var ids = groups.Select(g => g.Key).ToList();
        var existing = await db.BrokerContracts.Where(c => ids.Contains(c.ContractId)).ToDictionaryAsync(c => c.ContractId, cancellationToken);
        foreach (var group in groups)
        {
            var exits = group.Where(d => d.EntryType != EntryIn).ToList();
            if (exits.Count == 0)
            {
                continue;
            }

            var entry = group.FirstOrDefault(d => d.EntryType == EntryIn && d.Symbol.Length > 0);
            if (!existing.TryGetValue(group.Key, out var row))
            {
                if (entry is null)
                {
                    continue; // opened before the window: nothing to show it with
                }

                row = Add(db, group.Key, accountId);
            }

            if (entry is not null)
            {
                Describe(row, entry.Symbol, suffix, entry.IsBuy, entry.Volume);
                row.EntrySpot = entry.Price;
                row.PurchaseTimeUtc = entry.TimeUtc;
            }

            var last = exits.MaxBy(d => d.TimeUtc)!;
            row.ExitSpot = last.Price;
            row.SellTimeUtc = last.TimeUtc;
            row.IsOpen = false;
            // MT5 charges commission on the opening and the closing deal: the result is complete when the opening one is in the window.
            var counted = entry is null ? exits : group.ToList();
            row.Commission = counted.Sum(d => d.Commission);
            row.Profit = counted.Sum(d => d.Profit + d.Commission + d.Swap);
            row.UpdatedAtUtc = clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("MT5 history sync: {Count} deals since {From:u}", deals.Count, from);
    }

    private static BrokerContractEntity Add(TradingDbContext db, string positionId, string accountId)
    {
        var row = new BrokerContractEntity
        {
            ContractId = positionId,
            Broker = Mt5Broker.BrokerName,
            BrokerAccountId = accountId,
            Symbol = "?",
            ContractType = "?",
            IsOpen = true
        };
        db.BrokerContracts.Add(row);
        return row;
    }

    /// <summary>Our market symbol when known (so the dashboard shows the market's name), otherwise MT5's; the size in lots.</summary>
    private static void Describe(BrokerContractEntity row, string mt5Symbol, string suffix, bool buy, decimal lots)
    {
        var symbol = Mt5Symbols.ToInstrument(mt5Symbol, suffix)?.Symbol ?? mt5Symbol;
        row.Symbol = symbol.Length <= 64 ? symbol : symbol[..64];
        row.Direction = buy ? "Long" : "Short";
        row.ContractType = string.Create(CultureInfo.InvariantCulture, $"{lots:0.00###} lot");
        row.Description = string.Create(CultureInfo.InvariantCulture, $"{(buy ? "Buy" : "Sell")} {lots:0.00###} lot {mt5Symbol}");
    }
}
