using System.Text.Json.Serialization;
using HVTradingBot.Api.Auth;
using HVTradingBot.Api.Endpoints;
using HVTradingBot.Api.Hubs;
using HVTradingBot.Api.Infrastructure;
using HVTradingBot.Api.Services;
using HVTradingBot.Dashboard;
using HVTradingBot.Infrastructure;
using HVTradingBot.Infrastructure.Configuration;
using HVTradingBot.Infrastructure.Observability;
using HVTradingBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

const string serviceName = "HVTradingBot.Api";
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddSharedTradingConfiguration(builder.Environment.EnvironmentName);

    builder.Services.AddSerilog((_, logger) => logger.ConfigureHvLogging(builder.Configuration, serviceName));
    builder.Services.AddHvTelemetry(builder.Configuration, serviceName,
        tracing: t => t.AddAspNetCoreInstrumentation(),
        metrics: m => m.AddAspNetCoreInstrumentation());

    builder.Services.AddTradingCore(builder.Configuration);
    builder.Services.AddDashboard();
    builder.Services.AddSingleton<LiveMarketStream>();
    builder.Services.AddHostedService<DashboardBroadcaster>();
    builder.Services.AddHostedService<MarketCatalogLoader>();
    builder.Services.AddDashboardAuth();
    builder.Services.AddProblemDetails();
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<TradingDbContext>("database", tags: ["ready"])
        .AddCheck<WorkerHealthCheck>("trading-worker", tags: ["ready"]);

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    {
        await app.Services.MigrateDatabaseAsync(CancellationToken.None);
    }

    if (!await app.Services.GetRequiredService<UserAccounts>().AnyAsync(CancellationToken.None))
    {
        app.Services.GetRequiredService<SetupCode>().Current(); // logs the code needed to create the login
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/api") ? Serilog.Events.LogEventLevel.Debug
        : Serilog.Events.LogEventLevel.Verbose);
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseDefaultFiles();
    // index.html is always revalidated so a rebuilt dashboard shows up on the next load; the hashed assets it points
    // to never change and may be cached for good.
    var dashboardFiles = new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
            ctx.File.Name == "index.html" ? "no-cache" : ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache"
    };
    app.UseStaticFiles(dashboardFiles);
    app.UseDashboardAuth();

    app.MapAuthEndpoints();
    app.MapTradingEndpoints();
    app.MapSettingsEndpoints();
    app.MapHub<DashboardHub>(DashboardHub.Path);
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = c => c.Tags.Contains("ready"),
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), e.Value.Description })
            });
        }
    }).AllowAnonymous();
    app.MapFallbackToFile("index.html", dashboardFiles).AllowAnonymous();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "{Service} terminated unexpectedly", serviceName);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed for integration tests.</summary>
public partial class Program;
