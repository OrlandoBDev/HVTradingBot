using System.Security.Cryptography;
using System.Text.Json;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Settings;

public sealed record DerivCredentials(string AppId, string ApiToken, string? AccountId, int Version);

public sealed record DerivAccountSummary(string AccountId, string AccountType, string Currency);

public enum DerivConnectionState
{
    NotConfigured,
    Connected,
    Failed
}

public sealed record DerivConnectionStatus(
    DerivConnectionState State,
    string? Message,
    int SettingsVersion,
    string? ConnectedAccountId,
    IReadOnlyList<DerivAccountSummary> Accounts,
    DateTime CheckedAtUtc);

/// <summary>What the dashboard may see: never the token itself, only whether one is stored and its last 4 characters.</summary>
public sealed record DerivSettingsView(
    string? AppId,
    bool TokenConfigured,
    string? TokenHint,
    string? AccountId,
    int Version,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    string Source);

public interface IDerivCredentialsProvider
{
    /// <summary>Current credentials, or null when none are configured.</summary>
    Task<DerivCredentials?> GetAsync(CancellationToken cancellationToken);
}

public interface IDerivStatusSink
{
    Task WriteStatusAsync(DerivConnectionStatus status, CancellationToken cancellationToken);
}

/// <summary>
/// Stores Deriv settings in PostgreSQL. The API writes them (Settings page); the worker reads them and reports the
/// connection result. The token is encrypted with ASP.NET Core Data Protection, whose keys live outside the database.
/// Values from environment variables (DERIV_APP_ID / DERIV_API_TOKEN) are used only when nothing is stored.
/// </summary>
public sealed class DerivSettingsStore(
    IDbContextFactory<TradingDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    DerivEnvironmentCredentials environment,
    IClock clock) : IDerivCredentialsProvider, IDerivStatusSink
{
    private const int RowId = 1;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("HVTradingBot.Deriv.ApiToken.v1");

    public async Task<DerivSettingsView> GetViewAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row?.DerivApiTokenProtected is null && environment.IsComplete)
        {
            return new DerivSettingsView(environment.AppId, true, Hint(environment.ApiToken!), environment.AccountId ?? row?.DerivAccountId,
                row?.Version ?? 0, row?.UpdatedAtUtc, row?.UpdatedBy, "environment");
        }

        return new DerivSettingsView(row?.DerivAppId, row?.DerivApiTokenProtected is not null, row?.DerivApiTokenHint, row?.DerivAccountId,
            row?.Version ?? 0, row?.UpdatedAtUtc, row?.UpdatedBy, "database");
    }

    public async Task<DerivCredentials?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is { DerivAppId: { } appId, DerivApiTokenProtected: { } protectedToken })
        {
            string token;
            try
            {
                token = _protector.Unprotect(protectedToken);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException(
                    "The stored Deriv token cannot be decrypted because the encryption keys changed (e.g. switching between Docker and local runs). Re-enter the token in Settings.");
            }

            return new DerivCredentials(appId, token, row.DerivAccountId, row.Version);
        }

        return environment.IsComplete
            ? new DerivCredentials(environment.AppId!, environment.ApiToken!, row?.DerivAccountId ?? environment.AccountId, row?.Version ?? 0)
            : null;
    }

    /// <summary>Saves settings. A null <paramref name="apiToken"/> keeps the stored token.</summary>
    public async Task<DerivSettingsView> SaveAsync(string appId, string? apiToken, string? accountId, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerSettings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new BrokerSettingsEntity { Id = RowId };
            db.BrokerSettings.Add(row);
        }

        if (apiToken is null && row.DerivApiTokenProtected is null)
        {
            throw new ArgumentException("An API token is required.", nameof(apiToken));
        }

        row.DerivAppId = appId;
        if (apiToken is not null)
        {
            row.DerivApiTokenProtected = _protector.Protect(apiToken);
            row.DerivApiTokenHint = Hint(apiToken);
        }

        row.DerivAccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return await GetViewAsync(cancellationToken);
    }

    public async Task ClearAsync(string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerSettings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.DerivAppId = null;
        row.DerivApiTokenProtected = null;
        row.DerivApiTokenHint = null;
        row.DerivAccountId = null;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DerivConnectionStatus?> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerConnectionStatus.AsNoTracking().SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        return row is null
            ? null
            : new DerivConnectionStatus(
                Enum.Parse<DerivConnectionState>(row.Status),
                row.Message,
                row.SettingsVersion,
                row.ConnectedAccountId,
                row.AccountsJson is null ? [] : JsonSerializer.Deserialize<List<DerivAccountSummary>>(row.AccountsJson, JsonDefaults.Options) ?? [],
                row.CheckedAtUtc);
    }

    public async Task WriteStatusAsync(DerivConnectionStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BrokerConnectionStatus.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new BrokerConnectionStatusEntity { Id = RowId, Status = status.State.ToString() };
            db.BrokerConnectionStatus.Add(row);
        }

        row.Status = status.State.ToString();
        row.Message = status.Message is { Length: > 2000 } m ? m[..2000] : status.Message;
        row.SettingsVersion = status.SettingsVersion;
        row.ConnectedAccountId = status.ConnectedAccountId;
        row.AccountsJson = JsonSerializer.Serialize(status.Accounts, JsonDefaults.Options);
        row.CheckedAtUtc = status.CheckedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Hint(string token) => token.Length <= 4 ? "••••" : token[^4..];
}

/// <summary>Optional fallback credentials from environment variables (Deriv__AppId, Deriv__ApiToken, Deriv__AccountId).</summary>
public sealed record DerivEnvironmentCredentials(string? AppId, string? ApiToken, string? AccountId)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ApiToken);
}
