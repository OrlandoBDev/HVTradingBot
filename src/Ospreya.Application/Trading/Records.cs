using Ospreya.Domain.Analysis;
using Ospreya.Domain.Common;
using Ospreya.Domain.Execution;
using Ospreya.Domain.Risk;
using Ospreya.Domain.Scoring;
using Ospreya.Domain.Strategies;

namespace Ospreya.Application.Trading;

/// <summary>Everything about one evaluation, persisted whether the outcome was a trade, a rejection or NO_TRADE.</summary>
public sealed record DecisionRecord(
    Guid Id,
    string CorrelationId,
    string Instrument,
    DateTime MarketTimeUtc,
    DecisionState State,
    MarketRegime Regime,
    string? Strategy,
    Direction? Direction,
    int? Score,
    ScoreBreakdown? ScoreBreakdown,
    TradeSetup? Setup,
    IndicatorSnapshot PrimaryIndicators,
    IndicatorSnapshot StructuralIndicators,
    IReadOnlyList<StrategyResult> StrategyResults,
    RiskDecision? Risk,
    IReadOnlyList<string> Reasons,
    string? ClientOrderId,
    OrderResult? Order);

public sealed record MarketSnapshot(
    string Instrument,
    DateTime MarketTimeUtc,
    decimal Bid,
    decimal Ask,
    decimal SpreadPips,
    MarketRegime? Regime,
    IndicatorSnapshot? PrimaryIndicators,
    DecisionState? LastDecision,
    DateTime? LastDecisionTimeUtc);
