using System.Net;
using System.Text.Json;

namespace DoSelect.Api.IntegrationTests.Imports;

/// <summary>
/// 組長 PR #74 round-2 review (P2): the rows endpoint's pageSize is optional in the OpenAPI
/// contract, but the controller's non-nullable int bound an omitted value to 0 and the service's
/// 1–200 range check then rejected a perfectly normal GET. These run the full HTTP pipeline —
/// model binding included, which is exactly the layer the bug lived in.
/// </summary>
[Collection(nameof(ProductImportsApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductImportsApiTests
{
    private readonly ProductImportsApiFixture _fixture;

    public ProductImportsApiTests(ProductImportsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetRows_WithoutPageSize_UsesTheDefaultAndReturnsTheStagedRows()
    {
        var client = await _fixture.CreateAuthenticatedCatalogManagerClientAsync();
        var batchId = await _fixture.StagePreviewBatchAsync(client);

        using var response = await client.GetAsync($"/api/v1/admin/product-imports/{batchId}/rows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetRows_WithAnExplicitPageSizeOfZero_IsStillAValidationError()
    {
        var client = await _fixture.CreateAuthenticatedCatalogManagerClientAsync();
        var batchId = await _fixture.StagePreviewBatchAsync(client);

        using var response = await client.GetAsync($"/api/v1/admin/product-imports/{batchId}/rows?pageSize=0");

        // Omission means "use the default"; an explicit 0 is a caller mistake and must keep
        // failing the service's 1–200 range validation.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }
}
