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

    /// <summary>Scoped to this return's own shipment — a bare, unscoped count over
    /// ReturnShipmentEvents would false-fail/false-pass depending on execution order, since this
    /// fixture's database is shared across every test in the collection.</summary>
    private async Task AssertNoShipmentEventsAsync(Guid returnPublicId)
    {
        await using var context = _fixture.CreateScopedContext();
        var eventCount = await context.Set<ReturnShipmentEvent>()
            .Join(
                context.Set<ReturnShipment>().Join(
                    context.ReturnRequests.Where(r => r.PublicId == returnPublicId),
                    s => s.ReturnRequestId, r => r.Id, (s, _) => s),
                e => e.ReturnShipmentId, s => s.Id, (e, _) => e)
            .CountAsync();
        Assert.Equal(0, eventCount);
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
            // Exactly one past the real DB column length (64) — not the old, looser DTO limit —
            // per the review's explicit "test the actual DB boundary" requirement.
            reasonCode = new string('x', 65),
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

    /// <summary>
    /// alex 2026-09-05 #111 review P1：admin-web 為了不讓 JavaScript 浮點數靜默改寫可信的
    /// 退款輸入（見 #109／#111），把 <c>returnShippingCost</c> 當原始 decimal 字串送出
    /// （例如 <c>"1.01"</c>），符合 generated contract 的 <c>number | string</c>。這裡直接
    /// 送原始 JSON（而不是強型別的 C# 物件——那樣 System.Text.Json 會把 decimal 序列化成
    /// JSON number，測不到前端真正送出的形狀）驗證 <c>JsonNumberHandling</c> 有正確加在
    /// <see cref="DoSelect.Application.Returns.ApproveReturnRequest.ReturnShippingCost"/> 上，
    /// 不會在模型繫結時就 400。
    /// </summary>
    [Fact]
    public async Task Review_ReturnShippingCostAsJsonString_ModelBindsAndCreatesRefund()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);

        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/actions/review",
            $$"""
            {
              "approved": true,
              "items": [{"returnItemPublicId":"{{itemIds[0]}}","approvedQuantity":1,"inspectionRequired":false}],
              "reasonCode": "eligible",
              "assemblyFeeDisposition": "notApplicable",
              "returnShippingCost": "1.01",
              "returnRowVersion": "{{Convert.ToBase64String(rowVersion)}}"
            }
            """);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)response.StatusCode}: {body}");

        await using var context = _fixture.CreateScopedContext();
        var returnRequestId = await context.ReturnRequests
            .Where(r => r.PublicId == returnPublicId).Select(r => r.Id).SingleAsync();
        var refundCreated = await context.Refunds.AnyAsync(r => r.ReturnRequestId == returnRequestId);
        Assert.True(refundCreated, "The skip-shipment review path must have staged a Refund.");
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
            // Exactly one past the real ReturnStatusHistories.Note column length (500).
            note = new string('n', 501),
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

    /// <summary>見 Review_ReturnShippingCostAsJsonString_ModelBindsAndCreatesRefund 的說明——
    /// 同一個原因，驗證同一件事發生在 Inspect 這條路徑（<c>InspectReturnRequest</c>）。</summary>
    [Fact]
    public async Task Inspect_ReturnShippingCostAsJsonString_ModelBindsAndCreatesRefund()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.Received);

        var response = await PostAdminRawJsonAsync(
            client, $"/api/v1/admin/returns/{returnPublicId}/actions/inspect",
            $$"""
            {
              "items": [{"returnItemPublicId":"{{itemIds[0]}}","conditionCode":"Unopened","disposition":"resellable","note":null}],
              "assemblyFeeDisposition": "notApplicable",
              "returnShippingCost": "1.01",
              "returnRowVersion": "{{Convert.ToBase64String(rowVersion)}}"
            }
            """);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)response.StatusCode}: {body}");

        await using var context = _fixture.CreateScopedContext();
        var returnRequestId = await context.ReturnRequests
            .Where(r => r.PublicId == returnPublicId).Select(r => r.Id).SingleAsync();
        var refundCreated = await context.Refunds.AnyAsync(r => r.ReturnRequestId == returnRequestId);
        Assert.True(refundCreated, "The Inspect path must have staged a Refund.");
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
        await AssertNoShipmentEventsAsync(returnPublicId);
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
        await AssertNoShipmentEventsAsync(returnPublicId);
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
        await AssertNoShipmentEventsAsync(returnPublicId);
    }

    [Fact]
    public async Task ShipmentEvent_SourceExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events", new
        {
            // Exactly one past the real ReturnShipmentEvents.Source column length (32).
            source = new string('s', 33),
            externalEventId = "evt-1",
            eventType = "InTransit",
            occurredAtUtc = DateTime.UtcNow,
            description = (string?)null,
        });

        await AssertValidationFailedAsync(response);
        await AssertNoShipmentEventsAsync(returnPublicId);
    }

    [Fact]
    public async Task ShipmentEvent_ExternalEventIdExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events", new
        {
            source = "carrier",
            // Exactly one past the real ReturnShipmentEvents.ExternalEventId column length (128).
            externalEventId = new string('e', 129),
            eventType = "InTransit",
            occurredAtUtc = DateTime.UtcNow,
            description = (string?)null,
        });

        await AssertValidationFailedAsync(response);
        await AssertNoShipmentEventsAsync(returnPublicId);
    }

    [Fact]
    public async Task ShipmentEvent_DescriptionExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, _) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events", new
        {
            source = "carrier",
            externalEventId = "evt-1",
            eventType = "InTransit",
            occurredAtUtc = DateTime.UtcNow,
            // Exactly one past the real ReturnShipmentEvents.Description column length (500).
            description = new string('d', 501),
        });

        await AssertValidationFailedAsync(response);
        await AssertNoShipmentEventsAsync(returnPublicId);
    }

    // ---- Create shipment ----

    [Fact]
    public async Task CreateShipment_CarrierCodeExceedsMaxLength_Returns400ValidationFailedAndDoesNotMutate()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment", new
        {
            method = "SelfShip",
            // Exactly one past the real ReturnShipments.CarrierCode column length (32).
            carrierCode = new string('c', 33),
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertValidationFailedAsync(response);
        await AssertReturnUnchangedAsync(returnPublicId, rowVersion);
        await using var context = _fixture.CreateScopedContext();
        var returnRequestId = await context.ReturnRequests.Where(r => r.PublicId == returnPublicId).Select(r => r.Id).SingleAsync();
        var shipmentCount = await context.Set<ReturnShipment>().CountAsync(s => s.ReturnRequestId == returnRequestId);
        Assert.Equal(0, shipmentCount);
    }
}
