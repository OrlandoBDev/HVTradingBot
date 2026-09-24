namespace HVTradingBot.Domain.Scoring;

/// <summary>Score policy thresholds (0-100). Kept configurable per docs/MVP.md.</summary>
public sealed class ScoringOptions
{
    /// <summary>Scores below this are rejected (NO_TRADE).</summary>
    public int ObserveThreshold { get; set; } = 60;

    /// <summary>Scores at or above this become CANDIDATE; between Observe and this they are OBSERVE only.</summary>
    public int CandidateThreshold { get; set; } = 75;

    /// <summary>Scores at or above this are labelled high quality.</summary>
    public int HighQualityThreshold { get; set; } = 85;

    /// <summary>Spread above this multiple of the recent average is abnormal and forces abstention.</summary>
    public decimal AbnormalSpreadMultiple { get; set; } = 2.5m;
}
