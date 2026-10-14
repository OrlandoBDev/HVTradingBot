using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Learning;
using HVTradingBot.Domain.News;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Domain.Decisions;

public sealed record ScoredCandidate(StrategyResult Result, ScoreBreakdown Score, StrategyPerformance? Performance = null)
{
    /// <summary>News, calendar and cross-market trend view of this setup (<see cref="NewsAssessment.None"/> when unused).</summary>
    public NewsAssessment News { get; init; } = NewsAssessment.None;

    public bool DisabledByLearning => Performance?.Disabled == true;

    public TradeSetup Setup => Result.Setup!;
}

/// <summary>Signal-side outcome for one instrument before risk: NO_TRADE, OBSERVE or CANDIDATE.</summary>
public sealed record SignalEvaluation(
    MarketContext Context,
    DecisionState State,
    ScoredCandidate? Best,
    IReadOnlyList<StrategyResult> StrategyResults,
    IReadOnlyList<ScoredCandidate> ScoredCandidates,
    IReadOnlyList<string> Reasons);
