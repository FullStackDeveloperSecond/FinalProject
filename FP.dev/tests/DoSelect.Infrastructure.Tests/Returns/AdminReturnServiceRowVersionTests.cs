using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Returns;

/// <summary>
/// Regression coverage for the P1 defect Codex flagged post-review: AdminReturnService.InspectAsync
/// called SaveTransitionAsync with the freshly-loaded returnRequest.RowVersion instead of the
/// caller's own request.ReturnRowVersion, silently defeating optimistic concurrency for the
/// inspect action specifically (Review/Receive/Extend were already correct). Uses a real SQL
/// Server so the RowVersion mismatch is enforced by the actual rowversion column, not simulated —
/// reuses ReturnStoreConcurrencyFixture/Collection/SqlServerFactAttribute from
/// ReturnStoreConcurrencyTests.cs (same disposable per-collection database).
/// </summary>
[Collection(nameof(ReturnStoreConcurrencyCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AdminReturnServiceRowVersionTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 25, 3, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task InspectAsync_WithStaleReturnRowVersion_ThrowsConcurrencyConflictAndPersistsNothing()
    {
        Guid returnPublicId;
        Guid returnItemPublicId;
        byte[] validRowVersion;
        string adminUserId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (returnPublicId, returnItemPublicId, validRowVersion, adminUserId) = await SeedReceivedReturnAsync(seed);
        }

        // Deliberately corrupt one byte so this can never coincidentally match the real
        // rowversion column — a genuinely stale value, not just "a different-looking one".
        var staleRowVersion = (byte[])validRowVersion.Clone();
        staleRowVersion[^1] ^= 0xFF;

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var store = new ReturnStore(context);
        var orderPort = new ReturnOrderEligibilityLookup(context);
        var service = new AdminReturnService(
            store, orderPort, new NoOpReturnInventoryPort(), new NoOpReturnRefundCreationPort(), TimeProvider.System);

        var inspectRequest = new InspectReturnRequest(
            [new InspectReturnItemLine(returnItemPublicId, "Unopened", RestockDisposition.Resellable, null)],
            staleRowVersion,
            AssemblyFeeDisposition.NotApplicable,
            0m);

        var exception = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            service.InspectAsync(returnPublicId, adminUserId, inspectRequest, CancellationToken.None));
        Assert.Equal(ReturnsWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        var persistedRequest = await verify.ReturnRequests.AsNoTracking()
            .SingleAsync(r => r.PublicId == returnPublicId);
        Assert.Equal(ReturnRequestStatus.Received, persistedRequest.Status);
        Assert.Equal(validRowVersion, persistedRequest.RowVersion);

        var persistedItem = await verify.ReturnItems.AsNoTracking()
            .SingleAsync(i => i.PublicId == returnItemPublicId);
        Assert.Equal("NotInspected", persistedItem.InspectionStatus);
        Assert.Null(persistedItem.RestockDisposition);

        // Scoped to this test's own ReturnItem — this database is shared across the whole test
        // collection (see ReturnStoreConcurrencyCollection), so an unscoped AnyAsync() over the
        // whole table would false-positive on another test's legitimate inspection row.
        Assert.False(await verify.ReturnInspections.AsNoTracking().AnyAsync(i => i.ReturnItemId == persistedItem.Id));

        var historyCount = await verify.ReturnStatusHistories.AsNoTracking()
            .CountAsync(h => h.ReturnRequestId == persistedRequest.Id);
        Assert.Equal(3, historyCount); // Requested->UnderReview, UnderReview->AwaitingShipment (Approve), AwaitingShipment->InTransit->Received seed history — no "inspection-complete" row added.
        Assert.False(await verify.ReturnStatusHistories.AsNoTracking()
            .AnyAsync(h => h.ReturnRequestId == persistedRequest.Id && h.ReasonCode == "inspection-complete"));
    }

    [SqlServerFact]
    public async Task InspectAsync_WithTheCallersCurrentReturnRowVersion_Succeeds()
    {
        // Sanity companion to the stale-version test above: the same caller-supplied value, when
        // it genuinely matches the current row, must still let the legitimate inspect through —
        // proving the fix enforces the caller's version rather than merely rejecting everything.
        Guid returnPublicId;
        Guid returnItemPublicId;
        byte[] validRowVersion;
        string adminUserId;
        await using (var seed = ReturnStoreConcurrencyFixture.CreateContext())
        {
            (returnPublicId, returnItemPublicId, validRowVersion, adminUserId) = await SeedReceivedReturnAsync(seed);
        }

        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var store = new ReturnStore(context);
        var orderPort = new ReturnOrderEligibilityLookup(context);
        var service = new AdminReturnService(
            store, orderPort, new NoOpReturnInventoryPort(), new NoOpReturnRefundCreationPort(), TimeProvider.System);

        var inspectRequest = new InspectReturnRequest(
            [new InspectReturnItemLine(returnItemPublicId, "Unopened", RestockDisposition.Resellable, null)],
            validRowVersion,
            AssemblyFeeDisposition.NotApplicable,
            0m);

        var dto = await service.InspectAsync(returnPublicId, adminUserId, inspectRequest, CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, dto.Status);
    }

    /// <summary>Seeds a fully persisted Order/OrderItem/ReturnRequest/ReturnItem chain, already
    /// walked Requested -> UnderReview -> Approved -> AwaitingShipment -> InTransit -> Received
    /// (via ReturnStore.CreateWithItemsAsync for the initial insert, then direct Domain calls +
    /// SaveChanges for the rest — mirrors what ReviewAsync/ReceiveAsync do in production, just
    /// inlined since this test seeds prerequisite state rather than exercising those actions).</summary>
    private static async Task<(Guid ReturnPublicId, Guid ReturnItemPublicId, byte[] RowVersion, string AdminUserId)> SeedReceivedReturnAsync(
        DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", NowUtc);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active",
            null, null, "{}", 1, NowUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), shippingProfile.Id, 1,
            20m, 100m, 100m, 100m, 200m, 100_000m,
            null, null, NowUtc);
        context.Set<PackageLimitVersion>().Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            ValidOrderCreation(shippingProfile.Id, packageLimit.Id),
            NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderItem = new OrderItem(
            Guid.CreateVersion7(), order.Id, skuId: null, "SKU-1", "27型螢幕", "27型螢幕 White",
            quantity: 1, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
            unitCostSnapshot: 60m, lineSubtotal: 100m, discountAllocation: 0m,
            lineTotal: 100m, assemblyGroupKey: null, returnableQuantity: 1, NowUtc,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("Test specification", "{}", 1));
        context.OrderItems.Add(orderItem);
        await context.SaveChangesAsync();

        var store = new ReturnStore(context);
        var budgets = new[] { new ReturnItemQuantityBudget(orderItem.Id, RequestedQuantity: 1, MaximumReturnableQuantity: 1) };
        var returnNumber = $"RT-ROWVER-{Guid.NewGuid():N}"[..20];
        var creation = await store.CreateWithItemsAsync(
            new ReturnRequest(Guid.CreateVersion7(), returnNumber, order.Id, requesterUserId: null, "Defective", "面板有亮點", policyVersion: 1, NowUtc),
            budgets,
            requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItem.Id, 1, 0m, "NotInspected", NowUtc)],
            CancellationToken.None);

        var returnRequest = creation.Request;
        returnRequest.Transition(ReturnRequestStatus.UnderReview, NowUtc);
        returnRequest.Approve(admin.Id, ReturnApprovalOutcome.RequiresShipment, NowUtc); // -> AwaitingShipment
        returnRequest.Transition(ReturnRequestStatus.InTransit, NowUtc);
        returnRequest.Transition(ReturnRequestStatus.Received, NowUtc);
        context.ReturnStatusHistories.AddRange(
            new ReturnStatusHistory(returnRequest.Id, ReturnRequestStatus.Requested, ReturnRequestStatus.UnderReview, "auto", null, admin.Id, NowUtc),
            new ReturnStatusHistory(returnRequest.Id, ReturnRequestStatus.UnderReview, ReturnRequestStatus.AwaitingShipment, "eligible", null, admin.Id, NowUtc),
            new ReturnStatusHistory(returnRequest.Id, ReturnRequestStatus.AwaitingShipment, ReturnRequestStatus.Received, "manual-receive", null, admin.Id, NowUtc));
        await context.SaveChangesAsync();

        return (returnRequest.PublicId, creation.Items[0].PublicId, returnRequest.RowVersion, admin.Id);
    }

    private static OrderCreation ValidOrderCreation(
        long shippingProviderProfileId,
        long packageLimitVersionId) =>
        new(
            $"DS{Guid.NewGuid():N}"[..15],
            null,
            $"{Guid.NewGuid():N}@doselect.test",
            OrderStatus.Processing,
            PaymentStatus.Paid,
            FulfillmentStatus.Delivered,
            AssemblyStatus.NotRequired,
            1_200m,
            100m,
            225m,
            0m,
            1_325m,
            "Guest",
            "0912345678",
            "guest@example.com",
            "100",
            "Taipei",
            "Zhongzheng",
            "No. 1",
            null,
            "HOME_DELIVERY",
            shippingProviderProfileId,
            null,
            null,
            null,
            1,
            1,
            null,
            null,
            $"checkout-{Guid.NewGuid():N}",
            null,
            1,
            1,
            new OrderInvoicePreference(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.com",
                null,
                null,
                null,
                null),
            null,
            null,
            new OrderPackageSnapshot(
                packageLimitVersionId, 1m, 40m, 30m, 20m, 90m, 1_325m));

    private sealed class NoOpReturnInventoryPort : IReturnInventoryPort
    {
        public Task StageReturnToStockAsync(
            Guid returnPublicId,
            string adminUserId,
            IReadOnlyList<ReturnToStockInstruction> instructions,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <remarks>這幾條測的是 RowVersion 前置條件，退款建立由自己的測試覆蓋。</remarks>
    private sealed class NoOpReturnRefundCreationPort : IReturnRefundCreationPort
    {
        public Task<ReturnRefundCreationOutcome> StagePendingRefundAsync(
            ReturnRefundCreationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult<ReturnRefundCreationOutcome>(new ReturnRefundCreationOutcome.PendingRefundStaged());
    }
}
