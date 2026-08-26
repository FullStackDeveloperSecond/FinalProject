using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// Regression guard for the P1 defect where the Admin Return DTOs' StringLength attributes
/// existed and were enforced by MVC ModelState, but never made it into the generated OpenAPI
/// document — the native OpenApi generator only reads DataAnnotations from record PROPERTIES,
/// and a record with a primary constructor either drops that metadata from the schema entirely
/// (bare parameter attributes) or throws at request time the moment any property-targeted
/// metadata exists alongside it. Fetches the real, live-generated /openapi/v1.json (not the
/// committed contracts/openapi.v1.json file) so this fails the moment the DTOs regress back to a
/// shape the generator can't read, independent of whether anyone remembered to re-export.
/// </summary>
public sealed class ReturnDtosOpenApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReturnDtosOpenApiContractTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task OpenApiDocument_AdminReturnDtoStringFields_DeclareMaxLengthMatchingTheDatabaseColumns()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        // (schema, property, expected maxLength) — one row per field this review round flagged
        // as missing from the schema despite being enforced at request time.
        (string Schema, string Property, int MaxLength)[] expectations =
        [
            ("ApproveReturnRequest", "reasonCode", 64),
            ("ApproveReturnRequest", "note", 500),
            ("ReceiveReturnRequest", "note", 500),
            ("ExtendShipmentDeadlineRequest", "reasonCode", 64),
            ("CreateReturnShipmentRequest", "carrierCode", 32),
            ("AppendReturnShipmentEventRequest", "source", 32),
            ("AppendReturnShipmentEventRequest", "externalEventId", 128),
            ("AppendReturnShipmentEventRequest", "description", 500),
        ];

        foreach (var (schemaName, propertyName, expectedMaxLength) in expectations)
        {
            Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"Schema '{schemaName}' was not found in the OpenAPI document.");
            Assert.True(schema.TryGetProperty("properties", out var properties), $"Schema '{schemaName}' has no 'properties'.");
            Assert.True(properties.TryGetProperty(propertyName, out var property), $"Schema '{schemaName}' has no property '{propertyName}'.");
            Assert.True(
                property.TryGetProperty("maxLength", out var maxLength),
                $"'{schemaName}.{propertyName}' is missing 'maxLength' in the generated OpenAPI document.");
            Assert.Equal(expectedMaxLength, maxLength.GetInt32());
        }
    }

    /// <summary>
    /// The other half of the same defect class: converting these DTOs off positional records
    /// also silently dropped every non-nullable field's "required" status (a positional
    /// constructor parameter with no C# default is implicitly required; a plain property-init
    /// property is not, unless it explicitly carries the `required` modifier). Guards that the
    /// `required` set was restored to match, not just the maxLength facets.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_AdminReturnDtoNonNullableFields_StayMarkedRequired()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        (string Schema, string[] Required)[] expectations =
        [
            ("ApproveReturnRequest", ["approved", "items", "reasonCode", "returnRowVersion"]),
            ("ReceiveReturnRequest", ["returnRowVersion"]),
            ("ExtendShipmentDeadlineRequest", ["reasonCode", "returnRowVersion"]),
            ("CreateReturnShipmentRequest", ["method", "returnRowVersion"]),
            ("AppendReturnShipmentEventRequest", ["source", "externalEventId", "eventType", "occurredAtUtc"]),
        ];

        foreach (var (schemaName, expectedRequired) in expectations)
        {
            var schema = schemas.GetProperty(schemaName);
            var actualRequired = schema.TryGetProperty("required", out var requiredElement)
                ? requiredElement.EnumerateArray().Select(e => e.GetString()).ToHashSet()
                : [];
            foreach (var expected in expectedRequired)
            {
                Assert.True(actualRequired.Contains(expected), $"'{schemaName}' is missing '{expected}' from its 'required' set.");
            }
        }
    }
}
