using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>
/// Executes trades as Deriv multiplier contracts (MULTUP/MULTDOWN) with broker-side stop loss and take profit, on a
/// demo account by default. Deriv is authoritative for open contracts; the local tables hold the strategy context
/// (score, R, MAE/MFE) and the idempotency record for every submission.
/// </summary>
public sealed class DerivBroker(
    DerivSession session,
    IDbContextFactory<TradingDbContext> dbFactory,
    DerivOptions options,
    TradingEngineOptions engineOptions,
    IRiskOptionsSource risk,
    IClock clock,
    ILogger<DerivBroker> logger) : IExecutionBroker
{
    public const string BrokerName = "Deriv";
    private static readonly TimeSpan UnknownMatchWindow = TimeSpan.FromMinutes(2);

    private readonly Dictionary<Instrument, IReadOnlyList<int>> _multipliers = new();
    private readonly Dictionary<Instrument, Quote> _quotes = new();
    private (DateTime BarClose, IReadOnlyList<PortfolioContract> Contracts)? _portfolioCache;
    private bool _commissionsBackfilled;

    public void UpdateQuotes(IEnumerable<Quote> quotes)
    {
        foreach (var quote in quotes)
        {
            _quotes[quote.Instrument] = quote;
        }
    }

    public BrokerDescriptor Descriptor => new(BrokerName, session.Account?.AccountId, session.Account?.IsDemo ?? true);

    private string AccountId => session.Account?.AccountId ?? throw new InvalidOperationException("Deriv session is not connected.");

    public async Task<BrokerAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        var socket = await session.GetSocketAsync(cancellationToken);
        var response = await socket.SendAsync(new JsonObject { ["balance"] = 1 }, cancellationToken);
        var balance = response.GetProperty("balance");
        var amount = Dec(balance.GetProperty("balance"));
        var currency = balance.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : session.Account!.Currency;
        if (!string.Equals(currency, engineOptions.AccountCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Deriv account currency {currency} does not match Trading:AccountCurrency {engineOptions.AccountCurrency}.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerAccounts.SingleOrDefaultAsync(a => a.AccountKey == AccountId, cancellationToken);
        if (row is null)
        {
            row = new BrokerAccountEntity { AccountKey = AccountId, Broker = BrokerName, Currency = currency, StartingBalance = amount };
            db.BrokerAccounts.Add(row);
        }

        row.IsDemo = session.Account!.IsDemo;
        row.LastBalance = amount;
        row.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (!_commissionsBackfilled)
        {
            _commissionsBackfilled = true;
            await BackfillCommissionsAsync(socket, cancellationToken);
        }

        return new BrokerAccount(currency, amount, row.StartingBalance);
    }

    /// <summary>
    /// Trades recorded before commissions were stored get theirs from Deriv once per start. Best effort: a failure
    /// only leaves the fee blank in the trade history.
    /// </summary>
    private async Task BackfillCommissionsAsync(IDerivSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var accountId = AccountId;
            var missing = await db.Positions
                .Where(p => p.Broker == BrokerName && p.BrokerAccountId == accountId && p.BrokerContractId != null && p.Commission == null)
                .OrderByDescending(p => p.OpenedAtUtc).Take(100).ToListAsync(cancellationToken);
            foreach (var position in missing)
            {
                var contract = (await socket.SendAsync(new JsonObject
                {
                    ["proposal_open_contract"] = 1,
                    ["contract_id"] = long.Parse(position.BrokerContractId!, CultureInfo.InvariantCulture)
                }, cancellationToken)).GetProperty("proposal_open_contract");
                position.Commission = ContractCommission(contract);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DerivApiException or DerivConnectionException or KeyNotFoundException or FormatException)
        {
            logger.LogWarning(ex, "Could not load past Deriv commissions");
        }
    }

    public async Task<IReadOnlyCollection<OpenPosition>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        await session.GetSocketAsync(cancellationToken);
        var accountId = AccountId;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var local = await db.Positions.AsNoTracking()
            .Where(p => p.IsOpen && p.Broker == BrokerName && p.BrokerAccountId == accountId)
            .ToListAsync(cancellationToken);
        var result = local.Select(ToModel).ToList();

        // Broker state is authoritative: contracts opened outside this app still count toward exposure limits.
        var tracked = local.Select(p => p.BrokerContractId).ToHashSet();
        foreach (var contract in await PortfolioAsync(null, cancellationToken))
        {
            if (!tracked.Contains(contract.ContractId) && contract.Instrument is { } instrument && contract.Direction is { } direction)
            {
                result.Add(new OpenPosition(Guid.Empty, $"external-{contract.ContractId}", instrument, direction, 0, 0, 0, 0, 0,
                    contract.PurchaseTimeUtc, "External", 0, 0, 0));
            }
        }

        return result;
    }

    public async Task<IReadOnlyCollection<BrokerOrder>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().Where(o => o.Broker == BrokerName)
            .OrderByDescending(o => o.RecordedAtUtc).Take(500)
            .Select(o => new BrokerOrder(o.Id, o.ClientOrderId, o.Instrument, o.Direction, o.Units, o.FillPrice,
                Enum.Parse<OrderStatus>(o.Status), o.RejectReason, o.MarketTimeUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<Candle>> GetCandlesAsync(Instrument instrument, TimeFrame timeFrame, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Candle>>([]);

    public async Task<OrderResult> PlaceOrderAsync(TradeOrder order, CancellationToken cancellationToken)
    {
        var socket = await session.GetSocketAsync(cancellationToken);

        // 1. Idempotency: the unique index on client_order_id admits exactly one submission per signal.
        var entity = NewOrder(order);
        if (await TryInsertAsync(entity, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        if (!_quotes.TryGetValue(order.Instrument, out var quote))
        {
            return await RejectAsync(entity, "No current quote for this instrument.", cancellationToken);
        }

        // 2. Map the risk-sized position onto a multiplier contract.
        var converter = new CurrencyConverter(engineOptions.AccountCurrency, _quotes.ToDictionary(q => q.Key.Symbol, q => q.Value.Mid));
        var entry = order.Direction == Direction.Long ? quote.Ask : quote.Bid;
        var notional = converter.Notional(order.Instrument, order.Units, entry);
        var (estimate, rejection) = DerivContractMath.Plan(order.Direction, notional, entry, order.StopLoss, order.TakeProfit,
            await MultipliersAsync(socket, order.Instrument, cancellationToken), options.MinStake, options.MaxStake,
            risk.Current.AssumedCommissionPercent / 100m);
        if (estimate is null)
        {
            return await RejectAsync(entity, rejection!, cancellationToken);
        }

        // 3. Ask Deriv for the actual commission, set stop/target amounts from it, then price the final contract.
        JsonElement proposal;
        MultiplierContractPlan plan;
        try
        {
            var quoted = await ProposeAsync(socket, order, estimate, withLimits: false, cancellationToken);
            var commission = quoted.TryGetProperty("commission", out var cm) ? Dec(cm) : estimate.Notional * risk.Current.AssumedCommissionPercent / 100m;
            var (priced, pricedRejection) = DerivContractMath.WithQuotedCommission(estimate, entry, order.StopLoss, order.TakeProfit, commission,
                risk.Current.MaxCommissionShareOfRisk);
            if (priced is null)
            {
                return await RejectAsync(entity, pricedRejection!, cancellationToken);
            }

            plan = priced;
            proposal = await ProposeAsync(socket, order, plan, withLimits: true, cancellationToken);
        }
        catch (Exception ex) when (ex is DerivApiException or DerivConnectionException)
        {
            // Nothing was bought: a failed proposal is always safe to reject.
            return await RejectAsync(entity, $"Proposal failed: {ex.Message}", cancellationToken);
        }

        var spot = proposal.TryGetProperty("spot", out var sp) ? Dec(sp) : entry;
        var brokerStop = LimitLevel(proposal, "stop_loss") ?? order.StopLoss;
        var brokerTarget = LimitLevel(proposal, "take_profit") ?? order.TakeProfit;
        if (!DerivContractMath.StopWithinTolerance(spot, order.StopLoss, brokerStop, options.StopPriceTolerance))
        {
            return await RejectAsync(entity,
                $"Broker stop {brokerStop} deviates from strategy stop {order.StopLoss} by more than {options.StopPriceTolerance:P0} of the stop distance.",
                cancellationToken);
        }

        var askPrice = Dec(proposal.GetProperty("ask_price"));
        var proposalId = proposal.GetProperty("id").GetString()!;

        // 4. Buy. An explicit API error means "not bought"; a timeout or dropped connection means "unknown".
        JsonElement buy;
        var submittedAt = clock.UtcNow;
        try
        {
            buy = (await socket.SendAsync(new JsonObject
            {
                ["buy"] = proposalId,
                ["price"] = askPrice.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken)).GetProperty("buy");
        }
        catch (DerivApiException ex)
        {
            return await RejectAsync(entity, $"Buy rejected: {ex.Message}", cancellationToken);
        }
        catch (DerivConnectionException ex)
        {
            logger.LogError(ex, "Execution state unknown for {ClientOrderId}; reconciling with Deriv portfolio", order.ClientOrderId);
            await UpdateOrderAsync(entity.Id, o => { o.Status = nameof(OrderStatus.Unknown); o.RejectReason = ex.Message; }, cancellationToken);
            var recovered = await TryResolveUnknownAsync(entity.Id, cancellationToken);
            return recovered ?? new OrderResult(order.ClientOrderId, OrderStatus.Unknown, entity.Id, null, null,
                "Buy outcome unknown after a connection failure; the order will not be retried automatically.");
        }

        var contractId = ReadId(buy, "contract_id");
        var chargedCommission = proposal.TryGetProperty("commission", out var fee) ? Dec(fee) : (decimal?)null;
        var position = await RecordFillAsync(entity.Id, order, contractId, spot, brokerStop, brokerTarget, plan, chargedCommission, submittedAt,
            cancellationToken);
        logger.LogInformation("Deriv contract {ContractId} bought: {Type} x{Multiplier} stake {Stake} SL {StopLoss} TP {TakeProfit}",
            contractId, plan.ContractType, plan.Multiplier, plan.Stake, plan.StopLossAmount, plan.TakeProfitAmount);
        return new OrderResult(order.ClientOrderId, OrderStatus.Filled, entity.Id, position.Id, spot, null);
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OrderResult.Rejected(orderId, "Multiplier contracts open immediately; there is nothing pending to cancel."));

    public async Task<OrderResult> ClosePositionAsync(string positionId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(positionId, out var id))
        {
            return OrderResult.Rejected(positionId, "Invalid position id.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Broker == BrokerName, cancellationToken);
        if (position?.BrokerContractId is not { } contractId)
        {
            return OrderResult.Rejected(positionId, "Unknown Deriv position.");
        }

        var socket = await session.GetSocketAsync(cancellationToken);
        try
        {
            // price 0 = sell at the current market price.
            await socket.SendAsync(new JsonObject { ["sell"] = long.Parse(contractId, CultureInfo.InvariantCulture), ["price"] = 0 }, cancellationToken);
        }
        catch (DerivApiException ex) when (ex.Code == "ContractNotFound"
                                           || ex.Message.Contains("sold", StringComparison.OrdinalIgnoreCase)
                                           || ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            // Deriv no longer lists it as open (already sold, stopped out or hit its target, possibly by this very
            // request): treat as closed and let reconciliation read the actual result from Deriv.
            logger.LogInformation("Deriv contract {ContractId} is no longer open: {Message}", contractId, ex.Message);
        }
        catch (DerivApiException ex)
        {
            return OrderResult.Rejected(position.ClientOrderId, $"Deriv refused the close: {ex.Message}");
        }

        _portfolioCache = null; // the next reconciliation must see the sale
        return new OrderResult(position.ClientOrderId, OrderStatus.Filled, position.OrderId, position.Id, null, null);
    }

    public async Task<IReadOnlyList<ClosedPosition>> ProcessBarAsync(Instrument instrument, Candle bar, CurrencyConverter converter, CancellationToken cancellationToken)
    {
        _quotes[instrument] = new Quote(instrument, bar.CloseTimeUtc, instrument.RoundPrice(bar.CloseBid), instrument.RoundPrice(bar.CloseAsk));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await ResolveUnknownOrdersAsync(db, instrument, cancellationToken);

        var accountId = AccountId;
        var symbol = instrument.Symbol;
        var open = await db.Positions
            .Where(p => p.IsOpen && p.Broker == BrokerName && p.BrokerAccountId == accountId && p.Instrument == symbol)
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var live = (await PortfolioAsync(bar.CloseTimeUtc, cancellationToken)).Select(c => c.ContractId).ToHashSet();
        var socket = await session.GetSocketAsync(cancellationToken);
        var closed = new List<ClosedPosition>();

        foreach (var entity in open)
        {
            var tracked = PaperExecutionModel.TrackExcursion(ToModel(entity), bar);
            entity.MaxFavorableExcursion = tracked.MaxFavorableExcursion;
            entity.MaxAdverseExcursion = tracked.MaxAdverseExcursion;
            if (entity.BrokerContractId is not { } contractId || live.Contains(contractId))
            {
                continue;
            }

            var contract = (await socket.SendAsync(new JsonObject
            {
                ["proposal_open_contract"] = 1,
                ["contract_id"] = long.Parse(contractId, CultureInfo.InvariantCulture)
            }, cancellationToken)).GetProperty("proposal_open_contract");

            if (!IsSold(contract))
            {
                continue; // Not in the portfolio snapshot yet but still open; check again next bar.
            }

            var profit = contract.TryGetProperty("profit", out var pr) ? Dec(pr) : 0m;
            var exitPrice = FirstDecimal(contract, "exit_tick", "exit_spot", "sell_spot", "current_spot") ?? bar.Close;
            var sellTime = FirstLong(contract, "sell_time", "exit_tick_time", "date_expiry") is { } epoch
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime
                : bar.CloseTimeUtc;
            var reason = DerivContractMath.ClassifyExit(tracked.Direction, exitPrice, entity.StopLoss, entity.TakeProfit) switch
            {
                ExitClassification.TakeProfit => ExitReason.TakeProfit,
                ExitClassification.StopLoss => ExitReason.StopLoss,
                _ => ExitReason.Manual
            };

            var r = PaperExecutionModel.RMultiple(tracked, profit);
            entity.IsOpen = false;
            entity.ClosedAtUtc = sellTime;
            entity.ExitPrice = exitPrice;
            entity.ExitReason = reason.ToString();
            entity.RealizedPnl = profit;
            entity.Commission = ContractCommission(contract) ?? entity.Commission;
            entity.RMultiple = r;
            entity.MaePips = Math.Round(instrument.ToPips(tracked.MaxAdverseExcursion), 1);
            entity.MfePips = Math.Round(instrument.ToPips(tracked.MaxFavorableExcursion), 1);
            closed.Add(new ClosedPosition(tracked, sellTime, exitPrice, reason, profit, r));
        }

        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    private async Task<JsonElement> ProposeAsync(IDerivSocket socket, TradeOrder order, MultiplierContractPlan plan, bool withLimits,
        CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["proposal"] = 1,
            ["amount"] = plan.Stake,
            ["basis"] = "stake",
            ["contract_type"] = plan.ContractType,
            ["currency"] = engineOptions.AccountCurrency,
            ["underlying_symbol"] = DerivSymbols.For(order.Instrument),
            ["multiplier"] = plan.Multiplier
        };
        if (withLimits)
        {
            request["limit_order"] = new JsonObject { ["stop_loss"] = plan.StopLossAmount, ["take_profit"] = plan.TakeProfitAmount };
        }

        return (await socket.SendAsync(request, cancellationToken)).GetProperty("proposal");
    }

    private async Task<IReadOnlyList<int>> MultipliersAsync(IDerivSocket socket, Instrument instrument, CancellationToken cancellationToken)
    {
        if (_multipliers.TryGetValue(instrument, out var cached))
        {
            return cached;
        }

        var response = await socket.SendAsync(new JsonObject { ["contracts_for"] = DerivSymbols.For(instrument) }, cancellationToken);
        var multipliers = response.GetProperty("contracts_for").GetProperty("available").EnumerateArray()
            .Where(a => a.TryGetProperty("contract_type", out var t) && t.GetString() == "MULTUP" && a.TryGetProperty("multiplier_range", out _))
            .SelectMany(a => a.GetProperty("multiplier_range").EnumerateArray().Select(m => m.GetInt32()))
            .Distinct().Order().ToList();
        _multipliers[instrument] = multipliers;
        return multipliers;
    }

    private sealed record PortfolioContract(string ContractId, Instrument? Instrument, Direction? Direction, DateTime PurchaseTimeUtc, decimal BuyPrice);

    /// <summary>Open contracts on the Deriv account. Cached per bar so one cycle makes one portfolio call.</summary>
    private async Task<IReadOnlyList<PortfolioContract>> PortfolioAsync(DateTime? barClose, CancellationToken cancellationToken)
    {
        if (barClose is not null && _portfolioCache is { } cache && cache.BarClose == barClose)
        {
            return cache.Contracts;
        }

        var socket = await session.GetSocketAsync(cancellationToken);
        var response = await socket.SendAsync(new JsonObject { ["portfolio"] = 1 }, cancellationToken);
        var contracts = response.GetProperty("portfolio").GetProperty("contracts").EnumerateArray()
            .Select(c => new PortfolioContract(
                ReadId(c, "contract_id"),
                DerivSymbols.ToInstrument(FirstString(c, "underlying_symbol", "symbol", "underlying")),
                FirstString(c, "contract_type") switch { "MULTUP" => Direction.Long, "MULTDOWN" => Direction.Short, _ => null },
                FirstLong(c, "purchase_time", "date_start") is { } t ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : DateTime.MinValue,
                c.TryGetProperty("buy_price", out var bp) ? Dec(bp) : 0))
            .ToList();

        if (barClose is not null)
        {
            _portfolioCache = (barClose.Value, contracts);
        }

        return contracts;
    }

    private async Task ResolveUnknownOrdersAsync(TradingDbContext db, Instrument instrument, CancellationToken cancellationToken)
    {
        var symbol = instrument.Symbol;
        var unknownIds = await db.Orders.AsNoTracking()
            .Where(o => o.Broker == BrokerName && o.Status == nameof(OrderStatus.Unknown) && o.Instrument == symbol)
            .Select(o => o.Id).ToListAsync(cancellationToken);
        foreach (var id in unknownIds)
        {
            await TryResolveUnknownAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Looks for an untracked contract matching an order whose buy outcome is unknown. If found the order is marked
    /// filled and tracked; otherwise it stays Unknown (and the kill switch stays on) for a human to review.
    /// </summary>
    private async Task<OrderResult?> TryResolveUnknownAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        var direction = Enum.Parse<Direction>(order.Direction);
        var tracked = await db.Positions.Where(p => p.BrokerContractId != null).Select(p => p.BrokerContractId!).ToListAsync(cancellationToken);
        var match = (await PortfolioAsync(null, cancellationToken))
            .Where(c => !tracked.Contains(c.ContractId) && c.Instrument?.Symbol == order.Instrument && c.Direction == direction
                        && c.PurchaseTimeUtc >= order.RecordedAtUtc - UnknownMatchWindow && c.PurchaseTimeUtc <= order.RecordedAtUtc + UnknownMatchWindow)
            .OrderBy(c => c.PurchaseTimeUtc)
            .FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        logger.LogWarning("Resolved unknown order {ClientOrderId} to Deriv contract {ContractId}", order.ClientOrderId, match.ContractId);
        var instrument = Instruments.Get(order.Instrument);
        var tradeOrder = new TradeOrder(order.ClientOrderId, instrument, direction, order.Units, order.RequestedPrice, order.StopLoss,
            order.TakeProfit, 0, "Recovered", 0, order.DecisionId, order.CorrelationId);
        var plan = new MultiplierContractPlan(direction == Direction.Long ? "MULTUP" : "MULTDOWN", 0, match.BuyPrice, 0, 0, 0);
        var position = await RecordFillAsync(order.Id, tradeOrder, match.ContractId, order.RequestedPrice, order.StopLoss, order.TakeProfit, plan,
            null, match.PurchaseTimeUtc, cancellationToken);
        return new OrderResult(order.ClientOrderId, OrderStatus.Filled, order.Id, position.Id, order.RequestedPrice, null);
    }

    private async Task<PositionEntity> RecordFillAsync(Guid orderId, TradeOrder order, string contractId, decimal entry, decimal stop,
        decimal target, MultiplierContractPlan plan, decimal? commission, DateTime openedAt, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        entity.Status = nameof(OrderStatus.Filled);
        entity.FillPrice = entry;
        entity.BrokerContractId = contractId;
        entity.RejectReason = null;

        var position = new PositionEntity
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ClientOrderId = order.ClientOrderId,
            Broker = BrokerName,
            BrokerAccountId = AccountId,
            BrokerContractId = contractId,
            BrokerStake = plan.Stake,
            BrokerMultiplier = plan.Multiplier == 0 ? null : plan.Multiplier,
            Instrument = order.Instrument.Symbol,
            Direction = order.Direction.ToString(),
            Units = order.Units,
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = target,
            InitialRiskAmount = plan.StopLossAmount == 0 ? order.RiskAmount : plan.StopLossAmount,
            OpenedAtUtc = _quotes.TryGetValue(order.Instrument, out var q) ? q.TimestampUtc : openedAt,
            Strategy = order.Strategy,
            Score = order.Score,
            Commission = commission,
            IsOpen = true
        };
        db.Positions.Add(position);
        await db.SaveChangesAsync(cancellationToken);
        return position;
    }

    /// <summary>
    /// Claims the client order id for this submission. A previous attempt that was rejected before anything was bought
    /// may be retried (the row is atomically moved back to Submitting); a filled, submitting or unknown order never is.
    /// </summary>
    private async Task<OrderResult?> TryInsertAsync(OrderEntity entity, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var clientOrderId = entity.ClientOrderId;
        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
        if (existing is null)
        {
            db.Orders.Add(entity);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateException ex) when (DatabaseSetup.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                existing = await db.Orders.AsNoTracking().SingleAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
            }
        }

        if (existing.Status == nameof(OrderStatus.Rejected))
        {
            var rejected = nameof(OrderStatus.Rejected);
            var claimed = await db.Orders
                .Where(o => o.Id == existing.Id && o.Status == rejected)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(o => o.Status, "Submitting")
                    .SetProperty(o => o.RejectReason, (string?)null)
                    .SetProperty(o => o.Units, entity.Units)
                    .SetProperty(o => o.RequestedPrice, entity.RequestedPrice)
                    .SetProperty(o => o.StopLoss, entity.StopLoss)
                    .SetProperty(o => o.TakeProfit, entity.TakeProfit)
                    .SetProperty(o => o.DecisionId, entity.DecisionId)
                    .SetProperty(o => o.CorrelationId, entity.CorrelationId)
                    .SetProperty(o => o.RecordedAtUtc, entity.RecordedAtUtc), cancellationToken);
            if (claimed == 1)
            {
                entity.Id = existing.Id;
                logger.LogInformation("Retrying previously rejected order {ClientOrderId}", clientOrderId);
                return null;
            }
        }

        logger.LogWarning("Duplicate submission blocked for {ClientOrderId} (existing status {Status})", clientOrderId, existing.Status);
        return new OrderResult(clientOrderId, OrderStatus.Duplicate, existing.Id, null, existing.FillPrice,
            $"Signal already submitted (status {existing.Status}); not sent again.");
    }

    private async Task<OrderResult> RejectAsync(OrderEntity entity, string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Deriv order {ClientOrderId} rejected: {Reason}", entity.ClientOrderId, reason);
        await UpdateOrderAsync(entity.Id, o => { o.Status = nameof(OrderStatus.Rejected); o.RejectReason = reason; }, cancellationToken);
        return OrderResult.Rejected(entity.ClientOrderId, reason);
    }

    private async Task UpdateOrderAsync(Guid id, Action<OrderEntity> change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.SingleAsync(o => o.Id == id, cancellationToken);
        change(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    private OrderEntity NewOrder(TradeOrder order) => new()
    {
        Id = Guid.NewGuid(),
        ClientOrderId = order.ClientOrderId,
        Broker = BrokerName,
        BrokerAccountId = AccountId,
        DecisionId = order.DecisionId,
        CorrelationId = order.CorrelationId,
        Instrument = order.Instrument.Symbol,
        Direction = order.Direction.ToString(),
        Units = order.Units,
        RequestedPrice = order.RequestedPrice,
        StopLoss = order.StopLoss,
        TakeProfit = order.TakeProfit,
        Status = "Submitting",
        MarketTimeUtc = _quotes.TryGetValue(order.Instrument, out var q) ? q.TimestampUtc : clock.UtcNow,
        RecordedAtUtc = clock.UtcNow
    };

    private static OpenPosition ToModel(PositionEntity p) => new(
        p.Id, p.ClientOrderId, Instruments.Get(p.Instrument), Enum.Parse<Direction>(p.Direction), p.Units, p.EntryPrice, p.StopLoss,
        p.TakeProfit, p.InitialRiskAmount, DateTime.SpecifyKind(p.OpenedAtUtc, DateTimeKind.Utc), p.Strategy, p.Score,
        p.MaxFavorableExcursion, p.MaxAdverseExcursion);

    internal static decimal? LimitLevel(JsonElement proposal, string name) =>
        proposal.TryGetProperty("limit_order", out var limits) && limits.TryGetProperty(name, out var level)
        && level.TryGetProperty("value", out var value)
            ? Dec(value)
            : null;

    internal static bool IsSold(JsonElement contract) =>
        (contract.TryGetProperty("is_sold", out var sold) && sold.ValueKind switch
        {
            JsonValueKind.Number => sold.GetInt32() == 1,
            JsonValueKind.True => true,
            JsonValueKind.String => sold.GetString() is "1" or "true",
            _ => false
        })
        || (contract.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() is "sold" or "won" or "lost");

    /// <summary>Commission Deriv charged on a contract, when it reports one.</summary>
    internal static decimal? ContractCommission(JsonElement contract) =>
        contract.TryGetProperty("commission", out var c) && c.ValueKind is JsonValueKind.Number or JsonValueKind.String ? Dec(c) : null;

    /// <summary>Deriv sends some numbers as JSON strings (e.g. "profit": "0.87"); accept both.</summary>
    internal static decimal Dec(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new FormatException($"Expected a number from Deriv but got {value.ValueKind} '{value}'.")
    };

    internal static string ReadId(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    internal static string? FirstString(JsonElement element, params string[] names) =>
        names.Select(n => element.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
            .FirstOrDefault(v => v is not null);

    internal static decimal? FirstDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            }
        }

        return null;
    }

    internal static long? FirstLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ls)) return ls;
            }
        }

        return null;
    }
}
