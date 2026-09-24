using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

public static class DerivSymbols
{
    public static string For(Instrument instrument) =>
        instrument.BrokerSymbol ?? $"frx{instrument.BaseCurrency}{instrument.QuoteCurrency}";

    public static Instrument? ToInstrument(string? brokerSymbol) =>
        string.IsNullOrEmpty(brokerSymbol) ? null : Instruments.All.FirstOrDefault(i => For(i) == brokerSymbol);
}
