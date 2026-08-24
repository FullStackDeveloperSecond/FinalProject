using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// DevSignInEndpoints exists only so a human can click through the customer-web UI before
/// haru's real login flow ships (see DevSignInEndpoints.cs). It is mapped only inside
/// `if (app.Environment.IsDevelopment())`, but that guard is only as good as this test proving
/// it — mirrors the same Production-environment pattern already used by OpenApiEndpointTests.
/// </summary>
public sealed class DevSignInEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DevSignInEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostTestSignIn_WhenDevelopment_Succeeds()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/dev/test-sign-in",
            new { email = "dev-sign-in-smoke-test@doselect.local" });

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/api/v1/dev/test-sign-in")]
    [InlineData("/api/v1/dev/test-sign-out")]
    public async Task PostDevSignInRoutes_WhenProduction_DoesNotExist(string path)
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
