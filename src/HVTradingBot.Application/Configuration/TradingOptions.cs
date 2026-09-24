using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Configuration;

public sealed class TradingOptions
{
    public const string SectionName = "Trading";
    public TradingMode Mode { get; init; } = TradingMode.Paper;
}
