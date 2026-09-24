using System.Collections.Concurrent;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.Backtesting;

/// <summary>In-memory simulated broker for backtests. Uses the same <see cref="PaperExecutionModel"/> as the paper broker.</summary>
public sealed class InMemorySimulatedBroker(string currency, decimal startingBalance, ExecutionCostOptions costs) : IExecutionBroker
{
    private readonly decimal _startingBalance = startingBalance;
    private readonly Dictionary<Guid, OpenPosition> _open = new();
    private readonly Dictionary<string, BrokerOrder> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<Instrument, Quote> _quotes = new();
    private readonly List<(ClosedPosition Closed, decimal MaePips, decimal MfePips)> _closed = [];
    private decimal _balance = startingBalance;

    public IReadOnlyList<(ClosedPosition Closed, decimal MaePips, decimal MfePips)> ClosedPositions => _closed;

    public BrokerDescriptor Descriptor { get; } = new("Backtest", null, IsDemo: true);

    public Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BrokerAccount(currency, _balance, _startingBalance));

    public Task<IReadOnlyCollection<OpenPosition>> GetPositionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OpenPosition>>(_open.Values.ToList());

    public Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<BrokerOrder>>(_orders.Values.ToList());

    public Task<IReadOnlyCollection<Candle>> GetCandlesAsync(Instrument instrument, TimeFrame timeFrame, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Candle>>([]);

    public Task<OrderResult> PlaceOrderAsync(TradeOrder order, CancellationToken cancellationToken)
    {
        if (_orders.TryGetValue(order.ClientOrderId, out var existing))
        {
            return Task.FromResult(new OrderResult(order.ClientOrderId, OrderStatus.Duplicate, existing.Id, null, existing.FillPrice,
                "Duplicate client order id."));
        }

        if (!_quotes.TryGetValue(order.Instrument, out var quote))
        {
            return Task.FromResult(OrderResult.Rejected(order.ClientOrderId, "No quote available."));
        }

        var fill = PaperExecutionModel.EntryFill(order.Direction, quote, costs);
        var orderId = Guid.NewGuid();
        var position = new OpenPosition(Guid.NewGuid(), order.ClientOrderId, order.Instrument, order.Direction, order.Units, fill,
            order.StopLoss, order.TakeProfit, order.RiskAmount, quote.TimestampUtc, order.Strategy, order.Score, 0, 0);
        _open[position.Id] = position;
        _orders[order.ClientOrderId] = new BrokerOrder(orderId, order.ClientOrderId, order.Instrument.Symbol, order.Direction.ToString(),
            order.Units, fill, OrderStatus.Filled, null, quote.TimestampUtc);
        return Task.FromResult(new OrderResult(order.ClientOrderId, OrderStatus.Filled, orderId, position.Id, fill, null));
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OrderResult.Rejected(orderId, "Market orders fill immediately; nothing to cancel."));

    public Task<OrderResult> ClosePositionAsync(string positionId, CancellationToken cancellationToken) =>
        Task.FromResult(OrderResult.Rejected(positionId, "Manual close is not supported in backtests."));

    public Task<IReadOnlyList<ClosedPosition>> ProcessBarAsync(Instrument instrument, Candle bar, CurrencyConverter converter, CancellationToken cancellationToken)
    {
        _quotes[instrument] = new Quote(instrument, bar.CloseTimeUtc, instrument.RoundPrice(bar.CloseBid), instrument.RoundPrice(bar.CloseAsk));
        var result = new List<ClosedPosition>();
        foreach (var position in _open.Values.Where(p => p.Instrument == instrument && p.OpenedAtUtc < bar.CloseTimeUtc).ToList())
        {
            var tracked = PaperExecutionModel.TrackExcursion(position, bar);
            var exit = PaperExecutionModel.CheckExit(tracked, bar, costs);
            if (exit is null)
            {
                _open[position.Id] = tracked;
                continue;
            }

            var pnl = PaperExecutionModel.RealizedPnl(tracked, exit.Value.Price, converter.QuoteToAccountRate(instrument), costs);
            var closed = new ClosedPosition(tracked, bar.CloseTimeUtc, exit.Value.Price, exit.Value.Reason, pnl,
                PaperExecutionModel.RMultiple(tracked, pnl));
            _open.Remove(position.Id);
            _balance += pnl;
            _closed.Add((closed, instrument.ToPips(tracked.MaxAdverseExcursion), instrument.ToPips(tracked.MaxFavorableExcursion)));
            result.Add(closed);
        }

        return Task.FromResult<IReadOnlyList<ClosedPosition>>(result);
    }
}

public sealed class InMemoryStateStore : ITradingStateStore
{
    private TradingSystemState _state = new();

    public Task<TradingSystemState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_state);

    public Task<TradingSystemState> UpdateAsync(Func<TradingSystemState, TradingSystemState> update, CancellationToken cancellationToken)
    {
        _state = update(_state);
        return Task.FromResult(_state);
    }
}

public sealed class InMemoryJournal : IDecisionJournal, IMarketSnapshotSink
{
    public ConcurrentDictionary<DecisionState, int> DecisionCounts { get; } = new();

    public List<DecisionRecord> Decisions { get; } = [];

    public bool KeepDecisions { get; init; }

    public Task RecordDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken)
    {
        DecisionCounts.AddOrUpdate(decision.State, 1, (_, n) => n + 1);
        if (KeepDecisions)
        {
            Decisions.Add(decision);
        }

        return Task.CompletedTask;
    }

    public Task RecordAuditAsync(string actor, string action, string details, string? correlationId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task PublishAsync(MarketSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishQuotesAsync(IReadOnlyCollection<Quote> quotes, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Clock driven by the replayed bars, so data-freshness checks behave as they would live.</summary>
public sealed class ReplayClock : IClock
{
    public DateTime UtcNow { get; set; }
}
