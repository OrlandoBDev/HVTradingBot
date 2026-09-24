using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>
/// Sends email over SMTP (STARTTLS on 587, TLS on 465). Defaults target Gmail (smtp.gmail.com:587) with an App Password.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var username = settings.Username ?? throw new InvalidOperationException("SMTP username is not configured.");
        var password = settings.Password ?? throw new InvalidOperationException("SMTP password is not configured.");
        if (settings.ToAddresses.Count == 0)
        {
            throw new InvalidOperationException("No email recipients are configured.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.EffectiveFromAddress ?? username));
        foreach (var to in settings.ToAddresses)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        var socketOptions = settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socketOptions, timeout.Token);
        await client.AuthenticateAsync(username, password, timeout.Token);
        await client.SendAsync(mime, timeout.Token);
        await client.DisconnectAsync(quit: true, timeout.Token);
    }
}
