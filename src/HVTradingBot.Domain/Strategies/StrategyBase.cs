using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Domain.Strategies;

public abstract class StrategyBase : ITradingStrategy
{
    public abstract string Name { get; }

    public abstract IReadOnlyCollection<MarketRegime> CompatibleRegimes { get; }

    public Task<StrategyResult> EvaluateAsync(MarketContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsDataStale)
        {
            return Task.FromResult(NoTrade("Market data is stale."));
        }

        if (!CompatibleRegimes.Contains(context.Regime))
        {
            return Task.FromResult(NoTrade($"Regime {context.Regime} is not compatible."));
        }

        if (context.Primary.Atr is not { } atr || atr <= 0)
        {
            return Task.FromResult(NoTrade("ATR not available (insufficient history)."));
        }

        return Task.FromResult(Evaluate(context, atr));
    }

    protected abstract StrategyResult Evaluate(MarketContext context, decimal atr);

    protected StrategyResult NoTrade(string reason) => StrategyResult.NoTrade(Name, reason, CompatibleRegimes);

    /// <summary>Stops closer than this fraction of the 1H ATR are inside normal noise and are not traded.</summary>
    public const decimal MinStopAtrFraction = 0.5m;

    /// <summary>Builds a setup from the current executable price (ask for longs, bid for shorts).</summary>
    protected StrategyResult Trade(MarketContext context, Direction direction, decimal stopDistance, decimal targetDistance, string reason)
    {
        // With 5-minute evaluation, price may have moved towards a structural stop since the setup formed.
        if (context.Primary.Atr is { } atr && stopDistance < atr * MinStopAtrFraction)
        {
            return NoTrade($"Stop {stopDistance:0.#####} is inside {MinStopAtrFraction:0.#} ATR; price has moved too close to the invalidation level.");
        }

        var instrument = context.Instrument;
        var entry = direction == Direction.Long ? context.Quote.Ask : context.Quote.Bid;
        var sign = direction.Sign();
        var setup = new TradeSetup(
            direction,
            entry,
            instrument.RoundPrice(entry - sign * stopDistance),
            instrument.RoundPrice(entry + sign * targetDistance));

        return setup.IsValid
            ? StrategyResult.Trade(Name, setup, reason, CompatibleRegimes)
            : NoTrade("Computed stop/target is invalid for the current price.");
    }

    protected static Direction? RegimeDirection(MarketRegime regime) => regime switch
    {
        MarketRegime.TrendingBullish => Direction.Long,
        MarketRegime.TrendingBearish => Direction.Short,
        _ => null
    };
}
