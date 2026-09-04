using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.IntegrationTests.Orders;
using DoSelect.Api.Security;
using DoSelect.Application.Shipping;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Shipping;

/// <summary>
/// M-11 物流狀態命令的 HTTP 層（組長 2026-09-04 裁定 A1／C1／E1）：Idempotency-Key header、
/// 授權、錯誤碼、重播與 payload conflict，以及訂單明細上的物流摘要。交易規則在
/// DoSelect.Infrastructure.Tests.Shipping.EfShipmentStatusServiceTests。
/// </summary>
[Collection(nameof(ShippingApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ShipmentStatusApiTests
{
    private readonly ShippingApiFixture _fixture;

    public ShipmentStatusApiTests(ShippingApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InTransit_ReturnsTheUpdatedAdminOrderWithShipmentSummaryAndAvailableActions()
    {
        var seed = await SeedShippedOrderAsync();
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);

        using var response = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, Guid.NewGuid().ToString("N"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(seed.OrderPublicId, body.GetProperty("publicId").GetGuid());
        Assert.Equal("InTransit", body.GetProperty("fulfillmentStatus").GetString());
        var shipment = body.GetProperty("shipment");
        Assert.Equal(seed.ShipmentPublicId, shipment.GetProperty("publicId").GetGuid());
        Assert.Equal("InTransit", shipment.GetProperty("status").GetString());
        Assert.Equal("home-delivery", shipment.GetProperty("shippingMethodCode").GetString());
        Assert.Equal(
            [ShipmentStatusActions.Delivered, ShipmentStatusActions.DeliveryFailed],
            shipment.GetProperty("availableActions").EnumerateArray().Select(a => a.GetString()!).ToArray());
        Assert.Contains(shipment.GetProperty("history").EnumerateArray(), h => h.GetProperty("toStatus").GetString() == "InTransit");
        Assert.NotEqual(Convert.ToBase64String(seed.RowVersion), shipment.GetProperty("rowVersion").GetString());

        // C1：GET /admin/orders/{id} 帶同一份摘要。
        using var detail = await client.GetAsync($"/api/v1/admin/orders/{seed.OrderPublicId}");
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InTransit", detailBody.GetProperty("shipment").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Replay_SameKeyReturnsTheSameResult_DifferentPayloadConflicts()
    {
        var seed = await SeedShippedOrderAsync();
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);
        var key = Guid.NewGuid().ToString("N");

        using var first = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        using var replay = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            firstBody.GetProperty("shipment").GetProperty("rowVersion").GetString(),
            replayBody.GetProperty("shipment").GetProperty("rowVersion").GetString());

        using var conflict = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, key, note: "different");
        var (status, code, _) = await ShippingApiFixture.ReadProblemAsync(conflict);
        Assert.Equal(409, status);
        Assert.Equal("idempotency_payload_conflict", code);

        await using var context = _fixture.CreateScopedContext();
        Assert.Equal(1, await context.ShipmentStatusHistories.AsNoTracking()
            .CountAsync(h => h.ToStatus == FulfillmentStatus.InTransit && h.ShipmentId == seed.ShipmentId));
    }

    [Fact]
    public async Task WithoutIdempotencyKey_Returns400ValidationFailed()
    {
        var seed = await SeedShippedOrderAsync();
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);

        using var response = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, idempotencyKey: null);

        var (status, code, _) = await ShippingApiFixture.ReadProblemAsync(response);
        Assert.Equal(400, status);
        Assert.Equal("validation_failed", code);
    }

    [Fact]
    public async Task InvalidTransitionAndMissingReason_ReturnCataloguedErrors()
    {
        var seed = await SeedShippedOrderAsync();
        using var client = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.OrderManager);

        using var invalid = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.Delivered, seed.RowVersion, Guid.NewGuid().ToString("N"));
        var (invalidStatus, invalidCode, _) = await ShippingApiFixture.ReadProblemAsync(invalid);
        Assert.Equal(409, invalidStatus);
        Assert.Equal(ShippingErrorCodes.ShippingStatusTransitionInvalid, invalidCode);

        using var inTransit = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, Guid.NewGuid().ToString("N"));
        var rowVersion = Convert.FromBase64String((await inTransit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shipment").GetProperty("rowVersion").GetString()!);
        using var missingReason = await PostActionAsync(client, seed.ShipmentPublicId, ShipmentStatusActions.DeliveryFailed, rowVersion, Guid.NewGuid().ToString("N"));
        var (reasonStatus, reasonCode, _) = await ShippingApiFixture.ReadProblemAsync(missingReason);
        Assert.Equal(400, reasonStatus);
        Assert.Equal("validation_failed", reasonCode);

        using var unknownAction = await PostActionAsync(client, seed.ShipmentPublicId, "teleported", rowVersion, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownAction.StatusCode);

        using var unknownShipment = await PostActionAsync(client, Guid.NewGuid(), ShipmentStatusActions.InTransit, rowVersion, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, unknownShipment.StatusCode);
    }

    [Fact]
    public async Task AnonymousAndWrongRole_AreRejected()
    {
        var seed = await SeedShippedOrderAsync();

        using var anonymous = _fixture.CreateClient();
        using var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/v1/admin/shipments/{seed.ShipmentPublicId}/actions/in-transit", new { shipmentRowVersion = seed.RowVersion });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var catalogManager = await _fixture.CreateAuthenticatedAdminClientAsync(DoSelectRoles.CatalogManager);
        using var forbidden = await PostActionAsync(catalogManager, seed.ShipmentPublicId, ShipmentStatusActions.InTransit, seed.RowVersion, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await using var context = _fixture.CreateScopedContext();
        Assert.Equal(FulfillmentStatus.Shipped, (await context.Shipments.AsNoTracking().SingleAsync(s => s.Id == seed.ShipmentId)).Status);
    }

    private static Task<HttpResponseMessage> PostActionAsync(
        HttpClient client, Guid shipmentPublicId, string action, byte[] rowVersion, string? idempotencyKey, string? reasonCode = null, string? note = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/shipments/{shipmentPublicId}/actions/{action}")
        {
            Content = JsonContent.Create(new { shipmentRowVersion = rowVersion, reasonCode, note }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return ShippingApiFixture.SendWithAntiforgeryAsync(client, request);
    }

    private sealed record SeededShipment(Guid OrderPublicId, Guid ShipmentPublicId, long ShipmentId, byte[] RowVersion);

    /// <summary>Processing、已付款、已出貨（Shipped）的宅配訂單與它的物流單。</summary>
    private async Task<SeededShipment> SeedShippedOrderAsync()
    {
        await using var context = _fixture.CreateScopedContext();
        var now = DateTime.UtcNow;
        var providerId = await AdminOrdersApiSeeding.SeedShippingProviderProfileAsync(context);
        var method = await context.ShippingMethods.SingleOrDefaultAsync(candidate => candidate.Code == "home-delivery");
        if (method is null)
        {
            method = new ShippingMethod(
                Guid.CreateVersion7(), "home-delivery", "宅配", ShippingMethodKinds.HomeDelivery,
                baseFee: 0m, freeShippingThreshold: null, allowsCod: true, requiresPrepayment: false,
                providerCode: "TCAT", createdAtUtc: now);
            context.ShippingMethods.Add(method);
            await context.SaveChangesAsync();
        }

        var order = await AdminOrdersApiSeeding.SeedOrderAsync(
            context, providerId, OrderStatus.Processing, PaymentStatus.Paid, FulfillmentStatus.Shipped);
        var shipment = new Shipment(
            Guid.CreateVersion7(), order.Id, method.Id, providerId, convenienceStoreId: null,
            $"SH{Guid.NewGuid():N}"[..20], order.ShippingFee, now);
        shipment.SetTrackingNumber($"TRK{Guid.NewGuid():N}"[..20], now);
        shipment.ChangeStatus(FulfillmentStatus.Preparing, now);
        shipment.ChangeStatus(FulfillmentStatus.Shipped, now);
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();
        return new SeededShipment(order.PublicId, shipment.PublicId, shipment.Id, shipment.RowVersion);
    }
}
