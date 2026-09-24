using System.ComponentModel.DataAnnotations;

namespace HVTradingBot.Infrastructure.MarketData;

public sealed class SimulatedMarketOptions
{
    public const string SectionName = "MarketData:Simulated";

    /// <summary>Random seed; the same seed and start time always produce the same market.</summary>
    public int Seed { get; set; } = 20260924;

    /// <summary>Wall-clock delay between simulated 5m bars. 2000 ms replays one trading day in about 10 minutes.</summary>
    [Range(0, 300_000)] public int BarIntervalMilliseconds { get; set; } = 2000;

    /// <summary>History generated on first start so indicators (incl. 4H and Daily) can warm up.</summary>
    [Range(10, 120)] public int WarmupDays { get; set; } = 45;
}
