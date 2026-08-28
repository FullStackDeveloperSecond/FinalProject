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

    [Fact]
    public async Task GetOrder_WhenAnonymous_ReturnsUnauthorized()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
