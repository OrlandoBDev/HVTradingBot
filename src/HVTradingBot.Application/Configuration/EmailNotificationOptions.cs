using HVTradingBot.Domain.Trading;

namespace HVTradingBot.Application.Configuration;

public sealed class EmailNotificationOptions
{
    public const string SectionName = "Notifications:Email";

    public static readonly IReadOnlyCollection<TradeDecisionStatus> DefaultStatuses =
    [
        TradeDecisionStatus.Candidate,
        TradeDecisionStatus.RejectedByRisk,
        TradeDecisionStatus.ApprovalRequired,
        TradeDecisionStatus.Approved,
        TradeDecisionStatus.Executed
    ];

    public bool Enabled { get; init; }
    public string SmtpHost { get; init; } = "smtp.gmail.com";
    public int SmtpPort { get; init; } = 587;

    /// <summary>Gmail account used to authenticate, e.g. you@gmail.com.</summary>
    public string? Username { get; init; }

    /// <summary>Gmail App Password (requires 2-Step Verification). Never commit it; supply via environment or user secrets.</summary>
    public string? Password { get; init; }

    /// <summary>Sender address. Defaults to <see cref="Username"/>.</summary>
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "HVTradingBot";
    public string[] ToAddresses { get; init; } = [];
    public string SubjectPrefix { get; init; } = "[HVTradingBot]";

    /// <summary>Decision statuses that trigger an email. Defaults to <see cref="DefaultStatuses"/> when empty.</summary>
    public TradeDecisionStatus[] NotifyOnStatuses { get; init; } = [];

    public int MaxSendAttempts { get; init; } = 3;

    public string? EffectiveFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    public bool ShouldNotify(TradeDecisionStatus status) =>
        NotifyOnStatuses.Length == 0 ? DefaultStatuses.Contains(status) : NotifyOnStatuses.Contains(status);
}
