namespace HVTradingBot.Domain.MarketData;

/// <summary>
/// Mid-price OHLC bar with the bid/ask spread observed at the close.
/// Bid = mid - spread/2, Ask = mid + spread/2.
/// </summary>
public sealed record Candle(
    DateTime OpenTimeUtc,
    TimeFrame TimeFrame,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Spread,
    long Volume)
{
    public DateTime CloseTimeUtc => OpenTimeUtc + TimeFrame.Duration();

    public decimal HalfSpread => Spread / 2m;

    public decimal CloseBid => Close - HalfSpread;

    public decimal CloseAsk => Close + HalfSpread;

    public decimal Range => High - Low;
}

/// <summary>Latest tradable price for an instrument.</summary>
public sealed record Quote(Instrument Instrument, DateTime TimestampUtc, decimal Bid, decimal Ask)
{
    public decimal Mid => (Bid + Ask) / 2m;

    public decimal Spread => Ask - Bid;
}
