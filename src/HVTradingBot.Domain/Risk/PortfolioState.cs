using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Domain.Risk;

/// <summary>Snapshot of account and system state used for risk decisions.</summary>
/// <param name="MarketTimeUtc">Current market (bar) time; drives daily/weekly windows and cooldowns.</param>
/// <param name="WallClockUtc">Real time; drives data-freshness checks.</param>
public sealed record PortfolioState(
    TradingMode Mode,
    decimal Balance,
    decimal Equity,
    decimal DailyRealizedPnl,
    decimal WeeklyRealizedPnl,
    IReadOnlyList<OpenPosition> OpenPositions,
    int ConsecutiveLosses,
    DateTime? CooldownUntilUtc,
    bool KillSwitchActive,
    string? KillSwitchReason,
    MarketDataStatus MarketData,
    DateTime MarketTimeUtc,
    DateTime WallClockUtc,
    CurrencyConverter Converter)
{
    /// <summary>Realized P&amp;L of Derived (synthetic) markets today, for the Derived daily loss limit.</summary>
    public decimal DerivedDailyRealizedPnl { get; init; }

    public int DerivedOpenPositions => OpenPositions.Count(p => RiskOptions.IsDerived(p.Instrument));

    /// <summary>
    /// Net count of same-direction exposure per currency (long EUR/USD = +1 EUR, -1 USD). Non-currency assets count only
    /// against their own symbol, so e.g. a synthetic index does not add USD exposure.
    /// </summary>
    public IReadOnlyDictionary<string, int> CurrencyExposure()
    {
        var exposure = new Dictionary<string, int>();
        foreach (var p in OpenPositions)
        {
            Add(exposure, p.Instrument, p.Direction);
        }

        return exposure;
    }

    public static void Add(Dictionary<string, int> exposure, Instrument instrument, Direction direction)
    {
        var sign = direction.Sign();
        exposure[instrument.BaseCurrency] = exposure.GetValueOrDefault(instrument.BaseCurrency) + sign;
        if (instrument.IsCurrencyPair)
        {
            exposure[instrument.QuoteCurrency] = exposure.GetValueOrDefault(instrument.QuoteCurrency) - sign;
        }
    }
}
