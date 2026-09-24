namespace HVTradingBot.Domain.Trading;

public sealed record TradeProposal(
    Guid Id,
    string Instrument,
    TradeDirection Direction,
    string Strategy,
    decimal SuggestedEntry,
    decimal StopLoss,
    decimal TakeProfit,
    decimal Score,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
