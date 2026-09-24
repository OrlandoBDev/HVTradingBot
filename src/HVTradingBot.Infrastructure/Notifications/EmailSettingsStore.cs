using System.Security.Cryptography;
using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Application.Configuration;
using HVTradingBot.Application.Notifications;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HVTradingBot.Infrastructure.Notifications;

/// <summary>Values entered on the Settings page. A null <see cref="Password"/> keeps the stored password.</summary>
public sealed record EmailSettingsInput(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    bool OnTradeOpened,
    bool OnTradeClosed,
    bool OnOrderRejected,
    bool OnKillSwitch);

/// <summary>What the dashboard may see: never the password, only whether one is stored and its last characters.</summary>
public sealed record EmailSettingsView(
    EmailSettings Settings,
    bool PasswordConfigured,
    string? PasswordHint,
    string Source,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    DateTime? LastAttemptUtc,
    bool? LastAttemptSucceeded,
    string? LastError);

/// <summary>
/// Email settings in PostgreSQL (SMTP password encrypted with ASP.NET Core Data Protection). Falls back to the
/// Notifications:Email configuration when nothing is saved. <see cref="Current"/> is a cached snapshot refreshed by
/// <see cref="RefreshAsync"/> (the worker calls it every few seconds).
/// </summary>
public sealed class EmailSettingsStore(
    IDbContextFactory<TradingDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IOptions<EmailNotificationOptions> fallback,
    IClock clock,
    ILogger<EmailSettingsStore> logger) : IEmailSettingsProvider
{
    private const int RowId = 1;
    public const int MaxRecipients = 10;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("HVTradingBot.Email.Password.v1");
    private volatile EmailSettings _current = EmailSettings.Disabled;

    public EmailSettings Current => _current;

    public async Task<EmailSettings> RefreshAsync(CancellationToken cancellationToken)
    {
        _current = (await GetViewAsync(cancellationToken)).Settings;
        return _current;
    }

    public async Task<EmailSettingsView> GetViewAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.NotificationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            var configured = fallback.Value.ToSettings();
            return new EmailSettingsView(configured, !string.IsNullOrEmpty(configured.Password), Hint(configured.Password),
                configured.Enabled ? "environment" : "none", null, null, null, null, null);
        }

        string? password = null;
        if (row.PasswordProtected is not null)
        {
            try
            {
                password = _protector.Unprotect(row.PasswordProtected);
            }
            catch (CryptographicException)
            {
                logger.LogError("Stored SMTP password cannot be decrypted (encryption keys changed); re-enter it in Settings");
            }
        }

        var settings = new EmailSettings(row.Enabled, row.SmtpHost, row.SmtpPort, row.Username, password, row.FromAddress, row.FromName,
            JsonSerializer.Deserialize<List<string>>(row.ToAddresses) ?? [], row.OnTradeOpened, row.OnTradeClosed, row.OnOrderRejected,
            row.OnKillSwitch, fallback.Value.SubjectPrefix, fallback.Value.MaxSendAttempts);
        return new EmailSettingsView(settings, password is not null, row.PasswordHint, "database", row.UpdatedAtUtc, row.UpdatedBy,
            row.LastAttemptUtc, row.LastAttemptSucceeded, row.LastError);
    }

    public async Task<EmailSettingsView> SaveAsync(EmailSettingsInput input, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.NotificationSettings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new NotificationSettingsEntity { Id = RowId, SmtpHost = input.SmtpHost, FromName = "HVTradingBot", ToAddresses = "[]" };
            db.NotificationSettings.Add(row);
        }

        row.Enabled = input.Enabled;
        row.SmtpHost = input.SmtpHost.Trim();
        row.SmtpPort = input.SmtpPort;
        row.Username = Clean(input.Username);
        if (!string.IsNullOrEmpty(input.Password))
        {
            row.PasswordProtected = _protector.Protect(input.Password);
            row.PasswordHint = Hint(input.Password);
        }

        row.FromAddress = Clean(input.FromAddress);
        row.FromName = Clean(input.FromName) ?? "HVTradingBot";
        row.ToAddresses = JsonSerializer.Serialize(input.ToAddresses.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        row.OnTradeOpened = input.OnTradeOpened;
        row.OnTradeClosed = input.OnTradeClosed;
        row.OnOrderRejected = input.OnOrderRejected;
        row.OnKillSwitch = input.OnKillSwitch;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);

        var view = await GetViewAsync(cancellationToken);
        _current = view.Settings;
        return view;
    }

    public async Task RecordAttemptAsync(bool succeeded, string? error, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.NotificationSettings.Where(s => s.Id == RowId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.LastAttemptUtc, clock.UtcNow)
            .SetProperty(s => s.LastAttemptSucceeded, succeeded)
            .SetProperty(s => s.LastError, error == null ? null : error.Length > 1000 ? error.Substring(0, 1000) : error), cancellationToken);
    }

    /// <summary>
    /// Checks the input; a stored password counts when none is entered. Returns field errors keyed by input name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(EmailSettingsInput input, bool passwordStored)
    {
        var errors = new Dictionary<string, string>();
        if (input.ToAddresses.Count > MaxRecipients) errors["toAddresses"] = $"At most {MaxRecipients} recipients.";
        foreach (var address in input.ToAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            if (!IsEmail(address)) errors["toAddresses"] = $"'{address}' is not a valid email address.";
        }

        if (!string.IsNullOrWhiteSpace(input.FromAddress) && !IsEmail(input.FromAddress)) errors["fromAddress"] = "Sender must be a valid email address.";
        if (input.SmtpPort is < 1 or > 65535) errors["smtpPort"] = "Port must be between 1 and 65535.";
        if (string.IsNullOrWhiteSpace(input.SmtpHost) || input.SmtpHost.Length > 255 || input.SmtpHost.Any(char.IsWhiteSpace)) errors["smtpHost"] = "SMTP server is required.";
        if (input.Password is { Length: > 256 }) errors["password"] = "Password is too long.";

        if (input.Enabled)
        {
            if (string.IsNullOrWhiteSpace(input.Username)) errors["username"] = "Username (your email address) is required to send email.";
            if (string.IsNullOrEmpty(input.Password) && !passwordStored) errors["password"] = "An app password is required to send email.";
            if (!input.ToAddresses.Any(a => !string.IsNullOrWhiteSpace(a))) errors["toAddresses"] = "Add at least one recipient.";
            if (string.IsNullOrWhiteSpace(input.FromAddress) && !IsEmail(input.Username ?? "")) errors["fromAddress"] = "Set a sender address (or use an email address as username).";
        }

        return errors;
    }

    private static bool IsEmail(string value) => MailboxAddress.TryParse(value.Trim(), out var mailbox) && mailbox.Address.Contains('@');

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Hint(string? secret) => string.IsNullOrEmpty(secret) ? null : secret.Length <= 4 ? "••••" : secret[^4..];
}
