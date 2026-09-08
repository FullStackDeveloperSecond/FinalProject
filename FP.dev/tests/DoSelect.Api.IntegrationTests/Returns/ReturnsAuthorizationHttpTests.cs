using System.Net;
using System.Net.Http.Json;
using DoSelect.Domain.Returns;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

[Collection(nameof(ReturnsApiCollection))]
public sealed class ReturnsAuthorizationHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public ReturnsAuthorizationHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AdminRoutes_WhenRoleDoesNotGrantReturnApprove_Return403WithoutSideEffects()
    {
        var client = await _fixture.CreateAuthenticatedAdminWithoutReturnApproveClientAsync();
        var (returnPublicId, _, _, _) = await _fixture.SeedReturnAsync(
            ReturnRequestStatus.Requested, withShipment: true);

        await using var before = _fixture.CreateScopedContext();
        var original = await before.ReturnRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == returnPublicId);
        var originalRowVersion = original.RowVersion.ToArray();
        var returnRequestCount = await before.ReturnRequests.CountAsync();
        var returnItemCount = await before.ReturnItems.CountAsync();
        var inspectionCount = await before.ReturnInspections.CountAsync();
        var attachmentCount = await before.ReturnAttachments.CountAsync();
        var assignmentHistoryCount = await before.ReturnAssignmentHistories.CountAsync();
        var statusHistoryCount = await before.ReturnStatusHistories.CountAsync();
        var shipmentCount = await before.ReturnShipments.CountAsync();
        var shipmentEventCount = await before.ReturnShipmentEvents.CountAsync();
        var refundCount = await before.Refunds.CountAsync();
        var refundAllocationCount = await before.RefundAllocations.CountAsync();
        var inventoryMovementCount = await before.InventoryMovements.CountAsync();
        var auditCount = await before.AuditLogs.CountAsync();

        foreach (var path in new[]
        {
            "/api/v1/admin/returns",
            $"/api/v1/admin/returns/{returnPublicId}",
            $"/api/v1/admin/returns/{returnPublicId}/shipment",
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        foreach (var path in new[]
        {
            $"/api/v1/admin/returns/{returnPublicId}/actions/review",
            $"/api/v1/admin/returns/{returnPublicId}/actions/receive",
            $"/api/v1/admin/returns/{returnPublicId}/actions/inspect",
            $"/api/v1/admin/returns/{returnPublicId}/actions/extend-shipment-deadline",
            $"/api/v1/admin/returns/{returnPublicId}/shipment",
            $"/api/v1/admin/returns/{returnPublicId}/shipment/events",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new { }),
            };
            using var response = await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await using var verify = _fixture.CreateScopedContext();
        var reloaded = await verify.ReturnRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == original.Id);
        Assert.Equal(original.Status, reloaded.Status);
        Assert.Equal(originalRowVersion, reloaded.RowVersion);
        Assert.Equal(returnRequestCount, await verify.ReturnRequests.CountAsync());
        Assert.Equal(returnItemCount, await verify.ReturnItems.CountAsync());
        Assert.Equal(inspectionCount, await verify.ReturnInspections.CountAsync());
        Assert.Equal(attachmentCount, await verify.ReturnAttachments.CountAsync());
        Assert.Equal(assignmentHistoryCount, await verify.ReturnAssignmentHistories.CountAsync());
        Assert.Equal(statusHistoryCount, await verify.ReturnStatusHistories.CountAsync());
        Assert.Equal(shipmentCount, await verify.ReturnShipments.CountAsync());
        Assert.Equal(shipmentEventCount, await verify.ReturnShipmentEvents.CountAsync());
        Assert.Equal(refundCount, await verify.Refunds.CountAsync());
        Assert.Equal(refundAllocationCount, await verify.RefundAllocations.CountAsync());
        Assert.Equal(inventoryMovementCount, await verify.InventoryMovements.CountAsync());
        Assert.Equal(auditCount, await verify.AuditLogs.CountAsync());
    }
}
