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

/// <param name="IsOpen">False when no price has arrived for 10+ minutes (weekend, daily break, exchange hours).</param>
/// <param name="IsPaused">Derived market waiting because Forex is open ("Derived only while Forex is closed").</param>
public sealed record MarketDto(
    string Instrument,
    string DisplayName,
    string AssetClass,
    bool IsTradable,
    int PriceDecimals,
    bool IsOpen,
    bool IsPaused,
    bool IsLoading,
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
    decimal? MfePips,
    string? CloseStatus = null,
    string? CloseMessage = null,
    decimal? Commission = null);

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
    bool DerivedOnlyWhenForexClosed,
    bool IsDefaultSelection,
    int Version,
    int AppliedVersion,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    int MaxSelected,
    IReadOnlyList<MarketCatalogItemDto> Catalog);

public sealed record MarketSelectionRequest(IReadOnlyList<string> Instruments, bool DerivedOnlyWhenForexClosed = true);

public sealed record RiskLimitsDto(
    decimal MaxRiskPerTradePercent,
    decimal MaxDailyLossPercent,
    decimal MaxWeeklyLossPercent,
    int MaxOpenPositions,
    decimal MinRewardToRisk,
    int MaxConsecutiveLosses,
    int CooldownMinutes,
    int MaxCurrencyExposure,
    decimal MaxCommissionShareOfRisk,
    int? MaxDerivedOpenPositions = null,
    decimal? DerivedRiskPerTradePercent = null,
    decimal? MaxDerivedDailyLossPercent = null,
    decimal? AssumedCommissionPercent = null,
    int? MaxExtraDerivedPositions = null,
    int? HighScoreOverrideMinScore = null);

public sealed record RiskSettingsDto(
    RiskLimitsDto Effective,
    RiskLimitsDto Defaults,
    bool IsCustomized,
    int Version,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    decimal Balance,
    string Currency);

/// <summary>Email settings as shown on the Settings page. The SMTP password is never returned.</summary>
public sealed record NotificationSettingsDto(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    string? Username,
    bool PasswordConfigured,
    string? PasswordHint,
    string? FromAddress,
    string FromName,
    IReadOnlyList<string> ToAddresses,
    bool OnTradeOpened,
    bool OnTradeClosed,
    bool OnOrderRejected,
    bool OnKillSwitch,
    string Source,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    DateTime? LastAttemptUtc,
    bool? LastAttemptSucceeded,
    string? LastError);

/// <summary>Leave <see cref="Password"/> null or empty to keep the stored password.</summary>
public sealed record NotificationSettingsRequest(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string>? ToAddresses,
    bool OnTradeOpened,
    bool OnTradeClosed,
    bool OnOrderRejected,
    bool OnKillSwitch);

public sealed record TestEmailResult(bool Sent, string Message);

public sealed record TestTradeRequest(string Instrument);

public sealed record TestTradeDto(
    Guid Id,
    string Instrument,
    string Status,
    string? Message,
    string RequestedBy,
    DateTime RequestedAtUtc,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    string? ClientOrderId,
    decimal? FillPrice,
    decimal? ExitPrice,
    decimal? RealizedPnl,
    int HoldSeconds);

/// <summary>Mid-price OHLC bar; <see cref="Spread"/> is the bid/ask spread at the close.</summary>
public sealed record CandleDto(DateTime OpenTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Spread, long Volume);

/// <summary>Candles for one market and timeframe, oldest first.</summary>
public sealed record CandleSeriesDto(string Instrument, string TimeFrame, IReadOnlyList<CandleDto> Candles);

/// <summary>One page of a larger result. <see cref="Page"/> starts at 1.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Who is signed in. <c>SetupRequired</c> is true until the owner account has been created.</summary>
public sealed record AuthStatusDto(bool Authenticated, string? Username, bool SetupRequired);

public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);

/// <summary>Creates the owner account. The setup code is printed in the API log so only whoever runs the server can claim it.</summary>
public sealed record SetupRequest(string SetupCode, string Username, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
