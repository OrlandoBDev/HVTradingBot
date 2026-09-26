using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.MarketData;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence.Stores;

public sealed class EfCandleStore(IDbContextFactory<TradingDbContext> dbFactory) : ICandleStore
{
    public async Task<IReadOnlyList<Candle>> GetM5Async(string symbol, DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Candles.AsNoTracking().Where(c => c.Instrument == symbol && c.TimeFrame == nameof(TimeFrame.M5));
        if (fromUtc is { } from)
        {
            query = query.Where(c => c.OpenTimeUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(c => c.OpenTimeUtc < to);
        }

        var newestFirst = await query.OrderByDescending(c => c.OpenTimeUtc).Take(limit).ToListAsync(cancellationToken);
        return newestFirst.AsEnumerable().Reverse().Select(SimulatedMarketDataFeed.ToCandle).ToList();
    }
}
