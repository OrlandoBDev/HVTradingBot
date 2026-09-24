using HVTradingBot.Application.Trading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace HVTradingBot.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public const string CorrelationIdProperty = "CorrelationId";

    /// <summary>Serilog from configuration ("Serilog" section) with log-context enrichment for correlation ids.</summary>
    public static LoggerConfiguration ConfigureHvLogging(this LoggerConfiguration logger, IConfiguration configuration, string serviceName) =>
        logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName);

    /// <summary>
    /// OpenTelemetry traces and metrics for the trading engine. Exported via OTLP only when
    /// OTEL_EXPORTER_OTLP_ENDPOINT is set, so local runs need no collector.
    /// </summary>
    public static IServiceCollection AddHvTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        var otlpEnabled = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddSource(TradingEngine.TelemetryName);
                tracing?.Invoke(t);
                if (otlpEnabled) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddMeter(TradingEngine.TelemetryName);
                metrics?.Invoke(m);
                if (otlpEnabled) m.AddOtlpExporter();
            });
        return services;
    }
}
