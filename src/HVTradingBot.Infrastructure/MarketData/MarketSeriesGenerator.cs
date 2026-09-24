using HVTradingBot.Domain.MarketData;

namespace HVTradingBot.Infrastructure.MarketData;

/// <summary>
/// Deterministic synthetic Forex price generator: regime-switching drift (up / down / flat), mean-reverting
/// volatility clustering, session-dependent activity, bid/ask spread with occasional spikes, and no weekend bars.
/// Intended for paper trading and development only; it has no predictive relationship with real markets.
/// </summary>
public sealed class MarketSeriesGenerator
{
    private const int BarsPerDay = 288;
    private const int SubSteps = 6;

    private readonly Instrument _instrument;
    private readonly Random _random;
    private readonly double _barSigma;
    private readonly double _baseSpreadPips;
    private double _price;
    private double _drift;
    private double _volatility = 1.0;
    private DateTime _nextOpenUtc;

    public MarketSeriesGenerator(Instrument instrument, int seed, decimal startPrice, DateTime nextOpenUtc)
    {
        _instrument = instrument;
        // HashCode.Combine is randomized per process, so combine manually to keep runs reproducible.
        _random = new Random(unchecked(seed * 397 ^ instrument.Symbol.GetDeterministicHash() * 31 ^ (int)(nextOpenUtc.Ticks / TimeSpan.TicksPerMinute)));
        var profile = Profiles.GetValueOrDefault(instrument.Symbol, (Price: 1.0, DailyVolPercent: 0.5, SpreadPips: 1.0));
        _barSigma = profile.DailyVolPercent / 100.0 / Math.Sqrt(BarsPerDay);
        _baseSpreadPips = profile.SpreadPips;
        _price = startPrice > 0 ? (double)startPrice : profile.Price;
        _nextOpenUtc = SkipWeekend(nextOpenUtc);
    }

    /// <summary>Only markets with a price profile can be simulated realistically.</summary>
    public static bool Supports(Instrument instrument) => Profiles.ContainsKey(instrument.Symbol);

    public static decimal DefaultPrice(Instrument instrument) =>
        (decimal)Profiles.GetValueOrDefault(instrument.Symbol, (Price: 1.0, DailyVolPercent: 0.5, SpreadPips: 1.0)).Price;

    private static readonly Dictionary<string, (double Price, double DailyVolPercent, double SpreadPips)> Profiles = new()
    {
        ["EUR/USD"] = (1.0850, 0.45, 0.8),
        ["GBP/USD"] = (1.2700, 0.55, 1.2),
        ["USD/JPY"] = (150.00, 0.60, 1.0),
        ["AUD/USD"] = (0.6600, 0.65, 1.0),
        ["USD/CAD"] = (1.3600, 0.40, 1.4)
    };

    public DateTime NextOpenUtc => _nextOpenUtc;

    public Candle Next()
    {
        var open = _price;
        var high = open;
        var low = open;
        var session = SessionActivity(_nextOpenUtc);

        UpdateRegime();
        _volatility = Math.Clamp(_volatility * Math.Exp(0.08 * Gaussian()) + 0.02 * (1.0 - _volatility), 0.4, 3.5);
        var sigma = _barSigma * _volatility * session / Math.Sqrt(SubSteps);

        for (var i = 0; i < SubSteps; i++)
        {
            var shock = Gaussian();
            if (_random.NextDouble() < 0.002)
            {
                shock *= 4; // occasional jump
            }

            _price *= Math.Exp(_drift / SubSteps + sigma * shock);
            high = Math.Max(high, _price);
            low = Math.Min(low, _price);
        }

        var spreadPips = _baseSpreadPips * (0.85 + 0.3 * _random.NextDouble()) * (0.7 + 0.3 * _volatility) / Math.Sqrt(session);
        if (_random.NextDouble() < 0.004)
        {
            spreadPips *= 3 + 3 * _random.NextDouble(); // liquidity gap
        }

        var candle = new Candle(
            _nextOpenUtc,
            TimeFrame.M5,
            Round(open),
            Round(high),
            Round(low),
            Round(_price),
            _instrument.RoundPrice(_instrument.FromPips((decimal)Math.Round(spreadPips, 1))),
            (long)(200 * session * _volatility * (0.5 + _random.NextDouble())));

        _nextOpenUtc = SkipWeekend(_nextOpenUtc.AddMinutes(5));
        return candle;
    }

    private void UpdateRegime()
    {
        // Average regime length ~3 trading days.
        if (_random.NextDouble() >= 1.0 / (BarsPerDay * 3))
        {
            return;
        }

        var roll = _random.NextDouble();
        _drift = roll switch
        {
            < 0.35 => _barSigma * 0.035,
            < 0.70 => -_barSigma * 0.035,
            _ => 0
        };
    }

    /// <summary>Relative activity by UTC hour: quiet Asia, busier London and New York overlap.</summary>
    private static double SessionActivity(DateTime utc) => utc.Hour switch
    {
        >= 7 and < 12 => 1.25,
        >= 12 and < 16 => 1.4,
        >= 16 and < 21 => 1.0,
        _ => 0.7
    };

    private static DateTime SkipWeekend(DateTime utc) => utc.DayOfWeek switch
    {
        DayOfWeek.Saturday => utc.Date.AddDays(2),
        DayOfWeek.Sunday => utc.Date.AddDays(1),
        _ => utc
    };

    private decimal Round(double price) => _instrument.RoundPrice((decimal)price);

    private double Gaussian()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>Generates <paramref name="days"/> calendar days of 5m bars ending before <paramref name="endUtc"/>.</summary>
    public static IReadOnlyList<Candle> Generate(Instrument instrument, int seed, DateTime endUtc, int days, decimal? startPrice = null)
    {
        var start = TimeFrame.D1.BarStart(endUtc).AddDays(-days);
        var generator = new MarketSeriesGenerator(instrument, seed, startPrice ?? DefaultPrice(instrument), start);
        var bars = new List<Candle>(days * BarsPerDay);
        while (generator.NextOpenUtc.AddMinutes(5) <= endUtc)
        {
            bars.Add(generator.Next());
        }

        return bars;
    }
}

internal static class StringHashExtensions
{
    /// <summary>string.GetHashCode is randomized per process; this one is stable so seeds are reproducible.</summary>
    public static int GetDeterministicHash(this string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return hash;
        }
    }
}
