using System.Security.Claims;
using System.Security.Cryptography;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Persistence;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HVTradingBot.Api.Auth;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut
}

/// <summary>The owner account: creation, password checks with lockout, and password changes. Every change is audited.</summary>
public sealed class UserAccounts(IDbContextFactory<TradingDbContext> dbFactory, IClock clock, ILogger<UserAccounts> logger)
{
    public const int MinPasswordLength = 10;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public const string StampClaim = "hv:stamp";

    private static readonly PasswordHasher<AppUserEntity> Hasher = new();

    // Verified when the username is unknown, so a wrong username takes as long as a wrong password.
    private static readonly string DummyHash = Hasher.HashPassword(null!, RandomNumberGenerator.GetHexString(32));

    public async Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AppUsers.AnyAsync(cancellationToken);
    }

    public static IReadOnlyDictionary<string, string[]> ValidateCredentials(string? username, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        var name = username?.Trim() ?? "";
        if (name.Length is < 3 or > 64)
        {
            errors["username"] = ["Username must be 3 to 64 characters."];
        }

        if (password is null || password.Length < MinPasswordLength)
        {
            errors["password"] = [$"Password must be at least {MinPasswordLength} characters."];
        }
        else if (string.Equals(password, name, StringComparison.OrdinalIgnoreCase))
        {
            errors["password"] = ["Password must not be the username."];
        }

        return errors;
    }

    /// <summary>Creates the owner account; returns null if one already exists.</summary>
    public async Task<AppUserEntity?> CreateOwnerAsync(string username, string password, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.AppUsers.AnyAsync(cancellationToken))
        {
            return null;
        }

        var user = new AppUserEntity
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            PasswordHash = "",
            SecurityStamp = NewStamp(),
            CreatedAtUtc = clock.UtcNow
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.AppUsers.Add(user);
        Audit(db, user.Username, "LoginCreated", "Dashboard owner account created.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return null; // another setup request won the race
        }

        logger.LogInformation("Dashboard owner account {Username} created", user.Username);
        return user;
    }

    public async Task<(LoginOutcome Outcome, AppUserEntity? User)> VerifyAsync(string username, string password, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var name = username.Trim();
        var user = await db.AppUsers.SingleOrDefaultAsync(u => u.Username.ToLower() == name.ToLower(), cancellationToken);
        if (user is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            Audit(db, name, "LoginFailed", "Unknown username.");
            await db.SaveChangesAsync(cancellationToken);
            return (LoginOutcome.InvalidCredentials, null);
        }

        var now = clock.UtcNow;
        if (user.LockedUntilUtc is { } until && until > now)
        {
            return (LoginOutcome.LockedOut, null);
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLogins++;
            if (user.FailedLogins >= MaxFailedLogins)
            {
                user.LockedUntilUtc = now + LockoutDuration;
                user.FailedLogins = 0;
                Audit(db, user.Username, "LoginLocked", $"{MaxFailedLogins} wrong passwords; login locked for {LockoutDuration.TotalMinutes:F0} minutes.");
            }
            else
            {
                Audit(db, user.Username, "LoginFailed", "Wrong password.");
            }

            await db.SaveChangesAsync(cancellationToken);
            return (user.LockedUntilUtc > now ? LoginOutcome.LockedOut : LoginOutcome.InvalidCredentials, null);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        user.FailedLogins = 0;
        user.LockedUntilUtc = null;
        user.LastLoginAtUtc = now;
        Audit(db, user.Username, "LoginSucceeded", "Signed in to the dashboard.");
        await db.SaveChangesAsync(cancellationToken);
        return (LoginOutcome.Success, user);
    }

    /// <summary>Changes the password and the security stamp (signing out other sessions). Returns the updated user, or null if the current password is wrong.</summary>
    public async Task<AppUserEntity?> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        user.SecurityStamp = NewStamp();
        user.PasswordChangedAtUtc = clock.UtcNow;
        Audit(db, user.Username, "PasswordChanged", "Dashboard password changed; other sessions were signed out.");
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <summary>True while the session's user still exists and its password has not changed since sign-in.</summary>
    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return false;
        }

        var stamp = principal.FindFirstValue(StampClaim);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AppUsers.AnyAsync(u => u.Id == id && u.SecurityStamp == stamp, cancellationToken);
    }

    public static ClaimsPrincipal Principal(AppUserEntity user, string scheme) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(StampClaim, user.SecurityStamp)
    ], scheme));

    public async Task AuditAsync(string actor, string action, string details, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Audit(db, actor, action, details);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Audit(TradingDbContext db, string actor, string action, string details) =>
        db.AuditLogs.Add(new AuditLogEntity { TimestampUtc = clock.UtcNow, Actor = actor, Action = action, Details = details });

    private static string NewStamp() => RandomNumberGenerator.GetHexString(32);
}
