using System.Security.Claims;
using HVTradingBot.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HVTradingBot.Api.Auth;

public static class AuthEndpoints
{
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", async (HttpContext ctx, UserAccounts accounts, SetupCode setupCode, CancellationToken ct) =>
        {
            var setupRequired = !await accounts.AnyAsync(ct);
            if (setupRequired)
            {
                setupCode.Current(); // make sure a code is in the log
            }

            var signedIn = ctx.User.Identity?.IsAuthenticated == true;
            return new AuthStatusDto(signedIn, signedIn ? ctx.User.Identity!.Name : null, setupRequired);
        }).AllowAnonymous();

        auth.MapPost("/setup", async (SetupRequest request, HttpContext ctx, UserAccounts accounts, SetupCode setupCode, CancellationToken ct) =>
        {
            if (await accounts.AnyAsync(ct))
            {
                return Results.Problem("The login has already been created.", statusCode: StatusCodes.Status409Conflict);
            }

            if (!setupCode.Matches(request.SetupCode))
            {
                await accounts.AuditAsync(Actor(ctx), "LoginSetupFailed", "Wrong setup code.", ct);
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["setupCode"] = ["Setup code is wrong. It is printed in the API log."] });
            }

            var errors = UserAccounts.ValidateCredentials(request.Username, request.Password);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var user = await accounts.CreateOwnerAsync(request.Username, request.Password, ct);
            if (user is null)
            {
                return Results.Problem("The login has already been created.", statusCode: StatusCodes.Status409Conflict);
            }

            setupCode.Consume();
            await ctx.SignInAsync(Scheme, UserAccounts.Principal(user, Scheme));
            return Results.Ok(new AuthStatusDto(true, user.Username, false));
        }).AllowAnonymous().RequireRateLimiting(AuthSetup.LoginRateLimit);

        auth.MapPost("/login", async (LoginRequest request, HttpContext ctx, UserAccounts accounts, CancellationToken ct) =>
        {
            var (outcome, user) = await accounts.VerifyAsync(request.Username ?? "", request.Password ?? "", ct);
            switch (outcome)
            {
                case LoginOutcome.LockedOut:
                    return Results.Problem($"Too many wrong passwords. Try again in {UserAccounts.LockoutDuration.TotalMinutes:F0} minutes.",
                        statusCode: StatusCodes.Status423Locked);
                case LoginOutcome.InvalidCredentials:
                    return Results.Problem("Wrong username or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var properties = new AuthenticationProperties { IsPersistent = request.RememberMe };
            if (request.RememberMe)
            {
                properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
                properties.AllowRefresh = false;
            }

            await ctx.SignInAsync(Scheme, UserAccounts.Principal(user!, Scheme), properties);
            return Results.Ok(new AuthStatusDto(true, user!.Username, false));
        }).AllowAnonymous().RequireRateLimiting(AuthSetup.LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(Scheme);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/password", async (ChangePasswordRequest request, HttpContext ctx, UserAccounts accounts, CancellationToken ct) =>
        {
            var errors = UserAccounts.ValidateCredentials(ctx.User.Identity?.Name, request.NewPassword);
            if (errors.TryGetValue("password", out var passwordErrors))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = passwordErrors });
            }

            var user = await accounts.ChangePasswordAsync(Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                request.CurrentPassword ?? "", request.NewPassword, ct);
            if (user is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Current password is wrong."] });
            }

            // New stamp: this browser gets a fresh cookie, every other session is signed out.
            await ctx.SignInAsync(Scheme, UserAccounts.Principal(user, Scheme));
            return Results.NoContent();
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);
    }

    private static string Actor(HttpContext ctx) => $"ip:{ctx.Connection.RemoteIpAddress}";
}
