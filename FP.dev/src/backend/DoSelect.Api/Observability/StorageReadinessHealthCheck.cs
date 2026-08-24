using DoSelect.Application.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DoSelect.Api.Observability;

public sealed class StorageReadinessHealthCheck : IHealthCheck
{
    private readonly ILogger<StorageReadinessHealthCheck> _logger;
    private readonly IOptions<StorageOptions> _options;

    public StorageReadinessHealthCheck(
        IOptions<StorageOptions> options,
        ILogger<StorageReadinessHealthCheck> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probeDirectory = Path.Combine(_options.Value.DataRoot, ".health");
            Directory.CreateDirectory(probeDirectory);

            var probePath = Path.Combine(probeDirectory, $"{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);

            return Task.FromResult(
                HealthCheckResult.Healthy("Storage directory is writable."));
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            _logger.LogError(
                "Storage readiness check failed for configuration key {ConfigurationKey}.",
                "Storage:DataRoot");

            return Task.FromResult(
                HealthCheckResult.Unhealthy("Storage directory is unavailable."));
        }
    }
}
