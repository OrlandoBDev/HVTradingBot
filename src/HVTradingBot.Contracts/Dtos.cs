using System.Text.Json;

namespace HVTradingBot.Contracts;

public sealed record AccountDto(string Currency, decimal Balance, decimal StartingBalance, decimal Equity, decimal UnrealizedPnl);

public sealed record MarketDataStatusDto(DateTime? LastBarTimeUtc, DateTime? LastDataReceivedUtc, bool IsStale);

public sealed record BrokerDto(string Name, string? AccountId, bool IsDemo);

public sealed record SystemStatusDto(
    string Mode,
    BrokerDto Broker,
    bool KillSwitchActive,
    string? KillSwitchReason,
    DateTime? KillSwitchChangedUtc,
    DateTime? WorkerHeartbeatUtc,
    bool WorkerHealthy,
    MarketDataStatusDto MarketData,
    AccountDto Account,
    int OpenPositions,
    decimal DailyRealizedPnl,
    decimal WeeklyRealizedPnl,
    int ConsecutiveLosses,
    DateTime? CooldownUntilUtc,
    DateTime ServerTimeUtc);

public sealed record MarketDto(
    string Instrument,
    string DisplayName,
    string AssetClass,
    bool IsTradable,
    int PriceDecimals,
    DateTime MarketTimeUtc,
    decimal Bid,
    decimal Ask,
    decimal SpreadPips,
    string? Regime,
    string? LastDecision,
    DateTime? LastDecisionTimeUtc,
    JsonElement? Indicators);

public sealed record DecisionDto(
    Guid Id,
    DateTime MarketTimeUtc,
    string Instrument,
    string State,
    string Regime,
    string? Strategy,
    string? Direction,
    int? Score,
    decimal? Entry,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? RewardToRisk,
    string Reasons,
    string? ClientOrderId,
    string CorrelationId);

public sealed record DecisionDetailDto(DecisionDto Decision, JsonElement Details);

public sealed record PositionDto(
    Guid Id,
    string ClientOrderId,
    string Instrument,
    string Direction,
    decimal Units,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    decimal InitialRiskAmount,
    DateTime OpenedAtUtc,
    string Strategy,
    int Score,
    decimal? CurrentPrice,
    decimal? UnrealizedPnl,
    DateTime? ClosedAtUtc,
    decimal? ExitPrice,
    string? ExitReason,
    decimal? RealizedPnl,
    decimal? RMultiple,
    decimal? MaePips,
    decimal? MfePips);

public sealed record RiskLimitStatusDto(string Name, string Current, string Limit, bool Breached);

public sealed record RiskStatusDto(
    bool NewTradesAllowed,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<RiskLimitStatusDto> Limits,
    IReadOnlyDictionary<string, int> CurrencyExposure);

public sealed record KillSwitchRequest(bool Active, string? Reason);

public sealed record AuditEntryDto(long Id, DateTime TimestampUtc, string Actor, string Action, string Details, string? CorrelationId);

/// <summary>Source "simulated" generates reproducible synthetic data from <see cref="Seed"/>; "stored" replays candles in the database.</summary>
public sealed record BacktestRequest(
    string Source = "simulated",
    int Days = 90,
    int? Seed = null,
    DateOnly? EndDate = null,
    IReadOnlyList<string>? Instruments = null);

public sealed record BacktestRunDto(Guid Id, DateTime CreatedAtUtc, string Source, int TotalTrades, decimal NetPnl, JsonElement Parameters, JsonElement Summary);

/// <summary>Saves Deriv settings. Leave <see cref="ApiToken"/> null or empty to keep the stored token.</summary>
public sealed record DerivSettingsRequest(string AppId, string? ApiToken, string? AccountId);

public sealed record DerivAccountDto(string AccountId, string AccountType, string Currency);

public sealed record DerivConnectionDto(
    string State,
    string? Message,
    bool IsCurrent,
    string? ConnectedAccountId,
    IReadOnlyList<DerivAccountDto> Accounts,
    DateTime CheckedAtUtc);

/// <summary>Deriv settings as shown on the Settings page. The token itself is never returned.</summary>
public sealed record DerivSettingsDto(
    string BrokerProvider,
    string AccountType,
    string? AppId,
    bool TokenConfigured,
    string? TokenHint,
    string? AccountId,
    string Source,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    DerivConnectionDto? Connection);

public sealed record MarketCatalogItemDto(
    string Symbol,
    string BrokerSymbol,
    string Name,
    string Market,
    string Submarket,
    string AssetClass,
    bool IsTradable,
    bool IsOpen,
    IReadOnlyList<int> Multipliers);

public sealed record MarketSettingsDto(
    IReadOnlyList<string> Selected,
    bool IsDefaultSelection,
    int Version,
    int AppliedVersion,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    int MaxSelected,
    IReadOnlyList<MarketCatalogItemDto> Catalog);

public sealed record MarketSelectionRequest(IReadOnlyList<string> Instruments);
