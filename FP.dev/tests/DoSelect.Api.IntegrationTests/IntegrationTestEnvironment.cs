using System.Runtime.CompilerServices;

namespace DoSelect.Api.IntegrationTests;

internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    internal static void ConfigureRequiredSyntheticSecrets()
    {
        const string key = "GuestOrderAccess__Pepper";
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(
                key,
                "synthetic-integration-test-pepper-at-least-32-bytes");
        }
    }
}
