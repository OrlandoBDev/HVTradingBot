using Ospreya.Domain.MarketData;

namespace Ospreya.Application.Trading;

/// <summary>
/// The markets the engine works with. <see cref="Traded"/> are evaluated by strategies; <see cref="Data"/> adds the
/// currency pairs needed only to convert prices into the account currency (e.g. AUD/USD when trading EUR/AUD), and the
/// <see cref="SessionCandidates"/> when session markets are switched on.
/// Set once at worker startup, before market data and the engine are initialized.
/// </summary>
public sealed class TradingUniverse
{
    public IReadOnlyList<Instrument> Traded { get; private set; } = [];

    public IReadOnlyList<Instrument> Data { get; private set; } = [];

    public bool IsConfigured => Traded.Count > 0;

    /// <summary>
    /// Open Forex pairs outside the selection that may be added while their trading session is active (empty when the
    /// setting is off). Their prices are always followed, so positions on them are monitored after they rotate out.
    /// </summary>
    public IReadOnlyList<Instrument> SessionCandidates { get; private set; } = [];

    /// <summary>The session markets evaluated at <paramref name="utc"/> in addition to <see cref="Traded"/>.</summary>
    public IReadOnlyList<Instrument> SessionMarkets(DateTime utc) =>
        SessionCandidates.Count == 0 ? [] : TradingSessions.Pick(SessionCandidates, utc);

    /// <summary>
    /// When true, Derived (synthetic) markets are only evaluated while Forex is closed (weekends, daily break), so the
    /// engine's position slots go to Forex whenever Forex trades.
    /// </summary>
    public bool DerivedOnlyWhenForexClosed { get; private set; }

    public void Configure(IEnumerable<Instrument> traded, string accountCurrency, bool derivedOnlyWhenForexClosed = false,
        IEnumerable<Instrument>? sessionCandidates = null)
    {
        var tradedList = traded.Distinct().ToList();
        if (tradedList.Count == 0)
        {
            throw new ArgumentException("At least one instrument must be selected.", nameof(traded));
        }

        Traded = tradedList;
        DerivedOnlyWhenForexClosed = derivedOnlyWhenForexClosed;
        SessionCandidates = (sessionCandidates ?? []).Where(i => i.IsCurrencyPair).Except(tradedList).Distinct().ToList();
        var followed = tradedList.Concat(SessionCandidates).ToList();
        Data = followed.Concat(Instruments.ConversionPairs(followed, accountCurrency)).Distinct().ToList();
    }

    public static TradingUniverse From(IEnumerable<Instrument> traded, string accountCurrency, bool derivedOnlyWhenForexClosed = false,
        IEnumerable<Instrument>? sessionCandidates = null)
    {
        var universe = new TradingUniverse();
        universe.Configure(traded, accountCurrency, derivedOnlyWhenForexClosed, sessionCandidates);
        return universe;
    }
}
