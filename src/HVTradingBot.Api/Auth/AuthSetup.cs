using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace HVTradingBot.Api.Auth;

/// <summary>
/// Dashboard sign-in: an HttpOnly, SameSite=Strict session cookie, required by default for every endpoint (API and
/// SignalR). Only the sign-in endpoints, health checks and the static dashboard files are public.
/// </summary>
public static class AuthSetup
{
    public const string LoginRateLimit = "login";

    /// <summary>Header every state-changing API request must carry; a cross-site form or image cannot add it.</summary>
    public const string RequestHeader = "X-HV-Request";

    public static IServiceCollection AddDashboardAuth(this IServiceCollection services)
    {
        services.AddSingleton<SetupCode>();
        services.AddSingleton<UserAccounts>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "hv.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS-only once hosted behind TLS
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.SlidingExpiration = true;
                o.Events = new CookieAuthenticationEvents
                {
                    // An API answers 401/403 instead of redirecting to a login page.
                    OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
                    OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
                    OnValidatePrincipal = async ctx =>
                    {
                        var accounts = ctx.HttpContext.RequestServices.GetRequiredService<UserAccounts>();
                        if (ctx.Principal is null || !await accounts.IsSessionValidAsync(ctx.Principal, ctx.HttpContext.RequestAborted))
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    public static WebApplication UseDashboardAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
                && !ctx.Request.Headers.ContainsKey(RequestHeader))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { title = $"Missing {RequestHeader} header." });
                return;
            }

            await next();
        });
        return app;
    }
}
