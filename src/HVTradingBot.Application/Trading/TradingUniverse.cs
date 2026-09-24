using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.Trading;

/// <summary>
/// The markets the engine works with. <see cref="Traded"/> are evaluated by strategies; <see cref="Data"/> adds the
/// currency pairs needed only to convert prices into the account currency (e.g. AUD/USD when trading EUR/AUD).
/// Set once at worker startup, before market data and the engine are initialized.
/// </summary>
public sealed class TradingUniverse
{
    public IReadOnlyList<Instrument> Traded { get; private set; } = [];

    public IReadOnlyList<Instrument> Data { get; private set; } = [];

    public bool IsConfigured => Traded.Count > 0;

    public void Configure(IEnumerable<Instrument> traded, string accountCurrency)
    {
        var tradedList = traded.Distinct().ToList();
        if (tradedList.Count == 0)
        {
            throw new ArgumentException("At least one instrument must be selected.", nameof(traded));
        }

        Traded = tradedList;
        Data = tradedList.Concat(Instruments.ConversionPairs(tradedList, accountCurrency)).Distinct().ToList();
    }

    public static TradingUniverse From(IEnumerable<Instrument> traded, string accountCurrency)
    {
        var universe = new TradingUniverse();
        universe.Configure(traded, accountCurrency);
        return universe;
    }
}
