using HVTradingBot.Domain.Analysis;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;
using HVTradingBot.Domain.Risk;
using HVTradingBot.Domain.Scoring;
using HVTradingBot.Domain.Strategies;

namespace HVTradingBot.Application.Trading;

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
