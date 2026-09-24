namespace HVTradingBot.Infrastructure.Persistence.Entities;

public sealed class TradeDecisionEntity
{
    public Guid Id { get; set; }
    public required string Instrument { get; set; }
    public required string Strategy { get; set; }
    public required string Status { get; set; }
    public decimal? Score { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
