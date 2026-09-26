using System.Collections.Concurrent;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers;

/// <summary>
/// Database-backed simulated broker. Positions survive restarts; fills use <see cref="PaperExecutionModel"/>
/// (spread, slippage, commission). Idempotency is enforced by a unique index on the client order id.
/// </summary>
public sealed class PaperTradingBroker(
    IDbContextFactory<TradingDbContext> dbFactory,
    ExecutionCostOptions costs,
    TradingEngineOptions engineOptions,
    IRiskOptionsSource risk,
    IClock clock,
    ILogger<PaperTradingBroker> logger) : IExecutionBroker
{
    private const int AccountId = 1;
    private readonly ConcurrentDictionary<Instrument, Quote> _quotes = new();

    public void UpdateQuotes(IEnumerable<Quote> quotes)
    {
        foreach (var quote in quotes)
        {
            _quotes[quote.Instrument] = quote;
        }
    }

    public BrokerDescriptor Descriptor { get; } = new("Paper", null, IsDemo: true);

    public async Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var account = await EnsureAccountAsync(db, cancellationToken);
        return new BrokerAccount(account.Currency, account.Balance, account.StartingBalance);
    }

    public async Task<IReadOnlyCollection<OpenPosition>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var open = await db.Positions.AsNoTracking().Where(p => p.IsOpen && p.Broker == "Paper").OrderBy(p => p.OpenedAtUtc).ToListAsync(cancellationToken);
        return open.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking()
            .OrderByDescending(o => o.MarketTimeUtc)
            .Take(500)
            .Select(o => new BrokerOrder(o.Id, o.ClientOrderId, o.Instrument, o.Direction, o.Units, o.FillPrice,
                Enum.Parse<OrderStatus>(o.Status), o.RejectReason, o.MarketTimeUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Candle>> GetCandlesAsync(Instrument instrument, TimeFrame timeFrame, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Candles.AsNoTracking()
            .Where(c => c.Instrument == instrument.Symbol && c.TimeFrame == timeFrame.ToString())
            .OrderByDescending(c => c.OpenTimeUtc)
            .Take(500)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(c => c.OpenTimeUtc)
            .Select(c => new Candle(DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc), timeFrame, c.Open, c.High, c.Low, c.Close, c.Spread, c.Volume))
            .ToList();
    }

    public async Task<OrderResult> PlaceOrderAsync(TradeOrder order, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.ClientOrderId == order.ClientOrderId, cancellationToken);
        if (existing is not null)
        {
            return Duplicate(order.ClientOrderId, existing);
        }

        if (!_quotes.TryGetValue(order.Instrument, out var quote))
        {
            return await RecordRejectionAsync(db, order, "No quote available for simulated fill.", cancellationToken);
        }

        var fill = PaperExecutionModel.EntryFill(order.Direction, quote, costs);
        var orderEntity = NewOrder(order, OrderStatus.Filled, fill, null, quote.TimestampUtc);
        var position = new PositionEntity
        {
            Id = Guid.NewGuid(),
            OrderId = orderEntity.Id,
            Broker = Descriptor.Name,
            ClientOrderId = order.ClientOrderId,
            Instrument = order.Instrument.Symbol,
            Direction = order.Direction.ToString(),
            Units = order.Units,
            EntryPrice = fill,
            StopLoss = order.StopLoss,
            TakeProfit = order.TakeProfit,
            InitialRiskAmount = order.RiskAmount,
            OpenedAtUtc = quote.TimestampUtc,
            Strategy = order.Strategy,
            Score = order.Score,
            IsOpen = true
        };

        db.Orders.Add(orderEntity);
        db.Positions.Add(position);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseSetup.IsUniqueViolation(ex))
        {
            // A concurrent or retried submission won the race; report the existing order instead of creating a second one.
            await using var fresh = await dbFactory.CreateDbContextAsync(cancellationToken);
            var winner = await fresh.Orders.AsNoTracking().SingleAsync(o => o.ClientOrderId == order.ClientOrderId, cancellationToken);
            logger.LogWarning("Duplicate submission blocked for {ClientOrderId}", order.ClientOrderId);
            return Duplicate(order.ClientOrderId, winner);
        }

        return new OrderResult(order.ClientOrderId, OrderStatus.Filled, orderEntity.Id, position.Id, fill, null);
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OrderResult.Rejected(orderId, "Paper market orders fill immediately; there is nothing pending to cancel."));

    private readonly ConcurrentDictionary<Guid, bool> _closeRequested = new();

    /// <summary>Marks the position for closing; it is filled at the current price by the next <see cref="ProcessBarAsync"/>.</summary>
    public async Task<OrderResult> ClosePositionAsync(string positionId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(positionId, out var id))
        {
            return OrderResult.Rejected(positionId, "Invalid position id.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Broker == "Paper" && p.IsOpen, cancellationToken);
        if (position is null)
        {
            return OrderResult.Rejected(positionId, "No open paper position with this id.");
        }

        _closeRequested[id] = true;
        return new OrderResult(position.ClientOrderId, OrderStatus.Filled, position.OrderId, position.Id, null, null);
    }

    public async Task<IReadOnlyList<ClosedPosition>> ProcessBarAsync(Instrument instrument, Candle bar, CurrencyConverter converter, CancellationToken cancellationToken)
    {
        _quotes[instrument] = new Quote(instrument, bar.CloseTimeUtc, instrument.RoundPrice(bar.CloseBid), instrument.RoundPrice(bar.CloseAsk));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var open = await db.Positions
            .Where(p => p.IsOpen && p.Broker == "Paper" && p.Instrument == instrument.Symbol)
            .ToListAsync(cancellationToken);
        // Stops and targets are checked only against bars after the position opened; close requests apply at once.
        open = open.Where(p => p.OpenedAtUtc < bar.CloseTimeUtc || _closeRequested.ContainsKey(p.Id)).ToList();
        if (open.Count == 0)
        {
            return [];
        }

        var closed = new List<ClosedPosition>();
        PaperAccountEntity? account = null;
        foreach (var entity in open)
        {
            // A position opened after this bar closed must not be judged on price action from before it existed.
            var openedAfterBar = entity.OpenedAtUtc >= bar.CloseTimeUtc;
            var tracked = openedAfterBar ? ToModel(entity) : PaperExecutionModel.TrackExcursion(ToModel(entity), bar);
            entity.MaxFavorableExcursion = tracked.MaxFavorableExcursion;
            entity.MaxAdverseExcursion = tracked.MaxAdverseExcursion;

            var exitCheck = openedAfterBar ? null : PaperExecutionModel.CheckExit(tracked, bar, costs);
            if (exitCheck is null && _closeRequested.TryRemove(entity.Id, out _) && _quotes.TryGetValue(instrument, out var now))
            {
                // Closed on request: longs sell at the bid, shorts buy back at the ask (plus slippage).
                var slippage = instrument.FromPips(costs.SlippagePips);
                var price = tracked.Direction == Direction.Long ? now.Bid - slippage : now.Ask + slippage;
                exitCheck = (ExitReason.Manual, instrument.RoundPrice(price));
            }

            if (exitCheck is not { } exit)
            {
                continue;
            }

            var rate = converter.QuoteToAccountRate(instrument);
            var commissionPercent = risk.Current.AssumedCommissionPercent;
            var pnl = PaperExecutionModel.RealizedPnl(tracked, exit.Price, rate, commissionPercent);
            var r = PaperExecutionModel.RMultiple(tracked, pnl);
            entity.IsOpen = false;
            entity.ClosedAtUtc = bar.CloseTimeUtc;
            entity.ExitPrice = exit.Price;
            entity.ExitReason = exit.Reason.ToString();
            entity.RealizedPnl = pnl;
            entity.Commission = Math.Round(PaperExecutionModel.Commission(tracked, rate, commissionPercent), 2);
            entity.RMultiple = r;
            entity.MaePips = Math.Round(instrument.ToPips(tracked.MaxAdverseExcursion), 1);
            entity.MfePips = Math.Round(instrument.ToPips(tracked.MaxFavorableExcursion), 1);

            account ??= await EnsureAccountAsync(db, cancellationToken);
            account.Balance += pnl;
            closed.Add(new ClosedPosition(tracked, bar.CloseTimeUtc, exit.Price, exit.Reason, pnl, r));
        }

        // Position closes and the balance change commit together.
        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    private async Task<PaperAccountEntity> EnsureAccountAsync(TradingDbContext db, CancellationToken cancellationToken)
    {
        var account = await db.PaperAccounts.SingleOrDefaultAsync(a => a.Id == AccountId, cancellationToken);
        if (account is not null)
        {
            return account;
        }

        account = new PaperAccountEntity
        {
            Id = AccountId,
            Currency = engineOptions.AccountCurrency,
            Balance = engineOptions.StartingBalance,
            StartingBalance = engineOptions.StartingBalance
        };
        db.PaperAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Created concurrently by the other process.
            db.ChangeTracker.Clear();
            account = await db.PaperAccounts.SingleAsync(a => a.Id == AccountId, cancellationToken);
        }

        return account;
    }

    private async Task<OrderResult> RecordRejectionAsync(TradingDbContext db, TradeOrder order, string reason, CancellationToken cancellationToken)
    {
        db.Orders.Add(NewOrder(order, OrderStatus.Rejected, null, reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return OrderResult.Rejected(order.ClientOrderId, reason);
    }

    private OrderEntity NewOrder(TradeOrder order, OrderStatus status, decimal? fill, string? reason, DateTime marketTime) => new()
    {
        Id = Guid.NewGuid(),
        ClientOrderId = order.ClientOrderId,
        Broker = Descriptor.Name,
        DecisionId = order.DecisionId,
        CorrelationId = order.CorrelationId,
        Instrument = order.Instrument.Symbol,
        Direction = order.Direction.ToString(),
        Units = order.Units,
        RequestedPrice = order.RequestedPrice,
        FillPrice = fill,
        StopLoss = order.StopLoss,
        TakeProfit = order.TakeProfit,
        Status = status.ToString(),
        RejectReason = reason,
        MarketTimeUtc = marketTime,
        RecordedAtUtc = clock.UtcNow
    };

    private static OrderResult Duplicate(string clientOrderId, OrderEntity existing) =>
        new(clientOrderId, OrderStatus.Duplicate, existing.Id, null, existing.FillPrice, "Duplicate client order id; original order kept.");

    private static OpenPosition ToModel(PositionEntity p) => new(
        p.Id,
        p.ClientOrderId,
        Instruments.Get(p.Instrument),
        Enum.Parse<Direction>(p.Direction),
        p.Units,
        p.EntryPrice,
        p.StopLoss,
        p.TakeProfit,
        p.InitialRiskAmount,
        DateTime.SpecifyKind(p.OpenedAtUtc, DateTimeKind.Utc),
        p.Strategy,
        p.Score,
        p.MaxFavorableExcursion,
        p.MaxAdverseExcursion);
}
