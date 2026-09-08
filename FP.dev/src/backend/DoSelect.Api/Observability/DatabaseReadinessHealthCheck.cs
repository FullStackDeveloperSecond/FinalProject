using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DoSelect.Api.Observability;

public interface IDatabaseReadinessProbe
{
    Task<DatabaseReadinessProbeStatus> CheckAsync(CancellationToken cancellationToken);
}

public enum DatabaseReadinessProbeStatus
{
    Ready,
    QueryReturnedUnexpectedResult,
    SchemaOutdated,
}

public sealed class EfCoreDatabaseReadinessProbe(IServiceScopeFactory scopeFactory)
    : IDatabaseReadinessProbe
{
    public async Task<DatabaseReadinessProbeStatus> CheckAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            return DatabaseReadinessProbeStatus.SchemaOutdated;
        }

        var result = await dbContext.Database
            .SqlQueryRaw<int>("SELECT CAST(1 AS int) AS [Value]")
            .SingleAsync(cancellationToken);
        return result == 1
            ? DatabaseReadinessProbeStatus.Ready
            : DatabaseReadinessProbeStatus.QueryReturnedUnexpectedResult;
    }
}

public sealed class DatabaseReadinessHealthCheck(
    IDatabaseReadinessProbe probe,
    ILogger<DatabaseReadinessHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await probe.CheckAsync(cancellationToken);
            if (status == DatabaseReadinessProbeStatus.SchemaOutdated)
            {
                logger.LogError(
                    "Database schema readiness failed because one or more EF Core migrations are pending. Apply the pending migrations before accepting traffic.");
                return HealthCheckResult.Unhealthy("Database schema is outdated.");
            }

            return status == DatabaseReadinessProbeStatus.Ready
                ? HealthCheckResult.Healthy("Database query succeeded.")
                : HealthCheckResult.Unhealthy("Database query returned an unexpected result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Database readiness check failed with exception type {ExceptionType}.",
                exception.GetType().Name);
            return HealthCheckResult.Unhealthy("Database query failed.");
        }
    }
}
