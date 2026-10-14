using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Risk;

namespace HVTradingBot.Application.Signals;

public static class SignalKinds
{
    /// <summary>A setup on a market set to "signals only": the bot would have traded it.</summary>
    public const string SignalsOnlyMarket = "SignalsOnlyMarket";

    /// <summary>A setup that scored just below the automatic threshold.</summary>
    public const string NearMiss = "NearMiss";
}

/// <summary>A signal the engine found, before the user decides.</summary>
/// <param name="SetupId">The setup's key (same as the learning system's virtual trade), unique per setup and hour.</param>
/// <param name="Preview">The risk check as it would run now with no accepted overrides, shown with the signal.</param>
public sealed record NewSignal(
    Guid Id,
    string SetupId,
    string Kind,
    string Instrument,
    Direction Direction,
    string Strategy,
    int Score,
    string? Regime,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    Guid DecisionId,
    RiskDecision Preview)
{
    public string ClientOrderId => SignalOrders.Prefix + SetupId;
}

/// <summary>Where the engine records signals (the dashboard shows them; the worker places accepted ones).</summary>
public interface ISignalStore
{
    /// <summary>Adds the signal unless one for the same setup exists. Returns false for a duplicate.</summary>
    Task<bool> AddAsync(NewSignal signal, CancellationToken cancellationToken);
}

/// <summary>A signal the user accepted, to be placed by the engine.</summary>
public sealed record SignalRequest(
    Guid Id,
    string ClientOrderId,
    string Instrument,
    Direction Direction,
    string Strategy,
    int Score,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    IReadOnlyList<string> AcceptedRules,
    string RequestedBy);

public enum SignalOutcomeStatus
{
    Placed,

    /// <summary>Some rules failed that the user may accept; nothing was sent.</summary>
    NeedsReview,

    Failed
}

public sealed record SignalOutcome(
    SignalOutcomeStatus Status,
    string Message,
    IReadOnlyList<RiskCheck> Checks,
    decimal? RiskAmount = null,
    Guid? PositionId = null,
    decimal? FillPrice = null,
    decimal? Entry = null)
{
    public IReadOnlyList<string> OverriddenRules => Checks.Where(c => c.Overridden).Select(c => c.Rule).ToList();
}
