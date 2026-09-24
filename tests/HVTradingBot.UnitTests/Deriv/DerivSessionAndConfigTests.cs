using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.MarketData;
using Microsoft.Extensions.Options;

namespace HVTradingBot.UnitTests.Deriv;

public class DerivSessionAndConfigTests
{
    private static readonly DerivAccount Real = new("CR123", "real", "USD");
    private static readonly DerivAccount Demo = new("DOT90004580", "demo", "USD");

    [Fact]
    public void Selects_the_demo_account_even_when_a_real_account_is_listed_first() =>
        Assert.Equal(Demo, DerivSession.SelectAccount([Real, Demo], DerivAccountType.Demo, null));

    [Fact]
    public void Honors_a_pinned_demo_account() =>
        Assert.Equal(Demo, DerivSession.SelectAccount([Real, Demo], DerivAccountType.Demo, "dot90004580"));

    [Fact]
    public void Refuses_a_pinned_real_account() =>
        Assert.Throws<InvalidOperationException>(() => DerivSession.SelectAccount([Real, Demo], DerivAccountType.Demo, "CR123"));

    [Fact]
    public void Fails_when_the_token_has_no_demo_account() =>
        Assert.Throws<InvalidOperationException>(() => DerivSession.SelectAccount([Real], DerivAccountType.Demo, null));

    [Fact]
    public void Refuses_real_account_type_outright() =>
        Assert.Throws<InvalidOperationException>(() => DerivSession.SelectAccount([Real, Demo], DerivAccountType.Real, null));

    [Fact]
    public void Refuses_a_websocket_url_for_the_real_endpoint_when_demo_is_expected()
    {
        DerivSession.EnsureUrlMatchesAccountType(new Uri("wss://api.derivws.com/trading/v1/options/ws/demo?otp=x"), DerivAccountType.Demo);
        Assert.Throws<InvalidOperationException>(() =>
            DerivSession.EnsureUrlMatchesAccountType(new Uri("wss://api.derivws.com/trading/v1/options/ws/real?otp=x"), DerivAccountType.Demo));
    }

    [Fact]
    public void Maps_instruments_to_deriv_symbols_and_back()
    {
        Assert.Equal("frxUSDJPY", DerivSymbols.For(Instruments.UsdJpy));
        Assert.Equal(Instruments.EurUsd, DerivSymbols.ToInstrument("frxEURUSD"));
        Assert.Null(DerivSymbols.ToInstrument("XYZ_999"));
    }

    private static ValidateOptionsResult Validate(DerivOptions deriv, BrokerProvider broker = BrokerProvider.Deriv,
        MarketDataProvider data = MarketDataProvider.Deriv) =>
        new LiveTradingOptionsValidator(Options.Create(new BrokerOptions { Provider = broker }),
            Options.Create(new MarketDataOptions { Provider = data })).Validate(null, deriv);

    [Fact]
    public void Deriv_broker_starts_without_credentials_they_come_from_the_settings_page() =>
        Assert.True(Validate(new DerivOptions()).Succeeded);

    [Fact]
    public void Real_accounts_are_refused_by_configuration() =>
        Assert.True(Validate(new DerivOptions { ApiToken = "t", AppId = "1", AccountType = DerivAccountType.Real }).Failed);

    [Fact]
    public void Deriv_broker_cannot_trade_on_simulated_prices() =>
        Assert.True(Validate(new DerivOptions { ApiToken = "t", AppId = "1" }, data: MarketDataProvider.Simulated).Failed);

    [Fact]
    public void Paper_broker_needs_no_deriv_credentials() =>
        Assert.True(Validate(new DerivOptions(), BrokerProvider.Paper, MarketDataProvider.Simulated).Succeeded);
}
