using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Execution;

/// <summary>
/// Converts amounts denominated in an instrument's quote currency into the account currency,
/// using the latest mid prices of the configured instruments.
/// </summary>
public sealed class CurrencyConverter(string accountCurrency, IReadOnlyDictionary<string, decimal> midPrices)
{
    public string AccountCurrency { get; } = accountCurrency;

    public decimal QuoteToAccountRate(Instrument instrument) => Rate(instrument.QuoteCurrency);

    /// <summary>
    /// Position value in the account currency: units of the base currency for currency pairs, otherwise
    /// units x price in the quote currency.
    /// </summary>
    public decimal Notional(Instrument instrument, decimal units, decimal price) =>
        instrument.IsCurrencyPair ? units * Rate(instrument.BaseCurrency) : units * price * Rate(instrument.QuoteCurrency);

    /// <summary>Rate that converts one unit of <paramref name="currency"/> into the account currency.</summary>
    public decimal Rate(string currency)
    {
        if (currency == AccountCurrency)
        {
            return 1m;
        }

        foreach (var (symbol, mid) in midPrices)
        {
            if (mid <= 0 || !Instruments.TryGet(symbol, out var instrument) || !instrument.IsCurrencyPair) continue;
            if (instrument.BaseCurrency == currency && instrument.QuoteCurrency == AccountCurrency) return mid;
            if (instrument.BaseCurrency == AccountCurrency && instrument.QuoteCurrency == currency) return 1m / mid;
        }

        throw new InvalidOperationException($"No price available to convert {currency} into {AccountCurrency}.");
    }
}
