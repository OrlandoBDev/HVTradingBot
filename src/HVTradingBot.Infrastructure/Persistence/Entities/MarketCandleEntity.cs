namespace HVTradingBot.Infrastructure.Persistence.Entities;

public sealed class MarketCandleEntity
{
    public Guid Id { get; set; }
    public required string Instrument { get; set; }
    public required string TimeFrame { get; set; }
    public DateTimeOffset OpenTimeUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
}
