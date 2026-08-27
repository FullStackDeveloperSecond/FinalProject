using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DoSelect.Api.Observability;

public sealed class HangfireReadinessHealthCheck(
    ILogger<HangfireReadinessHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var servers = JobStorage.Current.GetMonitoringApi().Servers();
            return Task.FromResult(servers.Count > 0
                ? HealthCheckResult.Healthy("Hangfire storage and server heartbeat are available.")
                : HealthCheckResult.Unhealthy("Hangfire has no active server heartbeat."));
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Hangfire readiness check failed with exception type {ExceptionType}.",
                exception.GetType().Name);
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Hangfire storage or server is unavailable."));
        }
    }
}
