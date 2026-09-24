using System.Text.Json.Nodes;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVTradingBot.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DerivBrokerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CurrencyConverter Converter = new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.1m });

    private readonly FakeDerivSocket _socket = new();
    private readonly FakeDerivRest _rest = new();

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private DerivBroker NewBroker()
    {
        var options = new DerivOptions();
        var settings = fixture.NewSettingsStore(new DerivEnvironmentCredentials("123", "token-abcd", null));
        var session = new DerivSession(options, new DerivRestClient(new HttpClient(_rest), options), _socket, settings, settings,
            new FixedClock(), NullLogger<DerivSession>.Instance);
        return new DerivBroker(session, fixture.DbFactory, options, new TradingEngineOptions { Instruments = ["EUR/USD"] },
            new FixedRiskOptions(new RiskOptions()), new FixedClock(), NullLogger<DerivBroker>.Instance);
    }

    private static Candle Bar(DateTime open, decimal low = 1.0995m, decimal high = 1.1005m, decimal close = 1.1m) =>
        new(open, TimeFrame.M5, close, high, low, close, 0.0001m, 10);

    // 27,000 units of EUR/USD at 1.1 = 29,700 USD notional -> x100, stake 297.
    private static TradeOrder Order(string id = "EURUSD-T-L-1") =>
        new(id, Instruments.EurUsd, Direction.Long, 27_000m, 1.10005m, 1.0980m, 1.1041m, 60m, "Test", 80, null, "corr");

    private void AcceptProposalAndBuy(string stopLevel = "1.09800", long contractId = 991)
    {
        _socket.Handlers["proposal"] = req => new
        {
            proposal = new
            {
                id = "prop-1",
                ask_price = req["amount"]!.GetValue<decimal>(),
                spot = 1.10005m,
                commission = 6.3m,
                limit_order = new { stop_loss = new { value = stopLevel }, take_profit = new { value = "1.10410" } }
            }
        };
        _socket.Handlers["buy"] = _ => new { buy = new { contract_id = contractId, buy_price = 297m, purchase_time = 1767607200 } };
    }

    private async Task<DerivBroker> ReadyBrokerAsync()
    {
        var broker = NewBroker();
        await broker.GetAccountAsync(CancellationToken.None); // connects, selects the demo account
        await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0), Converter, CancellationToken.None);
        return broker;
    }

    [Fact]
    public async Task Connects_to_the_demo_account_and_records_its_balance()
    {
        var broker = NewBroker();
        var account = await broker.GetAccountAsync(CancellationToken.None);

        Assert.Equal(10_000m, account.Balance);
        Assert.Equal(new BrokerDescriptor("Deriv", FakeDerivRest.DemoAccount, true), broker.Descriptor);
        Assert.EndsWith("/ws/demo", _socket.ConnectedTo!.AbsolutePath);
        Assert.All(_rest.Requests, r =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization?.Scheme);
            Assert.Equal("123", r.Headers.GetValues("Deriv-App-ID").Single());
        });
    }

    [Fact]
    public async Task Fill_creates_a_tracked_position_with_broker_levels()
    {
        AcceptProposalAndBuy();
        var broker = await ReadyBrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, result.Status);
        var proposals = _socket.Requests.Where(r => r.ContainsKey("proposal")).ToList();
        Assert.Equal(2, proposals.Count);
        Assert.False(proposals[0].ContainsKey("limit_order")); // first: commission quote
        var proposal = proposals[1];
        // stop amount = 29,700 * 0.00205/1.10005 + 6.30 quoted commission = 61.65
        Assert.Equal(61.65m, proposal["limit_order"]!["stop_loss"]!.GetValue<decimal>());
        Assert.Equal("MULTUP", proposal["contract_type"]!.GetValue<string>());
        Assert.Equal("frxEURUSD", proposal["underlying_symbol"]!.GetValue<string>());
        Assert.Equal(100, proposal["multiplier"]!.GetValue<int>());
        Assert.Equal(297m, proposal["amount"]!.GetValue<decimal>());

        var position = Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));
        Assert.Equal("EURUSD-T-L-1", position.ClientOrderId);
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var row = await db.Positions.SingleAsync();
        Assert.Equal("991", row.BrokerContractId);
        Assert.Equal(FakeDerivRest.DemoAccount, row.BrokerAccountId);
    }

    [Fact]
    public async Task Duplicate_client_order_id_sends_only_one_buy()
    {
        AcceptProposalAndBuy();
        var broker = await ReadyBrokerAsync();

        var first = await broker.PlaceOrderAsync(Order(), CancellationToken.None);
        var second = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, first.Status);
        Assert.Equal(OrderStatus.Duplicate, second.Status);
        Assert.Equal(1, _socket.Count("buy"));
    }

    [Fact]
    public async Task Broker_stop_far_from_strategy_stop_is_rejected_before_buying()
    {
        AcceptProposalAndBuy(stopLevel: "1.09950");
        var broker = await ReadyBrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains("deviates", result.RejectReason);
        Assert.Equal(0, _socket.Count("buy"));
    }

    [Fact]
    public async Task Commission_that_dominates_the_risk_budget_is_rejected_before_buying()
    {
        AcceptProposalAndBuy();
        var baseline = _socket.Handlers["proposal"];
        _socket.Handlers["proposal"] = req => req.ContainsKey("limit_order")
            ? baseline(req)
            : new { proposal = new { id = "q", ask_price = 297m, spot = 1.10005m, commission = 40m } };
        var broker = await ReadyBrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains("too small for its fees", result.RejectReason);
        Assert.Equal(0, _socket.Count("buy"));
    }

    [Fact]
    public async Task A_rejected_signal_may_be_retried_but_a_filled_one_never_is()
    {
        AcceptProposalAndBuy(stopLevel: "1.09950"); // first attempt refused before buying
        var broker = await ReadyBrokerAsync();
        Assert.Equal(OrderStatus.Rejected, (await broker.PlaceOrderAsync(Order(), CancellationToken.None)).Status);

        AcceptProposalAndBuy();
        Assert.Equal(OrderStatus.Filled, (await broker.PlaceOrderAsync(Order(), CancellationToken.None)).Status);
        Assert.Equal(OrderStatus.Duplicate, (await broker.PlaceOrderAsync(Order(), CancellationToken.None)).Status);

        Assert.Equal(1, _socket.Count("buy"));
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        Assert.Equal(1, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Explicit_buy_rejection_is_recorded_as_rejected()
    {
        AcceptProposalAndBuy();
        _socket.Handlers["buy"] = _ => throw new DerivApiException("InsufficientBalance", "Your account balance is insufficient.");
        var broker = await ReadyBrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Empty(await broker.GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Lost_buy_response_is_unknown_and_never_retried_then_reconciled_from_portfolio()
    {
        AcceptProposalAndBuy();
        _socket.Handlers["buy"] = _ => throw new DerivConnectionException("No response from Deriv within 15s (buy).");
        var broker = await ReadyBrokerAsync();

        var result = await broker.PlaceOrderAsync(Order(), CancellationToken.None);
        Assert.Equal(OrderStatus.Unknown, result.Status);
        Assert.Equal(1, _socket.Count("buy"));

        // The contract did go through at Deriv; the next bar reconciles it instead of buying again.
        _socket.Handlers["portfolio"] = _ => new
        {
            portfolio = new { contracts = new[] { new { contract_id = 4242L, contract_type = "MULTUP", underlying_symbol = "frxEURUSD", purchase_time = new DateTimeOffset(new FixedClock().UtcNow).ToUnixTimeSeconds(), buy_price = 297m } } }
        };
        await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0.AddMinutes(5)), Converter, CancellationToken.None);

        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var order = await db.Orders.SingleAsync();
        Assert.Equal(nameof(OrderStatus.Filled), order.Status);
        Assert.Equal("4242", (await db.Positions.SingleAsync()).BrokerContractId);
        Assert.Equal(1, _socket.Count("buy"));
    }

    [Fact]
    public async Task Contract_closed_by_the_broker_is_reported_with_broker_profit()
    {
        AcceptProposalAndBuy();
        var broker = await ReadyBrokerAsync();
        await broker.PlaceOrderAsync(Order(), CancellationToken.None);

        // Portfolio no longer lists the contract; Deriv reports it sold at the take-profit level.
        _socket.Handlers["proposal_open_contract"] = _ => new
        {
            proposal_open_contract = new { contract_id = 991, is_sold = 1, status = "sold", profit = 102.4m, exit_tick = 1.10412m, sell_time = 1767609000 }
        };
        var closed = await broker.ProcessBarAsync(Instruments.EurUsd, Bar(T0.AddMinutes(5), high: 1.1045m, close: 1.1042m), Converter, CancellationToken.None);

        var trade = Assert.Single(closed);
        Assert.Equal(ExitReason.TakeProfit, trade.Reason);
        Assert.Equal(102.4m, trade.RealizedPnl);
        Assert.Empty(await broker.GetPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Contracts_opened_outside_the_app_count_as_open_positions()
    {
        _socket.Handlers["portfolio"] = _ => new
        {
            portfolio = new { contracts = new[] { new { contract_id = 77L, contract_type = "MULTDOWN", underlying_symbol = "frxGBPUSD", purchase_time = 1767600000L, buy_price = 50m } } }
        };
        var broker = NewBroker();

        var position = Assert.Single(await broker.GetPositionsAsync(CancellationToken.None));

        Assert.Equal("External", position.Strategy);
        Assert.Equal(Instruments.GbpUsd, position.Instrument);
        Assert.Equal(Direction.Short, position.Direction);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 5, 10, 5, 3, DateTimeKind.Utc);
    }
}
