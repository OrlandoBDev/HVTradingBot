namespace HVTradingBot.Application.Trading;

public sealed record TestTradeOutcome(bool Filled, string Message, string? ClientOrderId = null, Guid? PositionId = null, decimal? FillPrice = null)
{
    public static TestTradeOutcome Failed(string message, string? clientOrderId = null) => new(false, message, clientOrderId);
}
