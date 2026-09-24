namespace HVTradingBot.Domain.MarketData;

/// <summary>Health of the market-data feed.</summary>
/// <param name="LastBarTimeUtc">Market time of the most recent bar.</param>
/// <param name="LastReceivedUtc">Wall-clock time when the feed last delivered data.</param>
public sealed record MarketDataStatus(DateTime? LastBarTimeUtc, DateTime? LastReceivedUtc)
{
    public bool IsStale(DateTime wallClockUtc, TimeSpan maxAge) =>
        LastReceivedUtc is null || wallClockUtc - LastReceivedUtc.Value > maxAge;
}
