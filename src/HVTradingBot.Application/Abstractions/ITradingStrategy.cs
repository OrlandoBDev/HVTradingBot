using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Abstractions;

public interface ITradingStrategy
{
    string Name { get; }

    Task<StrategyResult> EvaluateAsync(
        MarketContext context,
        CancellationToken cancellationToken);
}

public sealed record MarketContext(
    string Instrument,
    IReadOnlyDictionary<TimeFrame, IReadOnlyCollection<Candle>> Candles,
    DateTimeOffset EvaluatedAtUtc);

public sealed record StrategyResult(
    bool HasTrade,
    TradeProposal? Proposal,
    IReadOnlyCollection<string> Reasons);
