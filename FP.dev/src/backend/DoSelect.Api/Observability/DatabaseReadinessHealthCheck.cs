using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DoSelect.Api.Observability;

public interface IDatabaseReadinessProbe
{
    Task<bool> CanReadAsync(CancellationToken cancellationToken);
}

public sealed class EfCoreDatabaseReadinessProbe(IServiceScopeFactory scopeFactory)
    : IDatabaseReadinessProbe
{
    public async Task<bool> CanReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var result = await dbContext.Database
            .SqlQueryRaw<int>("SELECT CAST(1 AS int) AS [Value]")
            .SingleAsync(cancellationToken);
        return result == 1;
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
            return await probe.CanReadAsync(cancellationToken)
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
