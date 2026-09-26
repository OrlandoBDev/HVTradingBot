using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Performance;
using HVTradingBot.Application.Trading;
using HVTradingBot.Contracts;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using HVTradingBot.Infrastructure.Settings;
using HVTradingBot.Infrastructure.Trades;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Dashboard;

/// <summary>Read-only projections for the dashboard. Never places or modifies orders.</summary>
public sealed class DashboardQueries(
    IDbContextFactory<TradingDbContext> dbFactory,
    ITradingStateStore stateStore,
    IClock clock,
    RiskOptionsSource riskSource,
    TradingEngineOptions engineOptions,
    MarketCatalogStore catalog,
    CloseRequestStore closeRequests)
{
    /// <summary>The worker counts as running while its heartbeat is at most this old.</summary>
    public static readonly TimeSpan WorkerHeartbeatTimeout = TimeSpan.FromSeconds(60);

    private sealed record AccountSnapshot(string Currency, decimal Balance, decimal StartingBalance);

    /// <summary>Balance of the broker the worker last connected to (Deriv account or local paper account).</summary>
    private async Task<AccountSnapshot> AccountAsync(TradingDbContext db, TradingSystemState state, CancellationToken cancellationToken)
    {
        if (state.BrokerAccountId is { } accountId)
        {
            var broker = await db.BrokerAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountKey == accountId, cancellationToken);
            if (broker is not null)
            {
                // The baseline is derived from the app's own closed trades, so deposits, withdrawals or a demo-balance
                // reset at the broker are not reported as trading profit.
                var realized = await db.Positions.AsNoTracking()
                    .Where(p => !p.IsOpen && p.BrokerAccountId == accountId)
                    .SumAsync(p => p.RealizedPnl ?? 0, cancellationToken);
                return new AccountSnapshot(broker.Currency, broker.LastBalance, broker.LastBalance - realized);
            }
        }

        var paper = await db.PaperAccounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new AccountSnapshot(paper?.Currency ?? engineOptions.AccountCurrency, paper?.Balance ?? engineOptions.StartingBalance,
            paper?.StartingBalance ?? engineOptions.StartingBalance);
    }

    private static string BrokerOf(TradingSystemState state) => state.BrokerName ?? "Paper";

    public async Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var account = await AccountAsync(db, state, cancellationToken);
        var positions = await OpenPositionsAsync(db, BrokerOf(state), cancellationToken);
        var unrealized = positions.Sum(p => p.UnrealizedPnl ?? 0);
        var balance = account.Balance;
        var now = clock.UtcNow;

        return new SystemStatusDto(
            state.Mode.ToString().ToUpperInvariant(),
            new BrokerDto(BrokerOf(state), state.BrokerAccountId, state.BrokerIsDemo || state.BrokerName is null or "Paper"),
            state.KillSwitchActive,
            state.KillSwitchReason,
            state.KillSwitchChangedUtc,
            state.WorkerHeartbeatUtc,
            state.WorkerHeartbeatUtc is { } hb && now - hb <= WorkerHeartbeatTimeout,
            new MarketDataStatusDto(state.LastBarTimeUtc, state.LastDataReceivedUtc,
                new MarketDataStatus(state.LastBarTimeUtc, state.LastDataReceivedUtc).IsStale(now, TimeSpan.FromSeconds(riskSource.Current.MaxMarketDataAgeSeconds))),
            new AccountDto(account.Currency, balance, account.StartingBalance, balance + unrealized, unrealized),
            positions.Count,
            state.DailyRealizedPnl,
            state.WeeklyRealizedPnl,
            state.ConsecutiveLosses,
            state.CooldownUntilUtc,
            now);
    }

    /// <summary>A market with no price for this long is treated as closed and hidden from the market tables.</summary>
    public static readonly TimeSpan ClosedAfter = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<MarketDto>> GetMarketsAsync(CancellationToken cancellationToken)
    {
        // Only the markets currently selected on the Settings page (deselected ones keep old snapshot rows).
        var selection = await catalog.GetSelectionAsync(cancellationToken);
        var selected = selection.Instruments ?? engineOptions.Instruments;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.MarketSnapshots.AsNoTracking().Where(s => selected.Contains(s.Instrument)).ToListAsync(cancellationToken);
        var order = selected.Select((symbol, index) => (symbol, index)).ToDictionary(x => x.symbol, x => x.index);
        var now = clock.UtcNow;

        var markets = rows.OrderBy(r => order.GetValueOrDefault(r.Instrument, int.MaxValue)).Select(s =>
        {
            var known = Instruments.TryGet(s.Instrument, out var instrument);
            var isOpen = now - s.MarketTimeUtc <= ClosedAfter;
            return (Row: s, Instrument: known ? instrument : null, IsOpen: isOpen);
        }).ToList();

        var forexOpen = markets.Any(m => m.IsOpen && m.Instrument?.IsCurrencyPair == true);
        var result = markets.Select(m =>
        {
            var s = m.Row;
            var i = m.Instrument;
            var paused = selection.DerivedOnlyWhenForexClosed && forexOpen && i?.AssetClass == AssetClass.SyntheticIndex;
            return new MarketDto(s.Instrument, i?.DisplayName ?? s.Instrument, i?.AssetClass.ToString() ?? "Forex", i?.IsTradable ?? true,
                i?.PriceDecimals ?? 5, m.IsOpen, paused, false, s.MarketTimeUtc, s.Bid, s.Ask, s.SpreadPips, s.Regime, s.LastDecision,
                s.LastDecisionTimeUtc, s.Indicators is null ? null : JsonDocument.Parse(s.Indicators).RootElement.Clone());
        }).ToList();

        // Newly selected markets have no data until the worker has loaded their history: show them as loading.
        var known = rows.Select(r => r.Instrument).ToHashSet();
        foreach (var symbol in selected.Where(s => !known.Contains(s)))
        {
            var found = Instruments.TryGet(symbol, out var i);
            result.Add(new MarketDto(symbol, found ? i.DisplayName : symbol, found ? i.AssetClass.ToString() : "Forex", !found || i.IsTradable,
                found ? i.PriceDecimals : 5, true, false, true, now, 0, 0, 0, null, null, null, null));
        }

        return result;
    }

    public async Task<IReadOnlyList<DecisionDto>> GetDecisionsAsync(string? state, string? instrument, int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TradeDecisions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
        {
            var states = state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(d => states.Contains(d.State));
        }

        if (!string.IsNullOrWhiteSpace(instrument))
        {
            query = query.Where(d => d.Instrument == instrument);
        }

        var rows = await query.OrderByDescending(d => d.MarketTimeUtc).ThenBy(d => d.Instrument)
            .Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public const int MaxPageSize = 200;

    private static (int Page, int Size) Paging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, MaxPageSize));

    public async Task<PagedResult<DecisionDto>> GetDecisionsPageAsync(string? state, string? instrument, int? page, int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, size) = Paging(page, pageSize);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TradeDecisions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
        {
            var states = state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(d => states.Contains(d.State));
        }

        if (!string.IsNullOrWhiteSpace(instrument))
        {
            query = query.Where(d => d.Instrument == instrument);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(d => d.MarketTimeUtc).ThenBy(d => d.Instrument).ThenBy(d => d.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<DecisionDto>(rows.Select(ToDto).ToList(), total, p, size);
    }

    public async Task<PagedResult<PositionDto>> GetTradeHistoryPageAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = Paging(page, pageSize);
        var broker = BrokerOf(await stateStore.GetAsync(cancellationToken));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Positions.AsNoTracking().Where(x => !x.IsOpen && x.Broker == broker);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.ClosedAtUtc).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<PositionDto>(rows.Select(x => ToDto(x, null, null)).ToList(), total, p, size);
    }

    public async Task<PagedResult<AuditEntryDto>> GetAuditPageAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = Paging(page, pageSize);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var total = await db.AuditLogs.CountAsync(cancellationToken);
        var rows = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Skip((p - 1) * size).Take(size)
            .Select(a => new AuditEntryDto(a.Id, a.TimestampUtc, a.Actor, a.Action, a.Details, a.CorrelationId))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditEntryDto>(rows, total, p, size);
    }

    public async Task<DecisionDetailDto?> GetDecisionAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.TradeDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return row is null ? null : new DecisionDetailDto(ToDto(row), JsonDocument.Parse(row.Details).RootElement.Clone());
    }

    public async Task<IReadOnlyList<PositionDto>> GetOpenPositionsAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var positions = await OpenPositionsAsync(db, BrokerOf(state), cancellationToken);

        // Show "closing…" (or why a close failed) next to positions the user asked to close.
        var requests = await closeRequests.LatestForAsync(positions.Select(p => p.Id).ToList(), cancellationToken);
        return positions.Select(p => requests.TryGetValue(p.Id, out var r) && r.Status != CloseRequestStatus.Closed
            ? p with { CloseStatus = r.Status, CloseMessage = r.Message }
            : p).ToList();
    }

    public async Task<IReadOnlyList<PositionDto>> GetTradeHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        var broker = BrokerOf(await stateStore.GetAsync(cancellationToken));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Positions.AsNoTracking().Where(p => !p.IsOpen && p.Broker == broker)
            .OrderByDescending(p => p.ClosedAtUtc).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(cancellationToken);
        return rows.Select(p => ToDto(p, null, null)).ToList();
    }

    public async Task<RiskStatusDto> GetRiskStatusAsync(CancellationToken cancellationToken)
    {
        await riskSource.RefreshAsync(cancellationToken);
        var risk = riskSource.Current;
        var status = await GetStatusAsync(cancellationToken);
        var positions = await GetOpenPositionsAsync(cancellationToken);
        var state = await stateStore.GetAsync(cancellationToken);
        var derivedOpen = positions.Count(p => Instruments.TryGet(p.Instrument, out var i) && RiskOptions.IsDerived(i));
        var balance = status.Account.Balance;
        var dailyLimit = balance * risk.MaxDailyLossPercent / 100m;
        var weeklyLimit = balance * risk.MaxWeeklyLossPercent / 100m;
        var marketTime = status.MarketData.LastBarTimeUtc ?? status.ServerTimeUtc;
        var cooling = status.CooldownUntilUtc is { } until && until > marketTime;

        var exposure = new Dictionary<string, int>();
        foreach (var p in positions)
        {
            if (Instruments.TryGet(p.Instrument, out var instrument))
            {
                PortfolioState.Add(exposure, instrument, Enum.Parse<Direction>(p.Direction));
            }
        }

        var limits = new List<RiskLimitStatusDto>
        {
            new("Risk per trade", $"{risk.MaxRiskPerTradePercent}% of equity", $"{risk.MaxRiskPerTradePercent}%", false),
            new("Risk per Derived trade", $"{risk.DerivedRiskPerTradePercent}% of equity", $"{risk.DerivedRiskPerTradePercent}%", false),
            new("Daily loss", $"{status.DailyRealizedPnl:F2}", $"-{dailyLimit:F2}", -status.DailyRealizedPnl >= dailyLimit),
            new("Weekly loss", $"{status.WeeklyRealizedPnl:F2}", $"-{weeklyLimit:F2}", -status.WeeklyRealizedPnl >= weeklyLimit),
            new("Open positions", $"{positions.Count}", $"{risk.MaxOpenPositions}", positions.Count >= risk.MaxOpenPositions),
            new("Consecutive losses", $"{status.ConsecutiveLosses}", $"{risk.MaxConsecutiveLosses}", cooling),
            new("Derived open positions", $"{derivedOpen}", $"{risk.MaxDerivedOpenPositions}", derivedOpen >= risk.MaxDerivedOpenPositions),
            new("Derived loss today", $"{state.DerivedDailyRealizedPnl:F2}", $"-{balance * risk.MaxDerivedDailyLossPercent / 100m:F2}",
                -state.DerivedDailyRealizedPnl >= balance * risk.MaxDerivedDailyLossPercent / 100m),
            new("Max currency exposure", exposure.Count == 0 ? "0" : $"{exposure.Values.Max(Math.Abs)}", $"{risk.MaxCurrencyExposure}",
                exposure.Values.Any(v => Math.Abs(v) > risk.MaxCurrencyExposure)),
            new("Min reward:risk", "-", $"{risk.MinRewardToRisk}:1", false),
            new("Max fee share of risk", "-", $"{risk.MaxCommissionShareOfRisk:P0}", false),
            new("Max spread", "-", $"{risk.MaxSpreadPips} pips", false),
            new("Market data age", status.MarketData.IsStale ? "stale" : "fresh", $"{risk.MaxMarketDataAgeSeconds}s", status.MarketData.IsStale)
        };

        var blocking = new List<string>();
        if (status.KillSwitchActive) blocking.Add($"Kill switch: {status.KillSwitchReason}");
        if (!status.WorkerHealthy) blocking.Add("Trading worker is not running.");
        if (status.MarketData.IsStale) blocking.Add("Market data is stale.");
        if (cooling) blocking.Add($"Cooldown until {status.CooldownUntilUtc:u}.");
        blocking.AddRange(limits.Where(l => l.Breached && l.Name is "Daily loss" or "Weekly loss" or "Open positions").Select(l => $"{l.Name} limit reached."));

        return new RiskStatusDto(blocking.Count == 0, blocking, limits, exposure);
    }

    public async Task<object> GetPerformanceAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(cancellationToken);
        var broker = BrokerOf(state);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var startingBalance = (await AccountAsync(db, state, cancellationToken)).StartingBalance;
        var closed = await db.Positions.AsNoTracking().Where(p => !p.IsOpen && p.Broker == broker).ToListAsync(cancellationToken);
        var trades = closed.Select(p => new ClosedTrade(p.Strategy, p.Instrument, p.Direction, p.OpenedAtUtc, p.ClosedAtUtc!.Value,
            p.RealizedPnl ?? 0, p.RMultiple ?? 0, p.MaePips ?? 0, p.MfePips ?? 0)).ToList();

        var decisionCounts = await db.TradeDecisions.AsNoTracking().GroupBy(d => d.State)
            .Select(g => new { State = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.State, x => x.Count, cancellationToken);

        return new
        {
            Overall = PerformanceCalculator.Calculate(trades, startingBalance),
            ByStrategy = PerformanceCalculator.ByStrategy(trades, startingBalance),
            ByInstrument = trades.GroupBy(t => t.Instrument).OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => PerformanceCalculator.Calculate(g, startingBalance)),
            DecisionCounts = decisionCounts
        };
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, 500))
            .Select(a => new AuditEntryDto(a.Id, a.TimestampUtc, a.Actor, a.Action, a.Details, a.CorrelationId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Open positions recorded for <paramref name="broker"/>. Unrealized P&amp;L is an estimate from the latest quotes (excludes commission).</summary>
    private async Task<List<PositionDto>> OpenPositionsAsync(TradingDbContext db, string broker, CancellationToken cancellationToken)
    {
        var open = await db.Positions.AsNoTracking().Where(p => p.IsOpen && p.Broker == broker).OrderBy(p => p.OpenedAtUtc).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var snapshots = await db.MarketSnapshots.AsNoTracking().ToDictionaryAsync(s => s.Instrument, cancellationToken);
        CurrencyConverter? converter = null;
        try
        {
            converter = new CurrencyConverter(engineOptions.AccountCurrency, snapshots.ToDictionary(s => s.Key, s => (s.Value.Bid + s.Value.Ask) / 2m));
        }
        catch (ArgumentException)
        {
            // Unknown instrument in snapshots; unrealized P&L is shown as unavailable.
        }

        return open.Select(p =>
        {
            if (converter is null || !snapshots.TryGetValue(p.Instrument, out var s) || !Instruments.TryGet(p.Instrument, out var instrument))
            {
                return ToDto(p, null, null);
            }

            var direction = Enum.Parse<Direction>(p.Direction);
            var exit = direction == Direction.Long ? s.Bid : s.Ask;
            decimal? pnl;
            try
            {
                pnl = Math.Round(direction.Sign() * (exit - p.EntryPrice) * p.Units * converter.QuoteToAccountRate(instrument), 2);
            }
            catch (InvalidOperationException)
            {
                pnl = null;
            }

            return ToDto(p, exit, pnl);
        }).ToList();
    }

    private static PositionDto ToDto(PositionEntity p, decimal? currentPrice, decimal? unrealized) => new(
        p.Id, p.ClientOrderId, p.Instrument, p.Direction, p.Units, p.EntryPrice, p.StopLoss, p.TakeProfit, p.InitialRiskAmount,
        p.OpenedAtUtc, p.Strategy, p.Score, currentPrice, unrealized, p.ClosedAtUtc, p.ExitPrice, p.ExitReason, p.RealizedPnl,
        p.RMultiple, p.MaePips, p.MfePips, Commission: p.Commission);

    private static DecisionDto ToDto(TradeDecisionEntity d) => new(
        d.Id, d.MarketTimeUtc, d.Instrument, d.State, d.Regime, d.Strategy, d.Direction, d.Score, d.Entry, d.StopLoss,
        d.TakeProfit, d.RewardToRisk, d.Reasons, d.ClientOrderId, d.CorrelationId);
}
