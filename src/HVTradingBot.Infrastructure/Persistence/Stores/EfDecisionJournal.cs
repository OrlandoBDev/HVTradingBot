using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Trading;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Persistence.Stores;

/// <summary>Persists every decision (including NO_TRADE and rejections), audit events and market snapshots.</summary>
public sealed class EfDecisionJournal(IDbContextFactory<TradingDbContext> dbFactory, IClock clock) : IDecisionJournal, IMarketSnapshotSink
{
    public async Task RecordDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.TradeDecisions.Add(new TradeDecisionEntity
        {
            Id = decision.Id,
            CorrelationId = decision.CorrelationId,
            Instrument = decision.Instrument,
            MarketTimeUtc = decision.MarketTimeUtc,
            RecordedAtUtc = clock.UtcNow,
            State = decision.State.ToString(),
            Regime = decision.Regime.ToString(),
            Strategy = decision.Strategy,
            Direction = decision.Direction?.ToString(),
            Score = decision.Score,
            Entry = decision.Setup?.Entry,
            StopLoss = decision.Setup?.StopLoss,
            TakeProfit = decision.Setup?.TakeProfit,
            RewardToRisk = decision.Setup is { } s ? Math.Round(s.RewardToRisk, 2) : null,
            ClientOrderId = decision.ClientOrderId,
            Reasons = Truncate(string.Join(" | ", decision.Reasons), 4000),
            Details = JsonSerializer.Serialize(new
            {
                decision.ScoreBreakdown,
                decision.PrimaryIndicators,
                decision.StructuralIndicators,
                decision.StrategyResults,
                decision.Risk,
                decision.Order
            }, JsonDefaults.Options)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAuditAsync(string actor, string action, string details, string? correlationId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.AuditLogs.Add(new AuditLogEntity
        {
            TimestampUtc = clock.UtcNow,
            Actor = actor,
            Action = action,
            Details = Truncate(details, 4000),
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishAsync(MarketSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MarketSnapshots.SingleOrDefaultAsync(s => s.Instrument == snapshot.Instrument, cancellationToken);
        if (entity is null)
        {
            entity = new MarketSnapshotEntity { Instrument = snapshot.Instrument };
            db.MarketSnapshots.Add(entity);
        }

        entity.MarketTimeUtc = snapshot.MarketTimeUtc;
        entity.Bid = snapshot.Bid;
        entity.Ask = snapshot.Ask;
        entity.SpreadPips = snapshot.SpreadPips;
        entity.Regime = snapshot.Regime?.ToString();
        entity.LastDecision = snapshot.LastDecision?.ToString();
        entity.LastDecisionTimeUtc = snapshot.LastDecisionTimeUtc;
        entity.Indicators = snapshot.PrimaryIndicators is null ? null : JsonSerializer.Serialize(snapshot.PrimaryIndicators, JsonDefaults.Options);
        entity.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishQuotesAsync(IReadOnlyCollection<Domain.MarketData.Quote> quotes, CancellationToken cancellationToken)
    {
        if (quotes.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var symbols = quotes.Select(q => q.Instrument.Symbol).ToList();
        var rows = await db.MarketSnapshots.Where(s => symbols.Contains(s.Instrument)).ToDictionaryAsync(s => s.Instrument, cancellationToken);
        foreach (var quote in quotes)
        {
            if (rows.TryGetValue(quote.Instrument.Symbol, out var row) && quote.TimestampUtc >= row.MarketTimeUtc)
            {
                row.Bid = quote.Bid;
                row.Ask = quote.Ask;
                row.SpreadPips = Math.Round(quote.Instrument.ToPips(quote.Spread), 2);
                row.MarketTimeUtc = quote.TimestampUtc;
                row.UpdatedAtUtc = clock.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
