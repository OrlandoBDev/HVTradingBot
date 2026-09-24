using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Application.Abstractions;

/// <summary>Real (wall-clock) time. Market time comes from bars.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public interface ITradingStateStore
{
    Task<TradingSystemState> GetAsync(CancellationToken cancellationToken);

    /// <summary>Applies <paramref name="update"/> atomically (optimistic concurrency with retry) and returns the saved state.</summary>
    Task<TradingSystemState> UpdateAsync(Func<TradingSystemState, TradingSystemState> update, CancellationToken cancellationToken);
}

public interface IDecisionJournal
{
    Task RecordDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken);

    Task RecordAuditAsync(string actor, string action, string details, string? correlationId, CancellationToken cancellationToken);
}

public interface IMarketSnapshotSink
{
    Task PublishAsync(MarketSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Updates live bid/ask between bars (display only; trading decisions use closed bars).</summary>
    Task PublishQuotesAsync(IReadOnlyCollection<Quote> quotes, CancellationToken cancellationToken);
}

public interface IMarketDataFeed
{
    MarketDataStatus Status { get; }

    /// <summary>Most recent live quote per instrument (from the tick stream where available).</summary>
    IReadOnlyCollection<Quote> LatestQuotes { get; }

    /// <summary>Recent closed 5m bars used to warm up indicators, oldest first.</summary>
    Task<IReadOnlyDictionary<Instrument, IReadOnlyList<Candle>>> LoadHistoryAsync(CancellationToken cancellationToken);

    /// <summary>Waits for and returns the next closed 5m bar for each instrument.</summary>
    Task<IReadOnlyList<InstrumentBar>> NextBarsAsync(CancellationToken cancellationToken);
}

public sealed record InstrumentBar(Instrument Instrument, Candle Bar);
