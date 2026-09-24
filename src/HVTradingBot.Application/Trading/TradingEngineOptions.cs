using System.ComponentModel.DataAnnotations;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Application.Trading;

public sealed class TradingEngineOptions
{
    public const string SectionName = "Trading";

    /// <summary>PAPER is the default. APPROVAL and AUTO are not implemented in the MVP and are rejected by validation.</summary>
    public TradingMode Mode { get; set; } = TradingMode.Paper;

    /// <summary>
    /// Traded instruments; set in hvtradingbot.shared.json. No default here: the configuration binder appends
    /// to an initialized list instead of replacing it, which would duplicate every entry.
    /// </summary>
    [Required, MinLength(1)]
    public List<string> Instruments { get; set; } = [];

    [Required] public string AccountCurrency { get; set; } = "USD";

    [Range(100, 100_000_000)] public decimal StartingBalance { get; set; } = 100_000m;

    /// <summary>Bars required before strategies are evaluated (indicator warm-up).</summary>
    [Range(30, 400)] public int MinPrimaryBars { get; set; } = 120;

    [Range(20, 400)] public int MinStructuralBars { get; set; } = 60;

    /// <summary>
    /// How often strategies are evaluated. M5 (default) re-checks every market at each 5-minute close so an entry is not
    /// missed within the hour; setups are still built on closed 1H/4H/Daily bars and traded at most once per signal bar.
    /// </summary>
    public Domain.MarketData.TimeFrame EvaluationTimeFrame { get; set; } = Domain.MarketData.TimeFrame.M5;
}
