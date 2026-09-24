using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.UnitTests.TestData;

public static class Bars
{
    public static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc); // a Monday

    public static Candle Bar(DateTime open, decimal close, TimeFrame tf = TimeFrame.M5, decimal range = 0.0010m, decimal spread = 0.00008m, decimal? openPrice = null) =>
        new(open, tf, openPrice ?? close, Math.Max(openPrice ?? close, close) + range / 2, Math.Min(openPrice ?? close, close) - range / 2, close, spread, 100);

    /// <summary>Linear series of closes on <paramref name="tf"/>.</summary>
    public static List<Candle> Trend(int count, decimal start, decimal step, TimeFrame tf = TimeFrame.H1, decimal range = 0.0010m)
    {
        var bars = new List<Candle>();
        var price = start;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            price += step;
            bars.Add(Bar(Start + tf.Duration() * i, price, tf, range, openPrice: open));
        }

        return bars;
    }

    public static List<Candle> Flat(int count, decimal price, TimeFrame tf = TimeFrame.M5) =>
        Enumerable.Range(0, count).Select(i => Bar(Start + tf.Duration() * i, price, tf)).ToList();

    public static Quote Quote(Instrument instrument, decimal mid, decimal spread = 0.00008m, DateTime? at = null) =>
        new(instrument, at ?? Start, mid - spread / 2, mid + spread / 2);

    public static CurrencyConverter Converter(decimal usdJpy = 150m, decimal usdCad = 1.36m) =>
        new("USD", new Dictionary<string, decimal> { ["EUR/USD"] = 1.08m, ["USD/JPY"] = usdJpy, ["USD/CAD"] = usdCad });

    public static PortfolioState Portfolio(
        decimal balance = 100_000m,
        IReadOnlyList<OpenPosition>? positions = null,
        bool killSwitch = false,
        decimal dailyPnl = 0,
        decimal weeklyPnl = 0,
        DateTime? cooldownUntil = null,
        DateTime? lastDataReceived = null,
        TradingMode mode = TradingMode.Paper)
    {
        var now = Start;
        return new PortfolioState(mode, balance, balance, dailyPnl, weeklyPnl, positions ?? [], 0, cooldownUntil, killSwitch,
            killSwitch ? "test" : null, new MarketDataStatus(now, lastDataReceived ?? now), now, now, Converter());
    }

    public static TradeProposal Proposal(
        Instrument? instrument = null,
        Direction direction = Direction.Long,
        decimal entry = 1.10000m,
        decimal stopPips = 20,
        decimal rr = 2.5m,
        decimal spread = 0.00008m,
        decimal averageSpread = 0.00008m,
        string clientOrderId = "EURUSD-Test-L-1")
    {
        instrument ??= Instruments.EurUsd;
        var sign = direction.Sign();
        var stop = entry - sign * instrument.FromPips(stopPips);
        var target = entry + sign * instrument.FromPips(stopPips * rr);
        return new TradeProposal(instrument, new TradeSetup(direction, entry, stop, target), Quote(instrument, entry, spread),
            averageSpread, "Test", 80, clientOrderId);
    }

    public static OpenPosition Position(Instrument instrument, Direction direction, decimal entry = 1.1m, decimal stop = 1.098m,
        decimal target = 1.105m, decimal units = 100_000m, string clientOrderId = "pos-1") =>
        new(Guid.NewGuid(), clientOrderId, instrument, direction, units, entry, stop, target, 200m, Start, "Test", 80, 0, 0);

    public static IndicatorSnapshot Snapshot(TimeFrame tf = TimeFrame.H1, decimal close = 1.1m, decimal? adx = 30, decimal? ema20 = 1.099m,
        decimal? ema50 = 1.098m, decimal? atrRank = 0.5m) =>
        new(tf, 300, Start, close, close, ema20, ema50, 1.09m, 55, 0.0001m, 0.00005m, [0.00001m, 0.00002m, 0.00003m], 0.0012m, atrRank, adx,
            close + 0.002m, close, close - 0.002m, [0.004m, 0.004m], 0.5m, 0.1m, 0.001m);
}
