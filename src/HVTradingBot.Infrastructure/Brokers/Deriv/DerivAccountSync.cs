using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static HVTradingBot.Infrastructure.Brokers.Deriv.DerivBroker;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>
/// Copies every contract on the Deriv account into broker_contracts, whether this app opened it or not, so the
/// dashboard can show the account's open positions and trade history (the dashboard never talks to the broker itself).
/// Open contracts come from "portfolio" plus "proposal_open_contract" (live profit, spots, stop and target); closed
/// ones from "profit_table". Read-only: nothing here buys or sells.
/// </summary>
public sealed class DerivAccountSync(DerivSession session, IDbContextFactory<TradingDbContext> dbFactory, IClock clock, ILogger<DerivAccountSync> logger)
{
    /// <summary>How far back the first history sync reaches (enough for the week and month on the overview).</summary>
    public static readonly TimeSpan InitialHistory = TimeSpan.FromDays(35);

    private const int HistoryLimit = 500;

    /// <summary>Refreshes the open contracts. Returns true when a contract closed since the last refresh.</summary>
    public async Task<bool> SyncOpenAsync(CancellationToken cancellationToken)
    {
        var socket = await session.GetSocketAsync(cancellationToken);
        var accountId = session.Account!.AccountId;
        var portfolio = (await socket.SendAsync(new JsonObject { ["portfolio"] = 1 }, cancellationToken))
            .GetProperty("portfolio").GetProperty("contracts").EnumerateArray().ToList();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.BrokerContracts.Where(c => c.BrokerAccountId == accountId && c.IsOpen).ToDictionaryAsync(c => c.ContractId, cancellationToken);
        var live = new HashSet<string>();
        foreach (var item in portfolio)
        {
            var id = ReadId(item, "contract_id");
            live.Add(id);
            var detail = await ContractAsync(socket, id, cancellationToken);
            if (!stored.TryGetValue(id, out var row))
            {
                row = await db.BrokerContracts.SingleOrDefaultAsync(c => c.ContractId == id, cancellationToken) ?? Add(db, id, accountId);
            }

            Apply(row, item);
            if (detail is { } d)
            {
                Apply(row, d);
            }

            row.IsOpen = detail is not { } sold || !IsSold(sold);
        }

        // Gone from the portfolio: sold (by us, a stop or target, or elsewhere). Record the final result.
        var closed = false;
        foreach (var row in stored.Values.Where(r => !live.Contains(r.ContractId)))
        {
            closed = true;
            if (await ContractAsync(socket, row.ContractId, cancellationToken) is { } detail)
            {
                Apply(row, detail);
            }

            row.IsOpen = false;
            row.SellTimeUtc ??= clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    /// <summary>Adds closed contracts from the profit table (the last 35 days at first, then since the latest known sale).</summary>
    public async Task SyncHistoryAsync(CancellationToken cancellationToken)
    {
        var socket = await session.GetSocketAsync(cancellationToken);
        var accountId = session.Account!.AccountId;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.BrokerContracts.Where(c => c.BrokerAccountId == accountId && !c.IsOpen).MaxAsync(c => c.SellTimeUtc, cancellationToken);
        var from = latest?.AddDays(-1) ?? clock.UtcNow - InitialHistory;

        var response = await socket.SendAsync(new JsonObject
        {
            ["profit_table"] = 1,
            ["description"] = 1,
            ["sort"] = "DESC",
            ["limit"] = HistoryLimit,
            ["date_from"] = new DateTimeOffset(from, TimeSpan.Zero).ToUnixTimeSeconds()
        }, cancellationToken);
        var transactions = response.GetProperty("profit_table").TryGetProperty("transactions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [];

        var ids = transactions.Select(t => ReadId(t, "contract_id")).ToList();
        var existing = await db.BrokerContracts.Where(c => ids.Contains(c.ContractId)).ToDictionaryAsync(c => c.ContractId, cancellationToken);
        foreach (var transaction in transactions)
        {
            var id = ReadId(transaction, "contract_id");
            if (!existing.TryGetValue(id, out var row))
            {
                row = Add(db, id, accountId);
                existing[id] = row;
            }

            Apply(row, transaction);
            row.IsOpen = false;
            if (FirstDecimal(transaction, "sell_price") is { } sell)
            {
                // Deriv's profit is the sale minus the stake (commission included); this also replaces the last open profit.
                row.SellPrice = sell;
                row.Profit = sell - row.BuyPrice;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Deriv history sync: {Count} closed contracts since {From:u}", transactions.Count, from);
    }

    private async Task<JsonElement?> ContractAsync(IDerivSocket socket, string contractId, CancellationToken cancellationToken)
    {
        try
        {
            return (await socket.SendAsync(new JsonObject
            {
                ["proposal_open_contract"] = 1,
                ["contract_id"] = long.Parse(contractId, CultureInfo.InvariantCulture)
            }, cancellationToken)).GetProperty("proposal_open_contract");
        }
        catch (Exception ex) when (ex is DerivApiException or KeyNotFoundException or FormatException)
        {
            logger.LogDebug(ex, "No details for Deriv contract {ContractId}", contractId);
            return null;
        }
    }

    private BrokerContractEntity Add(TradingDbContext db, string contractId, string accountId)
    {
        var row = new BrokerContractEntity
        {
            ContractId = contractId,
            Broker = BrokerName,
            BrokerAccountId = accountId,
            Symbol = "?",
            ContractType = "?",
            IsOpen = true
        };
        db.BrokerContracts.Add(row);
        return row;
    }

    /// <summary>Copies whatever the message has; portfolio, profit table and contract details share most field names.</summary>
    private void Apply(BrokerContractEntity row, JsonElement c)
    {
        var (shortType, shortSymbol) = DerivSymbols.ParseShortcode(FirstString(c, "shortcode"));
        var contractType = FirstString(c, "contract_type") ?? (shortType.Length > 0 ? shortType : null);
        if (contractType is not null)
        {
            row.ContractType = Truncate(contractType, 32);
            row.Direction = contractType switch { "MULTUP" => "Long", "MULTDOWN" => "Short", _ => null };
        }

        var brokerSymbol = FirstString(c, "underlying", "underlying_symbol", "symbol") ?? shortSymbol;
        if (brokerSymbol is not null)
        {
            row.Symbol = Truncate(DerivSymbols.ToInstrument(brokerSymbol)?.Symbol ?? brokerSymbol, 64);
        }

        row.Currency = FirstString(c, "currency") ?? row.Currency;
        row.BuyPrice = FirstDecimal(c, "buy_price") ?? row.BuyPrice;
        row.Multiplier = FirstDecimal(c, "multiplier") ?? row.Multiplier;
        row.EntrySpot = FirstDecimal(c, "entry_spot", "entry_tick") ?? row.EntrySpot;
        row.CurrentSpot = FirstDecimal(c, "current_spot") ?? row.CurrentSpot;
        row.ExitSpot = FirstDecimal(c, "exit_tick", "exit_spot", "sell_spot") ?? row.ExitSpot;
        row.StopLoss = LimitLevel(c, "stop_loss") ?? row.StopLoss;
        row.TakeProfit = LimitLevel(c, "take_profit") ?? row.TakeProfit;
        row.Profit = FirstDecimal(c, "profit") ?? row.Profit;
        row.Commission = ContractCommission(c) ?? row.Commission;
        row.SellPrice = FirstDecimal(c, "sell_price") ?? row.SellPrice;
        if (FirstLong(c, "purchase_time", "date_start") is { } bought)
        {
            row.PurchaseTimeUtc = DateTimeOffset.FromUnixTimeSeconds(bought).UtcDateTime;
        }

        if (FirstLong(c, "sell_time") is { } sold && sold > 0)
        {
            row.SellTimeUtc = DateTimeOffset.FromUnixTimeSeconds(sold).UtcDateTime;
        }

        if (FirstString(c, "longcode", "display_name") is { } description)
        {
            row.Description = Truncate(description, 1000);
        }

        row.UpdatedAtUtc = clock.UtcNow;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
