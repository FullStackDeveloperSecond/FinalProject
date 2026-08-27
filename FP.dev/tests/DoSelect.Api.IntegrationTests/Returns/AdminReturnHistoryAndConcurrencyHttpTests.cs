using System.Net;
using System.Net.Http.Json;
using DoSelect.Domain.Returns;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

[Collection(nameof(ReturnsApiCollection))]
public sealed class AdminReturnHistoryAndConcurrencyHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public AdminReturnHistoryAndConcurrencyHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    private static async Task<HttpResponseMessage> PostAdminAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        return await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
    }

    [Fact]
    public async Task Review_Approval_PersistsEveryLegalEdgeWithOneActorAndTimestamp()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (publicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);
        var beforeUtc = DateTime.UtcNow.AddSeconds(-1);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{publicId}/actions/review", new
        {
            approved = true,
            items = new[] { new { returnItemPublicId = itemIds[0], approvedQuantity = 1, inspectionRequired = true } },
            reasonCode = "eligible",
            note = "approved",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertOkAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == publicId);
        var history = await LoadHistoryAsync(context, request.Id);
        AssertEdges(history,
            (ReturnRequestStatus.Requested, ReturnRequestStatus.UnderReview),
            (ReturnRequestStatus.UnderReview, ReturnRequestStatus.Approved),
            (ReturnRequestStatus.Approved, ReturnRequestStatus.AwaitingShipment));
        await AssertActorAndTimeAsync(context, history, beforeUtc);
    }

    [Fact]
    public async Task Receive_FromAwaitingShipment_PersistsInTransitAndReceivedEdges()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (publicId, rowVersion, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment);
        var beforeUtc = DateTime.UtcNow.AddSeconds(-1);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{publicId}/actions/receive", new
        {
            note = "warehouse received",
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertOkAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == publicId);
        var history = await LoadHistoryAsync(context, request.Id);
        AssertEdges(history,
            (ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.InTransit),
            (ReturnRequestStatus.InTransit, ReturnRequestStatus.Received));
        await AssertActorAndTimeAsync(context, history, beforeUtc);
    }

    [Fact]
    public async Task Inspect_FromReceived_PersistsInspectingAndAwaitingRefundEdges()
    {
        var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (publicId, rowVersion, itemIds, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Received);
        var beforeUtc = DateTime.UtcNow.AddSeconds(-1);

        var response = await PostAdminAsync(client, $"/api/v1/admin/returns/{publicId}/actions/inspect", new
        {
            items = new[]
            {
                new { returnItemPublicId = itemIds[0], conditionCode = "Unopened", disposition = "resellable", note = "ok" },
            },
            assemblyFeeDisposition = "notApplicable",
            returnShippingCost = 0m,
            returnRowVersion = Convert.ToBase64String(rowVersion),
        });

        await AssertOkAsync(response);
        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == publicId);
        var history = await LoadHistoryAsync(context, request.Id);
        AssertEdges(history,
            (ReturnRequestStatus.Received, ReturnRequestStatus.Inspecting),
            (ReturnRequestStatus.Inspecting, ReturnRequestStatus.AwaitingRefund));
        await AssertActorAndTimeAsync(context, history, beforeUtc);
    }

    [Fact]
    public async Task ShipmentEvents_DifferentConcurrentEvents_BothPersistAndStateRemainsMonotonic()
    {
        var firstClient = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var secondClient = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (publicId, _, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);
        var pickedUpAtUtc = TruncateToMilliseconds(DateTime.UtcNow.AddMinutes(-2));
        var deliveredAtUtc = pickedUpAtUtc.AddMinutes(1);
        var source = $"carrier-{Guid.NewGuid():N}"[..32];

        var first = PostAdminAsync(firstClient, $"/api/v1/admin/returns/{publicId}/shipment/events", new
        {
            source,
            externalEventId = $"picked-{Guid.NewGuid():N}",
            eventType = "PickedUp",
            occurredAtUtc = pickedUpAtUtc,
            description = "picked up",
        });
        var second = PostAdminAsync(secondClient, $"/api/v1/admin/returns/{publicId}/shipment/events", new
        {
            source,
            externalEventId = $"delivered-{Guid.NewGuid():N}",
            eventType = "Delivered",
            occurredAtUtc = deliveredAtUtc,
            description = "delivered",
        });

        var responses = await Task.WhenAll(first, second);
        foreach (var response in responses)
        {
            await AssertOkAsync(response);
        }

        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == publicId);
        var shipment = await context.ReturnShipments.AsNoTracking().SingleAsync(s => s.ReturnRequestId == request.Id);
        var events = await context.ReturnShipmentEvents.AsNoTracking()
            .Where(e => e.ReturnShipmentId == shipment.Id)
            .ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(ReturnShipmentStatus.Delivered, shipment.Status);
        Assert.Equal(ReturnRequestStatus.Received, request.Status);
        var history = await LoadHistoryAsync(context, request.Id);
        AssertEdges(history,
            (ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.InTransit),
            (ReturnRequestStatus.InTransit, ReturnRequestStatus.Received));
    }

    [Fact]
    public async Task ShipmentEvents_SameConcurrentEvent_IsIdempotentAndPersistsOnce()
    {
        var firstClient = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var secondClient = await _fixture.CreateAuthenticatedOrderManagerClientAsync();
        var (publicId, _, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.AwaitingShipment, withShipment: true);
        var occurredAtUtc = TruncateToMilliseconds(DateTime.UtcNow.AddMinutes(-1));
        var source = $"carrier-{Guid.NewGuid():N}"[..32];
        var externalEventId = $"same-{Guid.NewGuid():N}";
        var body = new
        {
            source,
            externalEventId,
            eventType = "InTransit",
            occurredAtUtc,
            description = "in transit",
        };

        var responses = await Task.WhenAll(
            PostAdminAsync(firstClient, $"/api/v1/admin/returns/{publicId}/shipment/events", body),
            PostAdminAsync(secondClient, $"/api/v1/admin/returns/{publicId}/shipment/events", body));
        foreach (var response in responses)
        {
            await AssertOkAsync(response);
        }

        await using var context = _fixture.CreateScopedContext();
        var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.PublicId == publicId);
        var shipment = await context.ReturnShipments.AsNoTracking().SingleAsync(s => s.ReturnRequestId == request.Id);
        Assert.Equal(1, await context.ReturnShipmentEvents.AsNoTracking()
            .CountAsync(e => e.ReturnShipmentId == shipment.Id && e.Source == source && e.ExternalEventId == externalEventId));
        Assert.Equal(ReturnShipmentStatus.InTransit, shipment.Status);
        Assert.Equal(ReturnRequestStatus.InTransit, request.Status);
        var history = await LoadHistoryAsync(context, request.Id);
        AssertEdges(history, (ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.InTransit));
    }

    private static async Task<List<ReturnStatusHistory>> LoadHistoryAsync(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context,
        long returnRequestId) =>
        await context.ReturnStatusHistories.AsNoTracking()
            .Where(h => h.ReturnRequestId == returnRequestId)
            .OrderBy(h => h.Id)
            .ToListAsync();

    private static void AssertEdges(
        IReadOnlyList<ReturnStatusHistory> history,
        params (ReturnRequestStatus From, ReturnRequestStatus To)[] expected)
    {
        Assert.Equal(expected.Length, history.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].From, history[index].FromStatus);
            Assert.Equal(expected[index].To, history[index].ToStatus);
        }
    }

    private static async Task AssertActorAndTimeAsync(
        DoSelect.Infrastructure.Persistence.DoSelectDbContext context,
        IReadOnlyList<ReturnStatusHistory> history,
        DateTime beforeUtc)
    {
        var actor = Assert.Single(history.Select(h => h.ActorUserId).Distinct());
        Assert.False(string.IsNullOrWhiteSpace(actor));
        Assert.True(await context.Users.AsNoTracking().AnyAsync(u => u.Id == actor));
        var occurredAtUtc = Assert.Single(history.Select(h => h.OccurredAtUtc).Distinct());
        Assert.InRange(occurredAtUtc, beforeUtc, DateTime.UtcNow.AddSeconds(1));
    }

    private static async Task AssertOkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {body}");
    }

    private static DateTime TruncateToMilliseconds(DateTime value) =>
        new(value.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
}
