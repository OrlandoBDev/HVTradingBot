using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
        using var client = new SmtpClient
        {
            Timeout = (int)Timeout.TotalMilliseconds,
            ServerCertificateValidationCallback = (_, _, chain, errors) => IsAcceptable(chain, errors)
        };
        var socketOptions = settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, socketOptions, timeout.Token);
        await client.AuthenticateAsync(username, password, timeout.Token);
        await client.SendAsync(mime, timeout.Token);
        await client.DisconnectAsync(quit: true, timeout.Token);
    }

    private static readonly X509ChainStatusFlags RevocationUnknown =
        X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

    /// <summary>
    /// Full TLS validation, with one exception: if the chain and host name are valid and the only problem is that the
    /// revocation status could not be determined (common on macOS when OCSP/CRL servers do not answer), the certificate
    /// is accepted. Untrusted, expired, mismatched or revoked certificates are always rejected.
    /// </summary>
    public static bool IsAcceptable(X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || chain is null)
        {
            return false; // name mismatch or missing certificate
        }

        return IsOnlyRevocationUnknown(chain.ChainStatus.Select(s => s.Status));
    }

    public static bool IsOnlyRevocationUnknown(IEnumerable<X509ChainStatusFlags> statuses)
    {
        var flags = statuses.Aggregate(X509ChainStatusFlags.NoError, (all, s) => all | s);
        return flags != X509ChainStatusFlags.NoError && (flags & ~RevocationUnknown) == X509ChainStatusFlags.NoError;
    }
}
