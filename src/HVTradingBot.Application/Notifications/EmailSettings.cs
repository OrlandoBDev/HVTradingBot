namespace HVTradingBot.Application.Notifications;

/// <summary>What happened. Each kind can be switched on or off on the Settings page.</summary>
public enum NotificationKind
{
    TradeOpened,
    TradeClosed,
    OrderRejected,
    ApprovalRequired,
    KillSwitch,
    Test
}

/// <summary>Effective email settings (from the Settings page, or the Notifications:Email configuration as a fallback).</summary>
public sealed record EmailSettings(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    string? Username,
    string? Password,
    string? FromAddress,
    string FromName,
    IReadOnlyList<string> ToAddresses,
    bool OnTradeOpened,
    bool OnTradeClosed,
    bool OnOrderRejected,
    bool OnKillSwitch,
    string SubjectPrefix = "[HVTradingBot]",
    int MaxSendAttempts = 3)
{
    public static readonly EmailSettings Disabled = new(false, "smtp.gmail.com", 587, null, null, null, "HVTradingBot", [], true, true, false, true);

    public string? EffectiveFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) && ToAddresses.Count > 0;

    public bool ShouldSend(NotificationKind kind) => Enabled && kind switch
    {
        NotificationKind.TradeOpened => OnTradeOpened,
        NotificationKind.TradeClosed => OnTradeClosed,
        NotificationKind.OrderRejected => OnOrderRejected,
        NotificationKind.ApprovalRequired => OnOrderRejected,
        NotificationKind.KillSwitch => OnKillSwitch,
        NotificationKind.Test => true,
        _ => false
    };
}
