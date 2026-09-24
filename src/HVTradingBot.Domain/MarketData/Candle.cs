namespace HVTradingBot.Domain.MarketData;

public sealed record Candle(
    string Instrument,
    TimeFrame TimeFrame,
    DateTimeOffset OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? Volume,
    decimal? Bid,
    decimal? Ask)
{
    public decimal? Spread => Bid is not null && Ask is not null ? Ask - Bid : null;
}
