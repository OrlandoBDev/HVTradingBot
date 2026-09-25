using System.Collections.Concurrent;

namespace HVTradingBot.Domain.MarketData;

public enum AssetClass
{
    Forex,
    Commodity,
    Crypto,
    SyntheticIndex,
    StockIndex
}

/// <summary>
/// A tradable or analysable market. Currency pairs carry real base/quote currencies (used for conversion and
/// currency-exposure limits); other assets use their own symbol as <see cref="BaseCurrency"/> and are priced in
/// <see cref="QuoteCurrency"/>. <see cref="PipSize"/> is the pip for currency pairs and the price increment otherwise.
/// </summary>
public sealed record Instrument(string Symbol, string BaseCurrency, string QuoteCurrency, decimal PipSize, int PriceDecimals)
{
    public AssetClass AssetClass { get; init; } = AssetClass.Forex;

    /// <summary>Human-readable name, e.g. "Volatility 100 Index".</summary>
    public string? Name { get; init; }

    /// <summary>The broker's symbol when it differs from the conventional one (e.g. Deriv "R_100").</summary>
    public string? BrokerSymbol { get; init; }

    /// <summary>False for markets that can be analysed but not traded at the broker (e.g. stock indices without multiplier contracts).</summary>
    public bool IsTradable { get; init; } = true;

    public string DisplayName => Name ?? Symbol;

    public bool IsCurrencyPair => AssetClass == AssetClass.Forex;

    public decimal ToPips(decimal priceDistance) => priceDistance / PipSize;

    public decimal FromPips(decimal pips) => pips * PipSize;

    public decimal RoundPrice(decimal price) => Math.Round(price, PriceDecimals, MidpointRounding.ToEven);

    public override string ToString() => Symbol;

    /// <summary>An instrument is identified by its symbol; metadata (name, tradability) may be refreshed from the catalog.</summary>
    public bool Equals(Instrument? other) => other is not null && string.Equals(Symbol, other.Symbol, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Symbol);

    public static Instrument CurrencyPair(string baseCurrency, string quoteCurrency) =>
        new($"{baseCurrency}/{quoteCurrency}", baseCurrency, quoteCurrency, quoteCurrency == "JPY" ? 0.01m : 0.0001m, quoteCurrency == "JPY" ? 3 : 5);
}

/// <summary>
/// Registry of known instruments. Forex majors are built in; other markets (synthetic indices, stock indices,
/// metals, crypto) are registered at runtime from the broker's market catalog.
/// </summary>
public static class Instruments
{
    public static readonly Instrument EurUsd = Instrument.CurrencyPair("EUR", "USD");
    public static readonly Instrument GbpUsd = Instrument.CurrencyPair("GBP", "USD");
    public static readonly Instrument UsdJpy = Instrument.CurrencyPair("USD", "JPY");
    public static readonly Instrument AudUsd = Instrument.CurrencyPair("AUD", "USD");
    public static readonly Instrument UsdCad = Instrument.CurrencyPair("USD", "CAD");

    /// <summary>The default trading universe (docs/MVP.md).</summary>
    public static IReadOnlyList<Instrument> Defaults { get; } = [EurUsd, GbpUsd, UsdJpy, AudUsd, UsdCad];

    private static readonly ConcurrentDictionary<string, Instrument> Registry = new(StringComparer.OrdinalIgnoreCase);

    static Instruments()
    {
        var majors = Defaults.Concat(new[]
        {
            ("USD", "CHF"), ("EUR", "GBP"), ("EUR", "JPY"), ("GBP", "JPY"), ("AUD", "JPY"),
            ("EUR", "AUD"), ("EUR", "CAD"), ("EUR", "CHF"), ("GBP", "AUD")
        }.Select(p => Instrument.CurrencyPair(p.Item1, p.Item2)));

        foreach (var instrument in majors)
        {
            Registry[instrument.Symbol] = instrument;
        }
    }

    public static IReadOnlyList<Instrument> All =>
        Registry.Values.OrderBy(i => i.AssetClass).ThenBy(i => i.Symbol, StringComparer.Ordinal).ToList();

    /// <summary>Adds or updates an instrument (e.g. from the broker catalog). Built-in currency pairs keep their pip definition.</summary>
    public static Instrument Register(Instrument instrument) =>
        Registry.AddOrUpdate(instrument.Symbol, instrument,
            (_, existing) => existing.IsCurrencyPair
                ? existing with { Name = instrument.Name ?? existing.Name, BrokerSymbol = instrument.BrokerSymbol ?? existing.BrokerSymbol, IsTradable = instrument.IsTradable }
                : instrument);

    public static bool TryGet(string symbol, out Instrument instrument) => Registry.TryGetValue(symbol, out instrument!);

    /// <summary>Readable market name for <paramref name="symbol"/>; the symbol itself when the market is unknown.</summary>
    public static string DisplayNameOf(string symbol) => TryGet(symbol, out var instrument) ? instrument.DisplayName : symbol;

    public static Instrument Get(string symbol) =>
        TryGet(symbol, out var instrument)
            ? instrument
            : throw new ArgumentException($"Unknown instrument '{symbol}'.", nameof(symbol));

    /// <summary>Currency pairs needed to convert every currency of <paramref name="instruments"/> into <paramref name="accountCurrency"/>.</summary>
    public static IReadOnlyList<Instrument> ConversionPairs(IEnumerable<Instrument> instruments, string accountCurrency)
    {
        var currencies = instruments
            .SelectMany(i => i.IsCurrencyPair ? new[] { i.BaseCurrency, i.QuoteCurrency } : new[] { i.QuoteCurrency })
            .Where(c => c != accountCurrency)
            .Distinct();
        var pairs = new List<Instrument>();
        foreach (var currency in currencies)
        {
            var pair = All.FirstOrDefault(i => i.IsCurrencyPair
                && ((i.BaseCurrency == currency && i.QuoteCurrency == accountCurrency) || (i.BaseCurrency == accountCurrency && i.QuoteCurrency == currency)));
            if (pair is not null && !pairs.Contains(pair))
            {
                pairs.Add(pair);
            }
        }

        return pairs;
    }
}
