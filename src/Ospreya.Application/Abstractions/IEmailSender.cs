using Ospreya.Application.Notifications;

namespace Ospreya.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken cancellationToken);
}

/// <summary>The email settings in force; re-read periodically so Settings-page changes apply without a restart.</summary>
public interface IEmailSettingsProvider
{
    EmailSettings Current { get; }

    Task<EmailSettings> RefreshAsync(CancellationToken cancellationToken);
}
