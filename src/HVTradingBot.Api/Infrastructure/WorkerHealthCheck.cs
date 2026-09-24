using HVTradingBot.Application.Abstractions;
using HVTradingBot.Domain.Risk;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVTradingBot.Api.Infrastructure;

/// <summary>Reports whether the trading worker is alive (heartbeat) and receiving market data.</summary>
public sealed class WorkerHealthCheck(ITradingStateStore state, IClock clock, RiskOptions risk) : IHealthCheck
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(60);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var s = await state.GetAsync(cancellationToken);
        var now = clock.UtcNow;
        if (s.WorkerHeartbeatUtc is null || now - s.WorkerHeartbeatUtc > HeartbeatTimeout)
        {
            return HealthCheckResult.Unhealthy($"No worker heartbeat since {s.WorkerHeartbeatUtc:u}.");
        }

        if (s.LastDataReceivedUtc is null || now - s.LastDataReceivedUtc > TimeSpan.FromSeconds(risk.MaxMarketDataAgeSeconds))
        {
            return HealthCheckResult.Degraded("Market data is stale; new trades are blocked.");
        }

        return s.KillSwitchActive
            ? HealthCheckResult.Degraded($"Kill switch active: {s.KillSwitchReason}")
            : HealthCheckResult.Healthy("Worker running, data fresh.");
    }
}
