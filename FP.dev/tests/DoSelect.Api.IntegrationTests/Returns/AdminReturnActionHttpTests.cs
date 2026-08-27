using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Returns;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// PR #42 latest-review P1 fixes, verified through the real HTTP pipeline against a real SQL
/// Server database — not just Application-layer fakes.
/// </summary>
[Collection(nameof(ReturnsApiCollection))]
public sealed class AdminReturnActionHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public AdminReturnActionHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    private async Task<HttpResponseMessage> PostAdminAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        return await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
    }

    /// <summary>P1: rejecting a return with an empty items array must succeed, not 400 — the
    /// previous [MinLength(1)] DTO attribute blocked the exact payload the Admin Web rejection
    /// flow legitimately sends.</summary>
    [Fact]
    public async Task Review_RejectWithEmptyItems_SucceedsAndPersistsRejection()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested, itemCount: 2);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/actions/review", new
        {
            approved = false,
            items = Array.Empty<object>(),
            reasonCode = "not-eligible",
            note = "已超過鑑賞期",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)response.StatusCode}: {body}");

        await using var context = _fixture.CreateScopedContext();
        var persisted = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == returnPublicId);
        Assert.Equal(ReturnRequestStatus.Rejected, persisted.Status);

        var history = await context.ReturnStatusHistories
            .Where(h => h.ReturnRequestId == persisted.Id)
            .ToListAsync();
        Assert.Collection(
            history.OrderBy(h => h.Id),
            h =>
            {
                Assert.Equal(ReturnRequestStatus.Requested, h.FromStatus);
                Assert.Equal(ReturnRequestStatus.UnderReview, h.ToStatus);
            },
            h =>
            {
                Assert.Equal(ReturnRequestStatus.UnderReview, h.FromStatus);
                Assert.Equal(ReturnRequestStatus.Rejected, h.ToStatus);
            });
        Assert.All(history, h => Assert.Equal("not-eligible", h.ReasonCode));
        Assert.All(history, h => Assert.Equal("已超過鑑賞期", h.Note));
        Assert.All(history, h => Assert.Equal(persisted.ReviewedByAdminUserId, h.ActorUserId));
        Assert.Single(history.Select(h => h.OccurredAtUtc).Distinct());

        // The two original items must be untouched — no approval/inspection data was created.
        var items = await context.ReturnItems.AsNoTracking().Where(i => i.ReturnRequestId == persisted.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("NotInspected", i.InspectionStatus));
        Assert.All(items, i => Assert.Null(i.RestockDisposition));
        var itemDbIds = items.Select(i => i.Id).ToList();
        var inspectionCount = await context.Set<ReturnInspection>()
            .CountAsync(insp => itemDbIds.Contains(insp.ReturnItemId));
        Assert.Equal(0, inspectionCount);
        var shipmentCount = await context.Set<ReturnShipment>().CountAsync(s => s.ReturnRequestId == persisted.Id);
        Assert.Equal(0, shipmentCount);
    }

    /// <summary>P1: a shipment's first-ever event being Delivered must cascade ReturnRequest
    /// through AwaitingShipment -> InTransit -> Received (the Domain's own legal sequence), not
    /// be silently dropped or attempt an illegal direct AwaitingShipment -> Received jump.</summary>
    [Fact]
    public async Task ShipmentEvent_FirstEventIsDelivered_CascadesReturnRequestThroughInTransitToReceivedAtomically()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (returnPublicId, _, _, shipmentPublicId) =
            await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);
        Assert.NotNull(shipmentPublicId);

        // Truncated to millisecond precision up front — the DB column is datetime2(3), so
        // comparing against a tick-precision DateTime.UtcNow would spuriously fail on rounding.
        var occurredAtUtc = new DateTime(DateTime.UtcNow.AddHours(-3).Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{returnPublicId}/shipment/events", new
        {
            source = "carrier",
            externalEventId = "evt-delivered-first",
            eventType = "Delivered",
            occurredAtUtc,
            description = (string?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)response.StatusCode}: {body}");

        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == returnPublicId);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);

        var shipment = await context.Set<ReturnShipment>().AsNoTracking().SingleAsync(s => s.ReturnRequestId == request.Id);
        Assert.Equal(ReturnShipmentStatus.Delivered, shipment.Status);

        var events = await context.Set<ReturnShipmentEvent>().AsNoTracking()
            .Where(e => e.ReturnShipmentId == shipment.Id)
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal("evt-delivered-first", events[0].ExternalEventId);

        var history = await context.ReturnStatusHistories
            .Where(h => h.ReturnRequestId == request.Id)
            .OrderBy(h => h.Id)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(ReturnRequestStatus.AwaitingShipment, history[0].FromStatus);
        Assert.Equal(ReturnRequestStatus.InTransit, history[0].ToStatus);
        Assert.Equal(ReturnRequestStatus.InTransit, history[1].FromStatus);
        Assert.Equal(ReturnRequestStatus.Received, history[1].ToStatus);
        // Status timestamps reflect the carrier's own OccurredAtUtc, never API-receipt time.
        Assert.Equal(occurredAtUtc, history[0].OccurredAtUtc);
        Assert.Equal(occurredAtUtc, history[1].OccurredAtUtc);
    }
}
