using System.Text.Json;
using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Strategies;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.UnitTests.TestData;

namespace HVTradingBot.UnitTests.Markets;

public class MarketModelTests
{
    private static readonly Instrument Vol100 = Instruments.Register(new Instrument("R_100", "R_100", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.SyntheticIndex, Name = "Volatility 100 Index", BrokerSymbol = "R_100"
    });

    private static readonly Instrument Us500 = Instruments.Register(new Instrument("OTC_SPC", "OTC_SPC", "USD", 0.01m, 2)
    {
        AssetClass = AssetClass.StockIndex, Name = "US 500", BrokerSymbol = "OTC_SPC", IsTradable = false
    });

    [Fact]
    public void Forex_majors_are_built_in_and_catalog_markets_register_by_symbol()
    {
        Assert.True(Instruments.TryGet("EUR/JPY", out var eurJpy));
        Assert.Equal(0.01m, eurJpy.PipSize);
        Assert.Equal(Vol100, Instruments.Get("r_100"));
        Assert.Equal("R_100", DerivSymbols.For(Vol100));
        Assert.Equal("frxEURUSD", DerivSymbols.For(Instruments.EurUsd));
        Assert.Equal(Vol100, DerivSymbols.ToInstrument("R_100"));
    }

    [Fact]
    public void Conversion_pairs_cover_base_and_quote_currencies()
    {
        var universe = TradingUniverse.From([Instruments.Get("EUR/AUD"), Vol100], "USD");

        Assert.Equal(2, universe.Traded.Count);
        Assert.Contains(Instruments.EurUsd, universe.Data);
        Assert.Contains(Instruments.AudUsd, universe.Data);
        Assert.DoesNotContain(Instruments.UsdJpy, universe.Data);
    }

    [Fact]
    public void Synthetic_index_exposure_counts_only_itself()
    {
        var exposure = new Dictionary<string, int>();
        PortfolioState.Add(exposure, Vol100, Direction.Long);
        PortfolioState.Add(exposure, Instruments.EurUsd, Direction.Long);

        Assert.Equal(1, exposure["R_100"]);
        Assert.Equal(-1, exposure["USD"]); // from EUR/USD only
    }

    [Fact]
    public void Notional_for_indices_is_units_times_price()
    {
        var converter = Bars.Converter();
        Assert.Equal(588m, converter.Notional(Vol100, 1m, 588m));
        Assert.Equal(1_080m, converter.Notional(Instruments.EurUsd, 1_000m, 1.08m));
    }

    [Fact]
    public async Task Synthetic_indices_are_sized_in_fractional_units()
    {
        var risk = new RiskManager(new RiskOptions { MinUnits = 1, UnitStep = 1 }, new ExecutionCostOptions { SlippagePips = 0, CommissionPer100K = 0 });
        var proposal = new TradeProposal(Vol100, new TradeSetup(Direction.Long, 588m, 585m, 596m), new Quote(Vol100, Bars.Start, 587.9m, 588.1m),
            0.2m, "Test", 80, "R100-T-L-1");

        var decision = await risk.EvaluateAsync(proposal, Bars.Portfolio(balance: 100m), CancellationToken.None);

        Assert.True(decision.IsApproved, decision.RejectionReason);
        // 0.5% of 100 = 0.50 at risk over a 3-point stop -> 0.166666 units (~98 USD notional)
        Assert.Equal(0.166666m, decision.Units);
    }

    [Fact]
    public async Task Analysis_only_markets_are_rejected_by_risk()
    {
        var risk = new RiskManager(new RiskOptions(), new ExecutionCostOptions());
        var proposal = new TradeProposal(Us500, new TradeSetup(Direction.Long, 5000m, 4980m, 5050m), new Quote(Us500, Bars.Start, 4999.5m, 5000.5m),
            1m, "Test", 80, "SPC-T-L-1");

        var decision = await risk.EvaluateAsync(proposal, Bars.Portfolio(), CancellationToken.None);

        Assert.Contains(decision.Checks, c => c.Rule == "Tradable" && !c.Passed);
    }

    private static JsonElement Symbol(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("frxEURUSD", "forex", AssetClass.Forex, "EUR/USD", true)]
    [InlineData("frxXAUUSD", "commodities", AssetClass.Commodity, "XAU/USD", true)]
    [InlineData("cryBTCUSD", "cryptocurrency", AssetClass.Crypto, "BTC/USD", true)]
    [InlineData("R_100", "synthetic_index", AssetClass.SyntheticIndex, "R_100", true)]
    [InlineData("OTC_SPC", "indices", AssetClass.StockIndex, "OTC_SPC", false)]
    public void Deriv_catalog_entries_map_to_instruments(string brokerSymbol, string market, AssetClass assetClass, string symbol, bool tradable)
    {
        var json = Symbol($$"""{"underlying_symbol":"{{brokerSymbol}}","underlying_symbol_name":"Name","submarket":"s","pip_size":0.01,"exchange_is_open":1}""");
        var entity = DerivMarketDiscovery.ToEntity(json, brokerSymbol, market, assetClass, tradable ? [100, 200] : [])!;

        Assert.Equal(symbol, entity.Symbol);
        Assert.Equal(tradable, entity.IsTradable);
        Assert.Equal(2, entity.PriceDecimals);
        var instrument = MarketCatalogStore.ToInstrument(entity);
        Assert.Equal(assetClass, instrument.AssetClass);
        Assert.Equal(brokerSymbol, DerivSymbols.For(instrument));
    }
}
