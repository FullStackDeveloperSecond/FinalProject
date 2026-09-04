using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Auditing;
using DoSelect.Domain.Orders;
using DoSelect.Api.IntegrationTests.Returns;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Orders;

[Collection(nameof(ReturnsApiCollection))]
public sealed class OrdersMemberHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public OrdersMemberHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CancelOrder_AsAuthenticatedMemberWithAntiforgery_PersistsStatusHistoryAndAudit()
    {
        var (client, memberUserId, orderPublicId, orderRowVersion) =
            await _fixture.CreateAuthenticatedMemberWithPendingOrderAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orders/{orderPublicId}/actions/cancel")
        {
            Content = JsonContent.Create(new
            {
                reasonCode = "ordered_by_mistake",
                note = "重複下單",
                orderRowVersion = Convert.ToBase64String(orderRowVersion),
            }),
        };

        using var response = await ReturnsApiFixture.SendWithAntiforgeryAsync(client, request);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");

        using var responseDocument = JsonDocument.Parse(responseBody);
        Assert.Equal("cancelled", responseDocument.RootElement.GetProperty("orderStatus").GetString());

        await using var context = _fixture.CreateScopedContext();
        var order = await context.Orders.SingleAsync(candidate => candidate.PublicId == orderPublicId);
        Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
        var history = await context.OrderStatusHistories.SingleAsync(candidate => candidate.OrderId == order.Id);
        Assert.Equal(memberUserId, history.ActorUserId);
        Assert.Equal("ordered_by_mistake", history.ReasonCode);
        var audit = await context.AuditLogs.SingleAsync(candidate => candidate.ResourcePublicId == orderPublicId);
        Assert.Equal(AuditActions.OrderCancel, audit.Action);
        Assert.Equal(AuditActorType.Member, audit.ActorType);
        Assert.Contains("重複下單", audit.ChangedFieldsJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// C1（組長 2026-09-04）：顧客的 GET /orders/{id} 帶物流摘要與時間歷程，但不得回傳 Actor、原因備註或
    /// 內部 ID；擁有者判斷沿用既有 Owner／Guest Scope（跨訂單 404 由既有測試覆蓋）。
    /// </summary>
    [Fact]
    public async Task GetOrder_AsOwner_ExposesShipmentSummaryWithoutActorOrInternalIds()
    {
        var (client, _, orderPublicId, _, _) = await _fixture.CreateAuthenticatedMemberWithDeliveredOrderAsync();
        Guid shipmentPublicId;
        await using (var context = _fixture.CreateScopedContext())
        {
            var order = await context.Orders.SingleAsync(candidate => candidate.PublicId == orderPublicId);
            var method = await context.ShippingMethods.SingleOrDefaultAsync(candidate => candidate.Code == order.ShippingMethodCode)
                ?? new DoSelect.Domain.Shipping.ShippingMethod(
                    Guid.CreateVersion7(), order.ShippingMethodCode, "宅配", DoSelect.Domain.Shipping.ShippingMethodKinds.HomeDelivery,
                    0m, null, allowsCod: true, requiresPrepayment: false, providerCode: "TCAT", createdAtUtc: DateTime.UtcNow);
            if (method.Id == 0)
            {
                context.ShippingMethods.Add(method);
                await context.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;
            var seededShipment = new DoSelect.Domain.Shipping.Shipment(
                Guid.CreateVersion7(), order.Id, method.Id, order.ShippingProviderProfileVersionId, null,
                $"SH{Guid.NewGuid():N}"[..20], order.ShippingFee, now.AddDays(-2));
            seededShipment.SetTrackingNumber("TRK-CUSTOMER-1", now.AddDays(-2));
            foreach (var step in new[] { FulfillmentStatus.Preparing, FulfillmentStatus.Shipped, FulfillmentStatus.InTransit, FulfillmentStatus.Delivered })
            {
                seededShipment.ChangeStatus(step, now.AddDays(-1));
            }

            context.Shipments.Add(seededShipment);
            await context.SaveChangesAsync();
            context.ShipmentStatusHistories.Add(new DoSelect.Domain.Shipping.ShipmentStatusHistory(
                Guid.CreateVersion7(), seededShipment.Id, FulfillmentStatus.InTransit, FulfillmentStatus.Delivered, null, now.AddDays(-1), actorUserId: null));
            await context.SaveChangesAsync();
            shipmentPublicId = seededShipment.PublicId;
        }

        using var response = await client.GetAsync($"/api/v1/orders/{orderPublicId}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
        using var document = JsonDocument.Parse(text);
        var shipment = document.RootElement.GetProperty("shipment");
        Assert.Equal("delivered", shipment.GetProperty("status").GetString());
        Assert.Equal("TRK-CUSTOMER-1", shipment.GetProperty("trackingNumber").GetString());
        var history = Assert.Single(shipment.GetProperty("history").EnumerateArray());
        Assert.Equal("delivered", history.GetProperty("toStatus").GetString());
        Assert.False(history.TryGetProperty("actorPublicId", out _));
        Assert.False(history.TryGetProperty("reasonCode", out _));
        Assert.False(shipment.TryGetProperty("publicId", out _));
        Assert.DoesNotContain(shipmentPublicId.ToString("D"), text);
    }

    [Fact]
    public async Task GetOrder_WhenAnonymous_ReturnsUnauthorized()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
