using HVTradingBot.Application.Notifications;
using HVTradingBot.Domain.Common;

namespace HVTradingBot.Application.Configuration;

/// <summary>
/// Email settings from configuration / environment (Notifications__Email__*). Used only when nothing has been saved
/// on the Settings page, which stores its own values in the database.
/// </summary>
public sealed class EmailNotificationOptions
{
    public const string SectionName = "Notifications:Email";

    public bool Enabled { get; init; }
    public string SmtpHost { get; init; } = "smtp.gmail.com";
    public int SmtpPort { get; init; } = 587;

    /// <summary>Gmail account used to authenticate, e.g. you@gmail.com.</summary>
    public string? Username { get; init; }

    /// <summary>Gmail App Password (requires 2-Step Verification). Never commit it; supply via environment or user secrets.</summary>
    public string? Password { get; init; }

    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "HVTradingBot";
    public string[] ToAddresses { get; init; } = [];
    public string SubjectPrefix { get; init; } = "[HVTradingBot]";

    /// <summary>Decision statuses to notify on (legacy); RejectedByRisk switches on "order rejected" emails.</summary>
    public DecisionState[] NotifyOnStatuses { get; init; } = [];

    public int MaxSendAttempts { get; init; } = 3;

    public EmailSettings ToSettings() => new(
        Enabled,
        SmtpHost,
        SmtpPort,
        Username,
        Password,
        FromAddress,
        FromName,
        ToAddresses.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
        OnTradeOpened: true,
        OnTradeClosed: true,
        OnOrderRejected: NotifyOnStatuses.Contains(DecisionState.RejectedByRisk),
        OnKillSwitch: true,
        SubjectPrefix,
        MaxSendAttempts);
}
