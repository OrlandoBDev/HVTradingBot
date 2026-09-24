using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>
/// Sends email over SMTP with STARTTLS. Defaults target Gmail (smtp.gmail.com:587) using an App Password.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailNotificationOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        var username = settings.Username ?? throw new InvalidOperationException("Notifications:Email:Username is not configured.");
        var password = settings.Password ?? throw new InvalidOperationException("Notifications:Email:Password is not configured.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.EffectiveFromAddress ?? username));
        foreach (var to in settings.ToAddresses)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        var socketOptions = settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socketOptions, cancellationToken);
        await client.AuthenticateAsync(username, password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
