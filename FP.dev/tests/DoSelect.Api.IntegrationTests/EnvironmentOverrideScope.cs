namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// Applies a set of process-level environment variable overrides for the lifetime of the scope,
/// restoring each key's actual prior value (not blanking it) on Dispose.
/// </summary>
/// <remarks>
/// 組長 PR #34 round-5 review, item 1: several <c>WebApplicationFactory</c> fixtures set
/// <c>ConnectionStrings__DefaultConnection</c> (and friends) via
/// <see cref="Environment.SetEnvironmentVariable(string, string?)"/> before constructing the
/// factory — required because <c>Program.cs</c> reads the connection string eagerly, before a
/// <c>WithWebHostBuilder</c>/<c>ConfigureAppConfiguration</c> hook would ever run (see
/// <c>CatalogAdminApiFixture</c>'s remarks for the same gotcha). Three of them
/// (<c>AdminCompatibilityRulesApiFixture</c>, <c>BuildListsApiFixture</c>,
/// <c>CompatibilityChecksApiFixture</c>) then cleared every overridden key to <c>null</c>
/// unconditionally instead of restoring it — which deletes CI's own job-level
/// <c>ConnectionStrings__DefaultConnection</c> from the test process outright. Since this
/// assembly runs with <c>DisableTestParallelization = true</c>, a later fixture in the same
/// process (or the plain <c>WebApplicationFactory</c> default used by any test class that
/// doesn't set an override itself) would then read a missing connection string and fall back to
/// the Windows-only ".\SQL2025" default baked into appsettings — which doesn't exist on the
/// Linux CI runner, producing "Error Locating Server/Instance Specified".
/// </remarks>
internal sealed class EnvironmentOverrideScope : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> _previousValues;
    private bool _disposed;

    public EnvironmentOverrideScope(IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        _previousValues = overrides.Keys
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);

        foreach (var (key, value) in overrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (key, value) in _previousValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        _disposed = true;
    }
}
