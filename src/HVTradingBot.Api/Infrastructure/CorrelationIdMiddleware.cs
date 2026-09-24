using HVTradingBot.Infrastructure.Observability;
using Serilog.Context;

namespace HVTradingBot.Api.Infrastructure;

/// <summary>Accepts or creates an X-Correlation-Id, echoes it on the response and adds it to every log event.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var value)
                            && value.ToString() is { Length: > 0 and <= 64 } provided
            ? provided
            : Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (LogContext.PushProperty(ObservabilityExtensions.CorrelationIdProperty, correlationId))
        {
            await next(context);
        }
    }
}

public static class HttpContextExtensions
{
    public static string CorrelationId(this HttpContext context) =>
        context.Items[CorrelationIdMiddleware.HeaderName] as string ?? context.TraceIdentifier;
}
