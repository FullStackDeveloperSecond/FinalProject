using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// The CORS allowed-origin list is configuration-driven (Cors:AllowedOrigins) precisely so the
/// two Vue dev-server ports never become a de facto production policy — see the comment above
/// the CORS registration in Program.cs. This proves the closed-by-default behavior: with no
/// Cors:AllowedOrigins configured and the environment not Development, a request carrying the
/// dev origin gets no Access-Control-Allow-Origin header back.
/// </summary>
public sealed class CorsPolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsPolicyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WhenProductionWithNoCorsOriginsConfigured_DoesNotAllowTheDevOrigin()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/support-tickets");
        request.Headers.Add("Origin", "http://localhost:5173");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Get_WhenDevelopment_AllowsTheDevOriginByDefault()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/support-tickets");
        request.Headers.Add("Origin", "http://localhost:5173");

        using var response = await client.SendAsync(request);

        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
