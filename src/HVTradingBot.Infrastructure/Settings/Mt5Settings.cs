using System.Security.Cryptography;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Infrastructure.Settings;

/// <summary>What the worker needs to trade through MetaApi.</summary>
public sealed record Mt5Credentials(string Token, string AccountId, string Region, string SymbolSuffix, decimal CommissionPercent, int Version);

/// <summary>The worker's latest check of the MT5 account.</summary>
public sealed record Mt5Status(
    string State,
    string? Message,
    int SettingsVersion,
    string? Login,
    string? Server,
    string? Broker,
    bool? IsDemo,
    decimal? Balance,
    string? Currency,
    DateTime CheckedAtUtc);

/// <summary>What the dashboard may see: never the token, only whether one is stored and its last 4 characters.</summary>
public sealed record Mt5SettingsView(
    bool Enabled,
    bool TokenConfigured,
    string? TokenHint,
    string? AccountId,
    string Region,
    string SymbolSuffix,
    decimal CommissionPercent,
    int Version,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    Mt5Status? Status);

/// <summary>
/// MetaTrader 5 settings, entered on the Settings page. When enabled and complete, the worker trades Forex on this MT5
/// account (through MetaApi) instead of Deriv multipliers; saving restarts the worker to switch. The MetaApi token is
/// encrypted with Data Protection, whose keys live outside the database.
/// </summary>
public sealed class Mt5SettingsStore(IDbContextFactory<TradingDbContext> dbFactory, IDataProtectionProvider dataProtection, IClock clock)
{
    /// <summary>The broker name the MT5 broker reports (the trading state shows which broker is in use).</summary>
    public const string BrokerName = "MT5";

    public const string MetaApiRegionDefault = "new-york";

    /// <summary>MetaApi regions (the account's region is shown in the MetaApi web app).</summary>
    public static readonly IReadOnlyList<string> Regions = ["new-york", "london", "singapore", "vint-hill"];

    /// <summary>A typical round-trip cost on a raw-spread MT5 account ($7 per lot on $100,000): used for sizing.</summary>
    public const decimal DefaultCommissionPercent = 0.007m;

    private const int RowId = 1;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("HVTradingBot.Mt5.MetaApiToken.v1");

    public async Task<Mt5SettingsView> GetViewAsync(CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken);
        return row is null
            ? new Mt5SettingsView(false, false, null, null, MetaApiRegionDefault, "", DefaultCommissionPercent, 0, null, null, null)
            : new Mt5SettingsView(row.Enabled, row.TokenProtected is not null, row.TokenHint, row.AccountId, row.Region, row.SymbolSuffix,
                row.CommissionPercent, row.Version, row.UpdatedAtUtc, row.UpdatedBy, Status(row));
    }

    /// <summary>The settings to trade with, or null when MT5 is off or incomplete (Deriv multipliers are used).</summary>
    public async Task<Mt5Credentials?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken);
        if (row is not { Enabled: true, TokenProtected: { } protectedToken, AccountId: { } accountId })
        {
            return null;
        }

        string token;
        try
        {
            token = _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            throw new BrokerUnavailableException(
                "The stored MetaApi token cannot be decrypted (the encryption keys changed, e.g. after restoring a backup on another phone). Enter it again in Settings.");
        }

        return new Mt5Credentials(token, accountId, row.Region, row.SymbolSuffix, row.CommissionPercent, row.Version);
    }

    /// <summary>Whether MT5 is switched on with a token and account (read at worker start to choose the broker).</summary>
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken) =>
        await RowAsync(cancellationToken) is { Enabled: true, TokenProtected: not null, AccountId: not null };

    /// <summary>MT5's commission for position sizing while MT5 is active; null otherwise.</summary>
    public async Task<decimal?> GetActiveCommissionAsync(CancellationToken cancellationToken) =>
        await RowAsync(cancellationToken) is { Enabled: true, TokenProtected: not null, AccountId: not null } row ? row.CommissionPercent : null;

    public async Task<int> GetVersionAsync(CancellationToken cancellationToken) => (await RowAsync(cancellationToken))?.Version ?? 0;

    /// <summary>Saves settings. A null <paramref name="token"/> keeps the stored token.</summary>
    public async Task<Mt5SettingsView> SaveAsync(bool enabled, string? token, string? accountId, string region, string symbolSuffix,
        decimal commissionPercent, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Mt5Settings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            row = new Mt5SettingsEntity { Id = RowId };
            db.Mt5Settings.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            row.TokenProtected = _protector.Protect(token.Trim());
            row.TokenHint = token.Trim().Length <= 4 ? "••••" : token.Trim()[^4..];
        }

        row.Enabled = enabled;
        row.AccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        row.Region = region;
        row.SymbolSuffix = symbolSuffix.Trim();
        row.CommissionPercent = commissionPercent;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        return await GetViewAsync(cancellationToken);
    }

    /// <summary>Forgets the token and turns MT5 off (Forex goes back to Deriv multipliers).</summary>
    public async Task ClearAsync(string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Mt5Settings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Enabled = false;
        row.TokenProtected = null;
        row.TokenHint = null;
        row.Version++;
        row.UpdatedAtUtc = clock.UtcNow;
        row.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteStatusAsync(Mt5Status status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Mt5Settings.SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.StatusState = status.State;
        row.StatusMessage = status.Message is { Length: > 2000 } m ? m[..2000] : status.Message;
        row.StatusVersion = status.SettingsVersion;
        row.StatusLogin = status.Login;
        row.StatusServer = status.Server;
        row.StatusBroker = status.Broker;
        row.StatusIsDemo = status.IsDemo;
        row.StatusBalance = status.Balance;
        row.StatusCurrency = status.Currency;
        row.StatusCheckedAtUtc = status.CheckedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Mt5SettingsEntity?> RowAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Mt5Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == RowId, cancellationToken);
    }

    private static Mt5Status? Status(Mt5SettingsEntity row) => row.StatusState is null
        ? null
        : new Mt5Status(row.StatusState, row.StatusMessage, row.StatusVersion, row.StatusLogin, row.StatusServer, row.StatusBroker, row.StatusIsDemo,
            row.StatusBalance, row.StatusCurrency, row.StatusCheckedAtUtc ?? row.UpdatedAtUtc);
}
