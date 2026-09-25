namespace HVTradingBot.Infrastructure.Persistence.Entities;

public sealed class CandleEntity
{
    public required string Instrument { get; set; }
    public required string TimeFrame { get; set; }
    public DateTime OpenTimeUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Spread { get; set; }
    public long Volume { get; set; }
}

public sealed class TradeDecisionEntity
{
    public Guid Id { get; set; }
    public required string CorrelationId { get; set; }
    public required string Instrument { get; set; }
    public DateTime MarketTimeUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public required string State { get; set; }
    public required string Regime { get; set; }
    public string? Strategy { get; set; }
    public string? Direction { get; set; }
    public int? Score { get; set; }
    public decimal? Entry { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? RewardToRisk { get; set; }
    public string? ClientOrderId { get; set; }
    public required string Reasons { get; set; }

    /// <summary>JSON: score breakdown, indicators, every strategy result, risk checks and order result.</summary>
    public required string Details { get; set; }
}

public sealed class OrderEntity
{
    public Guid Id { get; set; }
    public required string ClientOrderId { get; set; }
    public string Broker { get; set; } = "Paper";
    public string? BrokerAccountId { get; set; }
    public string? BrokerContractId { get; set; }
    public Guid? DecisionId { get; set; }
    public required string CorrelationId { get; set; }
    public required string Instrument { get; set; }
    public required string Direction { get; set; }
    public decimal Units { get; set; }
    public decimal RequestedPrice { get; set; }
    public decimal? FillPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public required string Status { get; set; }
    public string? RejectReason { get; set; }
    public DateTime MarketTimeUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class PositionEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string ClientOrderId { get; set; }
    public string Broker { get; set; } = "Paper";
    public string? BrokerAccountId { get; set; }
    public string? BrokerContractId { get; set; }
    public decimal? BrokerStake { get; set; }
    public int? BrokerMultiplier { get; set; }
    public required string Instrument { get; set; }
    public required string Direction { get; set; }
    public decimal Units { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal InitialRiskAmount { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public required string Strategy { get; set; }
    public int Score { get; set; }
    public bool IsOpen { get; set; }
    public decimal MaxFavorableExcursion { get; set; }
    public decimal MaxAdverseExcursion { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal? ExitPrice { get; set; }
    public string? ExitReason { get; set; }
    public decimal? RealizedPnl { get; set; }
    public decimal? RMultiple { get; set; }
    public decimal? MaePips { get; set; }
    public decimal? MfePips { get; set; }
}

public sealed class PaperAccountEntity
{
    public int Id { get; set; }
    public required string Currency { get; set; }
    public decimal Balance { get; set; }
    public decimal StartingBalance { get; set; }
    public uint Version { get; set; }
}

/// <summary>Balance baseline per external broker account (e.g. a Deriv demo account).</summary>
public sealed class BrokerAccountEntity
{
    public required string AccountKey { get; set; }
    public required string Broker { get; set; }
    public bool IsDemo { get; set; }
    public required string Currency { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal LastBalance { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SystemStateEntity
{
    public int Id { get; set; }
    public required string Mode { get; set; }
    public bool KillSwitchActive { get; set; }
    public string? KillSwitchReason { get; set; }
    public DateTime? KillSwitchChangedUtc { get; set; }
    public int ConsecutiveLosses { get; set; }
    public DateTime? CooldownUntilUtc { get; set; }
    public DateOnly? PnlDay { get; set; }
    public decimal DailyRealizedPnl { get; set; }
    public decimal DerivedDailyRealizedPnl { get; set; }
    public DateOnly? PnlWeekStart { get; set; }
    public decimal WeeklyRealizedPnl { get; set; }
    public DateTime? LastBarTimeUtc { get; set; }
    public DateTime? LastDataReceivedUtc { get; set; }
    public DateTime? WorkerHeartbeatUtc { get; set; }
    public string? BrokerName { get; set; }
    public string? BrokerAccountId { get; set; }
    public bool BrokerIsDemo { get; set; }
    public string? MarketDataSource { get; set; }
    public uint Version { get; set; }
}

public sealed class MarketSnapshotEntity
{
    public required string Instrument { get; set; }
    public DateTime MarketTimeUtc { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal SpreadPips { get; set; }
    public string? Regime { get; set; }
    public string? LastDecision { get; set; }
    public DateTime? LastDecisionTimeUtc { get; set; }
    public string? Indicators { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public required string Details { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class BacktestRunEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public required string Source { get; set; }
    public required string Parameters { get; set; }
    public required string Summary { get; set; }
    public int TotalTrades { get; set; }
    public decimal NetPnl { get; set; }
}

/// <summary>Deriv account settings entered in the dashboard. The API token is stored encrypted (ASP.NET Data Protection).</summary>
public sealed class BrokerSettingsEntity
{
    public int Id { get; set; }
    public string? DerivAppId { get; set; }
    public string? DerivApiTokenProtected { get; set; }
    public string? DerivApiTokenHint { get; set; }
    public string? DerivAccountId { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Result of the worker's latest attempt to connect with the stored settings.</summary>
public sealed class BrokerConnectionStatusEntity
{
    public int Id { get; set; }
    public required string Status { get; set; }
    public string? Message { get; set; }
    public int SettingsVersion { get; set; }
    public string? ConnectedAccountId { get; set; }
    public string? AccountsJson { get; set; }
    public DateTime CheckedAtUtc { get; set; }
}

/// <summary>A market offered by the broker (catalog discovered from Deriv's public API).</summary>
public sealed class MarketEntity
{
    public required string BrokerSymbol { get; set; }
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public required string Market { get; set; }
    public required string Submarket { get; set; }
    public required string AssetClass { get; set; }
    public required string BaseCurrency { get; set; }
    public required string QuoteCurrency { get; set; }
    public decimal PipSize { get; set; }
    public int PriceDecimals { get; set; }
    public required string Multipliers { get; set; }
    public bool IsTradable { get; set; }
    public bool IsOpen { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Markets chosen on the Settings page. <see cref="AppliedVersion"/> is written by the worker once it runs with them.</summary>
public sealed class MarketSelectionEntity
{
    public int Id { get; set; }
    public required string Instruments { get; set; }
    public bool DerivedOnlyWhenForexClosed { get; set; } = true;
    public int Version { get; set; }
    public int AppliedVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A setup followed as a virtual trade for learning (traded or not).</summary>
public sealed class SetupOutcomeEntity
{
    public Guid Id { get; set; }
    public required string SetupId { get; set; }
    public Guid? DecisionId { get; set; }
    public required string Instrument { get; set; }
    public required string AssetClass { get; set; }
    public required string Strategy { get; set; }
    public required string Regime { get; set; }
    public required string Direction { get; set; }
    public required string DecisionState { get; set; }
    public int Score { get; set; }
    public decimal Entry { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public required string Status { get; set; }
    public decimal? RMultiple { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Risk limits changed on the Settings page (JSON of <c>RiskLimits</c>), applied over the configured defaults.</summary>
public sealed class RiskSettingsEntity
{
    public int Id { get; set; }
    public required string Limits { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Email notification settings from the Settings page. The SMTP password is encrypted (Data Protection).</summary>
public sealed class NotificationSettingsEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public required string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public string? PasswordHint { get; set; }
    public string? FromAddress { get; set; }
    public required string FromName { get; set; }
    public required string ToAddresses { get; set; }
    public bool OnTradeOpened { get; set; }
    public bool OnTradeClosed { get; set; }
    public bool OnOrderRejected { get; set; }
    public bool OnKillSwitch { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public bool? LastAttemptSucceeded { get; set; }
    public string? LastError { get; set; }
}

/// <summary>A test trade requested from the dashboard and carried out by the worker.</summary>
public sealed class TestTradeEntity
{
    public Guid Id { get; set; }
    public required string Instrument { get; set; }
    public required string Status { get; set; }
    public string? Message { get; set; }
    public required string RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? CloseRequestedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClientOrderId { get; set; }
    public Guid? PositionId { get; set; }
    public decimal? FillPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? RealizedPnl { get; set; }
    public int HoldSeconds { get; set; }
}

/// <summary>A request from the dashboard to close a position before its stop or target; carried out by the worker.</summary>
public sealed class CloseRequestEntity
{
    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public required string Instrument { get; set; }
    public required string Status { get; set; }
    public string? Message { get; set; }
    public required string RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? RealizedPnl { get; set; }
}
