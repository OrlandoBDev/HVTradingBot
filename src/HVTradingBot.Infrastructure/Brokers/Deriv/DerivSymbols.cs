using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

public static class DerivSymbols
{
    public static string For(Instrument instrument) =>
        instrument.BrokerSymbol ?? $"frx{instrument.BaseCurrency}{instrument.QuoteCurrency}";

    public static Instrument? ToInstrument(string? brokerSymbol) =>
        string.IsNullOrEmpty(brokerSymbol) ? null : Instruments.All.FirstOrDefault(i => string.Equals(For(i), brokerSymbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Contract type and underlying from a Deriv shortcode such as "MULTUP_FRXEURUSD_10.00_100_1695712345_..." or
    /// "MULTDOWN_R_100_5.00_…" (synthetic symbols contain underscores, so known symbols are matched first).
    /// </summary>
    public static (string ContractType, string? Symbol) ParseShortcode(string? shortcode)
    {
        var split = shortcode?.IndexOf('_') ?? -1;
        if (split < 0)
        {
            return (shortcode ?? "", null);
        }

        var contractType = shortcode![..split];
        var rest = shortcode[(split + 1)..];
        var known = Instruments.All.Select(For)
            .Where(symbol => rest.StartsWith(symbol + "_", StringComparison.OrdinalIgnoreCase) || rest.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .MaxBy(symbol => symbol.Length);
        if (known is not null)
        {
            return (contractType, known);
        }

        // Unknown underlying: everything up to the stake, the first part with decimals ("10.00"); symbols such as
        // R_100 contain whole numbers themselves.
        var parts = rest.Split('_');
        var symbolParts = parts.TakeWhile(part => !(part.Contains('.')
            && decimal.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))).ToList();
        return (contractType, symbolParts.Count == 0 ? null : string.Join('_', symbolParts));
    }
}
