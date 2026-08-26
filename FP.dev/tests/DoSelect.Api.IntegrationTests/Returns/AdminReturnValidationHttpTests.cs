using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoSelect.Domain.Returns;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// D1 review finding: the Admin Return action DTOs' DataAnnotations (added to ReturnDtos.cs)
/// were only verified by build/OpenAPI-generation, never by an actual HTTP request through the
/// real [ApiController] ModelState pipeline. These tests hit the real endpoints with malformed
/// bodies and assert the full contract: HTTP 400, error code "validation_failed" (never 500),
/// and — for the request line — that nothing was written to ReturnRequests, ReturnInspections,
/// ReturnShipmentEvents or ReturnStatusHistories.
/// </summary>
[Collection(nameof(ReturnsApiCollection))]
public sealed class AdminReturnValidationHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public AdminReturnValidationHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    private async Task<HttpResponseMessage> PostAdminAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        return await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
    }

    private async Task<HttpResponseMessage> PostAdminRawJsonAsync(HttpClient client, string path, string rawJson)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json"),
        };
        return await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
    }

    private static async Task AssertValidationFailedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 but received {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var code = document.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : document.RootElement.GetProperty("errors").ValueKind == JsonValueKind.Object ? "validation_failed" : null;
        Assert.Equal("validation_failed", code);
    }

    private async Task AssertReturnUnchangedAsync(Guid returnPublicId, byte[] originalRowVersion)
    {
        await using var context = _fixture.CreateScopedContext();
        var current = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == returnPublicId);
        Assert.Equal(originalRowVersion, current.RowVersion);
        var historyCount = await context.ReturnStatusHistories.CountAsync(h => h.ReturnRequestId == current.Id);
        Assert.Equal(0, historyCount);
        var inspectionCount = await context.Set<ReturnInspection>()
            .Join(context.ReturnItems.Where(i => i.ReturnRequestId == current.Id), insp => insp.ReturnItemId, i => i.Id, (insp, _) => insp)
            .CountAsync();
        Assert.Equal(0, inspectionCount);
    }

    // ---- Review (Approve) ----

    [Fact]
    public async Task Review_MissingRequiredReasonCode_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/actions/review",
            $$"""{"approved":true,"items":[{"returnItemPublicId":"{{itemIds[0]}}","approvedQuantity":1,"inspectionRequired":true}],"returnRowVersion":"{{Convert.ToBase64String(rowVersion)}}"}""");

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Review_EmptyReasonCode_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = true,
            items = new[] { new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true } },
            reasonCode = "",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Review_ReasonCodeExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = true,
            items = new[] { new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true } },
            reasonCode = new string('x', 101),
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Review_EmptyItemsCollection_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = true,
            items = Array.Empty<object>(),
            reasonCode = "eligible",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Review_DuplicateItem_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested, itemCount: 2);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = true,
            items = new[]
            {
                new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true },
                new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true },
            },
            reasonCode = "eligible",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Review_InvalidRowVersionLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = true,
            items = new[] { new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true } },
            reasonCode = "eligible",
            returnRowVersion = Convert.ToBase64String([1, 2, 3]),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    // ---- Receive ----

    [Fact]
    public async Task Receive_InvalidRowVersionLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/receive", new
        {
            note = (string?)null,
            returnRowVersion = Convert.ToBase64String([1, 2, 3, 4]),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Receive_NoteExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/receive", new
        {
            note = new string('n', 1001),
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    // ---- Inspect ----

    [Fact]
    public async Task Inspect_OmittedItem_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Received, itemCount: 2);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/inspect", new
        {
            items = new[] { new { returnItemPublicId = itemIds[0], conditionCode = "Unopened", disposition = "resellable", note = (string?)null } },
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Inspect_DuplicateItem_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Received, itemCount: 2);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/inspect", new
        {
            items = new[]
            {
                new { returnItemPublicId = itemIds[0], conditionCode = "Unopened", disposition = "resellable", note = (string?)null },
                new { returnItemPublicId = itemIds[0], conditionCode = "Unopened", disposition = "resellable", note = (string?)null },
            },
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    [Fact]
    public async Task Inspect_EmptyItemsCollection_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Received);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/inspect", new
        {
            items = Array.Empty<object>(),
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    // ---- Extend shipment deadline ----

    [Fact]
    public async Task Extend_MissingRequiredReasonCode_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment);

        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/actions/extend-shipment-deadline",
            $$"""{"returnRowVersion":"{{Convert.ToBase64String(rowVersion)}}"}""");

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
    }

    // ---- Shipment events ----

    [Fact]
    public async Task ShipmentEvent_NonUtcOccurredAtUtc_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        // No 'Z'/offset suffix — System.Text.Json deserializes this as DateTimeKind.Unspecified.
        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events",
            """{"source":"carrier","externalEventId":"evt-1","eventType":"InTransit","occurredAtUtc":"2026-01-01T00:00:00","description":null}""");

        await AssertValidationFailedAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var eventCount = await context.Set<ReturnShipmentEvent>().CountAsync();
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task ShipmentEvent_MissingRequiredSource_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events",
            """{"externalEventId":"evt-1","eventType":"InTransit","occurredAtUtc":"2026-01-01T00:00:00Z","description":null}""");

        await AssertValidationFailedAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var eventCount = await context.Set<ReturnShipmentEvent>().CountAsync();
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task ShipmentEvent_EventTypeExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events", new
        {
            source = "carrier",
            externalEventId = "evt-1",
            eventType = new string('e', 51),
            occurredAtUtc = DateTime.UtcNow,
            description = (string?)null,
        });

        await AssertValidationFailedAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var eventCount = await context.Set<ReturnShipmentEvent>().CountAsync();
        Assert.Equal(0, eventCount);
    }
}
