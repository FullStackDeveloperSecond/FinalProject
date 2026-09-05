using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Returns;
using DoSelect.Infrastructure.Refunds;
using DoSelect.Infrastructure.Orders;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Returns;

/// <summary>
/// WP1：退貨進入 <c>AwaitingRefund</c> 時建立待審退款的正式路徑（alex 2026-09-03 #98 A2）。
/// </summary>
/// <remarks>
/// <para>
/// 用真的 SQL Server：這裡要證明的三件事全部只在真的資料庫上成立 —— 同一筆交易、
/// 唯一索引擋得住並行、失敗時退貨狀態不會單獨落地。InMemory 沒有交易、沒有唯一索引，
/// 綠燈只代表沒被發現。
/// </para>
/// <para>
/// 裁定要求「不得以 seed 取代 production 路徑」，所以每一條都真的呼叫
/// <see cref="AdminReturnService.InspectAsync"/>，不直接呼叫埠、也不自己塞 Refund。
/// </para>
/// </remarks>
[Collection(nameof(ReturnStoreConcurrencyCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ReturnRefundCreationTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 3, 3, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task InspectingAReceivedReturnCreatesOnePendingRefundInTheSameTransaction()
    {
        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var seed = await SeedReceivedReturnAsync(context, withPaidAttempt: true);

        await CreateService(context).InspectAsync(
            seed.ReturnPublicId, seed.AdminUserId, InspectRequest(seed), CancellationToken.None);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        var refund = Assert.Single(await verify.Refunds
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .ToListAsync());

        Assert.Equal(RefundStatus.PendingReview, refund.Status);
        Assert.Equal(seed.OrderId, refund.OrderId);
        Assert.Equal(seed.PaymentAttemptId, refund.PaymentAttemptId);
        Assert.Equal(seed.AdminUserId, refund.RequestedBy);
        Assert.Null(refund.ApprovedAmount);

        // 金額必須是後端依可信快照算出來的淨額，不是任何人傳進來的值。
        // 這筆訂單：單價 100 的商品退 1 件、無折扣、非完整退貨、退貨運費 0、組裝費不退。
        Assert.Equal(100m, refund.RequestedAmount);

        // 退貨狀態與退款在同一筆交易 —— 兩者都要在，不能只有一邊。
        var returnRequest = await verify.ReturnRequests
            .SingleAsync(candidate => candidate.Id == seed.ReturnRequestId);
        Assert.Equal(ReturnRequestStatus.AwaitingRefund, returnRequest.Status);

        // Order 邊界也必須在同一筆交易收到退款投影：待審退款存在時為 Pending，
        // 尚未成功退款所以累計金額仍為 0，並留下可稽核的狀態歷程。
        var order = await verify.Orders.SingleAsync(candidate => candidate.Id == seed.OrderId);
        Assert.Equal(OrderRefundStatus.Pending, order.OrderRefundStatus);
        Assert.Equal(0m, order.RefundedAmount);

        var orderHistory = Assert.Single(await verify.OrderStatusHistories
            .Where(candidate =>
                candidate.OrderId == seed.OrderId &&
                candidate.StateDimension == OrderStateDimension.OrderRefundStatus)
            .ToListAsync());
        Assert.Equal(OrderRefundStatus.None.ToString(), orderHistory.FromStatus);
        Assert.Equal(OrderRefundStatus.Pending.ToString(), orderHistory.ToStatus);
        Assert.Equal(seed.AdminUserId, orderHistory.ActorUserId);
    }

    [SqlServerFact]
    public async Task AFailedRefundCreationRollsBackTheReturnTransitionToo()
    {
        // 這張訂單沒有已付款的付款嘗試，退款因此建立不起來。整筆交易必須一起回滾：
        // 退貨若單獨進了 AwaitingRefund，就會留下一張永遠等不到退款的退貨。
        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var seed = await SeedReceivedReturnAsync(context, withPaidAttempt: false);

        var caught = await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(context).InspectAsync(
                seed.ReturnPublicId, seed.AdminUserId, InspectRequest(seed), CancellationToken.None));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, caught.Code);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        Assert.Empty(await verify.Refunds
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .ToListAsync());

        var returnRequest = await verify.ReturnRequests
            .SingleAsync(candidate => candidate.Id == seed.ReturnRequestId);
        Assert.Equal(ReturnRequestStatus.Received, returnRequest.Status);

        var order = await verify.Orders.SingleAsync(candidate => candidate.Id == seed.OrderId);
        Assert.Equal(OrderRefundStatus.None, order.OrderRefundStatus);
        Assert.Equal(0m, order.RefundedAmount);
        Assert.False(await verify.OrderStatusHistories.AnyAsync(candidate =>
            candidate.OrderId == seed.OrderId &&
            candidate.StateDimension == OrderStateDimension.OrderRefundStatus));
    }

    [SqlServerFact]
    public async Task TwoConcurrentInspectionsProduceExactlyOneRefund()
    {
        // 兩個管理員同時完成檢查。決定性冪等金鑰讓兩邊寫出同一把值，
        // UX_Refunds_IdempotencyKey 於是只讓一邊提交，另一邊整筆回滾。
        // 沒有這道保證，一張退貨會有兩筆待審退款，可退款餘額會被算兩次。
        ReturnSeed seed;
        await using (var setup = ReturnStoreConcurrencyFixture.CreateContext())
        {
            seed = await SeedReceivedReturnAsync(setup, withPaidAttempt: true);
        }

        await using var first = ReturnStoreConcurrencyFixture.CreateContext();
        await using var second = ReturnStoreConcurrencyFixture.CreateContext();

        var results = await Task.WhenAll(
            InspectCatchingAsync(first, seed),
            InspectCatchingAsync(second, seed));

        Assert.Equal(1, results.Count(outcome => outcome is null));
        var failure = Assert.Single(results, outcome => outcome is not null);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        Assert.Single(await verify.Refunds
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .ToListAsync());

        // 敗方拿到的是可理解的狀態衝突，不是 provider 例外變成的 500。
        Assert.Contains(
            failure,
            new[]
            {
                ReturnsWriteException.ErrorCodes.ReturnStateConflict,
                ReturnsWriteException.ErrorCodes.ConcurrencyConflict,
            });
    }

    [SqlServerFact]
    public async Task ZeroNetRefundInspectionCompletesInOneTransactionWithoutCreatingAnyRefund()
    {
        // #99 A1 裁定：淨額 <= 0 時退貨、檢查、庫存回補與歷史仍須同一筆交易落地，
        // 只是終點是 Completed 而不是 AwaitingRefund，而且完全不建立 Refund。
        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var seed = await SeedReceivedReturnAsync(
            context, withPaidAttempt: true, withClawbackExceedingCoupon: true);

        var dto = await CreateService(context).InspectAsync(
            seed.ReturnPublicId, seed.AdminUserId,
            new InspectReturnRequest(
                [new InspectReturnItemLine(seed.ReturnItemPublicId, "Unopened", RestockDisposition.Resellable, null)],
                seed.ReturnRowVersion,
                AssemblyFeeDisposition.NotApplicable,
                returnShippingCost: 0m),
            CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.Completed, dto.Status);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        Assert.Empty(await verify.Refunds
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .ToListAsync());

        var returnRequest = await verify.ReturnRequests
            .SingleAsync(candidate => candidate.Id == seed.ReturnRequestId);
        Assert.Equal(ReturnRequestStatus.Completed, returnRequest.Status);

        var history = await verify.ReturnStatusHistories
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        Assert.DoesNotContain(history, h => h.ToStatus == ReturnRequestStatus.AwaitingRefund);
        var lastHistory = Assert.Single(history, h => h.ToStatus == ReturnRequestStatus.Completed);
        Assert.Equal("zero-net-refund", lastHistory.ReasonCode);

        // 商品確實收回來了——沒有退款不是沒有退貨這件事。
        var movement = await verify.InventoryMovements
            .SingleOrDefaultAsync(candidate => candidate.ReferencePublicId == seed.ReturnItemPublicId);
        Assert.NotNull(movement);

        // #99 review：ReturnInspection 也要在同一筆交易落地，不只是庫存與歷史
        // （alex 2026-09-04）。
        var returnItemId = await verify.ReturnItems
            .Where(candidate => candidate.PublicId == seed.ReturnItemPublicId)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        var inspection = Assert.Single(await verify.ReturnInspections
            .Where(candidate => candidate.ReturnItemId == returnItemId)
            .ToListAsync());
        Assert.Equal("Resellable", inspection.Result);
        Assert.Equal("Unopened", inspection.ConditionCode);
        Assert.Equal(seed.AdminUserId, inspection.InspectedByAdminUserId);
    }

    [SqlServerFact]
    public async Task ARefundNumberCollisionAloneIsAlsoTranslatedNotLeakedAs500()
    {
        // P2（alex 2026-09-03 #99）：RefundNumber 與 IdempotencyKey 都是由同一個
        // ReturnPublicId 推導的決定性值，先前的並行測試剛好只逼出 IdempotencyKey 那條
        // 分支就通過了，SQL Server 回報哪個索引違規在先並不保證順序 —— RefundNumber
        // 那條完全沒被驗證過。這裡刻意讓兩者分開：預先塞一筆 IdempotencyKey 不同、
        // 但 RefundNumber 與「seed 這筆退貨將產生的值」相同的 Refund，逼真正的建立
        // 只可能撞 RefundNumber。
        await using var context = ReturnStoreConcurrencyFixture.CreateContext();
        var seed = await SeedReceivedReturnAsync(context, withPaidAttempt: true);
        var decoy = await SeedReceivedReturnAsync(context, withPaidAttempt: true);

        context.Refunds.Add(new Refund(
            Guid.CreateVersion7(),
            decoy.OrderId,
            decoy.ReturnRequestId,
            decoy.PaymentAttemptId,
            ReturnRefundCreationPort.RefundNumberFor(seed.ReturnPublicId),
            50m,
            "Defective",
            decoy.AdminUserId,
            ReturnRefundCreationPort.IdempotencyKeyFor(decoy.ReturnPublicId),
            NowUtc));
        await context.SaveChangesAsync();

        var caught = await Assert.ThrowsAsync<ReturnsWriteException>(() =>
            CreateService(context).InspectAsync(
                seed.ReturnPublicId, seed.AdminUserId, InspectRequest(seed), CancellationToken.None));

        Assert.Equal(ReturnsWriteException.ErrorCodes.ReturnStateConflict, caught.ErrorCode);

        await using var verify = ReturnStoreConcurrencyFixture.CreateContext();
        Assert.Empty(await verify.Refunds
            .Where(candidate => candidate.ReturnRequestId == seed.ReturnRequestId)
            .ToListAsync());

        var returnRequest = await verify.ReturnRequests
            .SingleAsync(candidate => candidate.Id == seed.ReturnRequestId);
        Assert.Equal(ReturnRequestStatus.Received, returnRequest.Status);
    }

    /// <summary>回 <c>null</c> 代表成功，否則回錯誤碼。</summary>
    private static async Task<string?> InspectCatchingAsync(DoSelectDbContext context, ReturnSeed seed)
    {
        try
        {
            await CreateService(context).InspectAsync(
                seed.ReturnPublicId, seed.AdminUserId, InspectRequest(seed), CancellationToken.None);
            return null;
        }
        catch (ReturnsWriteException caught)
        {
            return caught.ErrorCode;
        }
    }

    private static AdminReturnService CreateService(DoSelectDbContext context) =>
        new(
            new ReturnStore(context),
            new ReturnOrderEligibilityLookup(context),
            new ReturnInventoryRestockWriter(context),
            new ReturnRefundCreationPort(context, new EfRefundOrderProjectionPort(context)),
            TimeProvider.System);

    private static InspectReturnRequest InspectRequest(ReturnSeed seed) =>
        new(
            // Quarantine 而不是 Resellable：回補庫存需要原始 SKU 與庫存餘額，那是庫存埠的
            // 前置條件，與退款建立無關。金額只看退貨數量與可信快照，處置方式不影響。
            [new InspectReturnItemLine(seed.ReturnItemPublicId, "Damaged", RestockDisposition.Quarantine, null)],
            seed.ReturnRowVersion,
            AssemblyFeeDisposition.NotApplicable,
            returnShippingCost: 0m);

    private sealed record ReturnSeed(
        Guid ReturnPublicId,
        long ReturnRequestId,
        Guid ReturnItemPublicId,
        byte[] ReturnRowVersion,
        string AdminUserId,
        long OrderId,
        long PaymentAttemptId);

    private static async Task<ReturnSeed> SeedReceivedReturnAsync(
        DoSelectDbContext context, bool withPaidAttempt, bool withClawbackExceedingCoupon = false)
    {
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", NowUtc);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"HOME-{Guid.NewGuid():N}"[..20], 1, "Active",
            null, null, "{}", 1, NowUtc);
        context.Set<ShippingProviderProfile>().Add(shippingProfile);
        await context.SaveChangesAsync();

        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), shippingProfile.Id, 1,
            20m, 100m, 100m, 100m, 200m, 100_000m, null, null, NowUtc);
        context.Set<PackageLimitVersion>().Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(), ValidOrderCreation(shippingProfile.Id, packageLimit.Id), NowUtc);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // 真的 SKU／庫存餘額：Quarantine 情境用不到，Resellable 回補測試需要它。
        var catalogSuffix = Guid.NewGuid().ToString("N")[..10];
        var brand = new Brand(Guid.CreateVersion7(), $"RFB{catalogSuffix}", "退款測試品牌", NowUtc);
        var category = new Category(
            Guid.CreateVersion7(), $"RFC{catalogSuffix}", $"refund-test-{catalogSuffix}",
            "退款測試分類", null, NowUtc);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();
        var product = new Product(
            Guid.CreateVersion7(), $"RFP{catalogSuffix}", brand.Id, category.Id, "退款測試商品", NowUtc);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(
            Guid.CreateVersion7(), $"RFS{catalogSuffix}", product.Id, "退款測試 SKU", 100m, 60m, NowUtc);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 4, reorderLevel: 1, NowUtc));
        await context.SaveChangesAsync();

        var orderItem = new OrderItem(
            Guid.CreateVersion7(), order.Id, sku.Id, sku.SkuCode, "27型螢幕", "27型螢幕 White",
            quantity: 2, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
            unitCostSnapshot: 60m, lineSubtotal: 200m, discountAllocation: 0m,
            lineTotal: 200m, assemblyGroupKey: null, returnableQuantity: 2, NowUtc,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("Test specification", "{}", 1));
        context.OrderItems.Add(orderItem);
        await context.SaveChangesAsync();

        if (withClawbackExceedingCoupon)
        {
            // 保留下來的品項小計（(2-1)*100=100）遠低於門檻 3000，扣回蓋過品項退款
            // （100 - 500 = -400）。與 RefundCalculatorTests
            // .WhenTheClawbackSwallowsTheWholeRefund_TheAmountIsRejected 同一組數字。
            context.OrderCoupons.Add(new OrderCoupon(
                Guid.CreateVersion7(), order.Id, couponId: null, redemptionId: null,
                "CLAWBACK500", "扣回測試券", CouponDiscountType.FixedAmount, ruleVersion: 1,
                discountValue: 500m, minimumSpendAmount: 3000m, appliedAmount: 500m,
                eligibleSubtotal: 3000m, isFreeShipping: false, NowUtc));
            await context.SaveChangesAsync();
        }

        long paymentAttemptId = 0;
        if (withPaidAttempt)
        {
            var attempt = new PaymentAttempt(
                Guid.CreateVersion7(), order.Id, PaymentMethod.CreditCard, 1_325m,
                "SIMULATED", $"pay-{Guid.NewGuid():N}", null, NowUtc);
            attempt.Transition(PaymentAttemptStatus.AwaitingPayment, NowUtc);
            attempt.Transition(PaymentAttemptStatus.Processing, NowUtc);
            attempt.Transition(PaymentAttemptStatus.Paid, NowUtc);
            context.PaymentAttempts.Add(attempt);
            order.ApplyPaymentProjection(PaymentStatus.Paid, 1_325m, NowUtc);
            await context.SaveChangesAsync();
            paymentAttemptId = attempt.Id;
        }

        var store = new ReturnStore(context);
        var creation = await store.CreateWithItemsAsync(
            new ReturnRequest(
                Guid.CreateVersion7(), $"RT-REFUND-{Guid.NewGuid():N}"[..20], order.Id,
                requesterUserId: null, "Defective", "面板有亮點", policyVersion: 1, NowUtc),
            [new ReturnItemQuantityBudget(orderItem.Id, RequestedQuantity: 1, MaximumReturnableQuantity: 2)],
            requestId => [new ReturnItem(Guid.CreateVersion7(), requestId, orderItem.Id, 1, 0m, "NotInspected", NowUtc)],
            CancellationToken.None);

        var returnRequest = creation.Request;
        returnRequest.Transition(ReturnRequestStatus.UnderReview, NowUtc);
        returnRequest.Approve(admin.Id, ReturnApprovalOutcome.RequiresShipment, NowUtc);
        returnRequest.Transition(ReturnRequestStatus.InTransit, NowUtc);
        returnRequest.Transition(ReturnRequestStatus.Received, NowUtc);
        await context.SaveChangesAsync();

        return new ReturnSeed(
            returnRequest.PublicId,
            returnRequest.Id,
            creation.Items[0].PublicId,
            returnRequest.RowVersion,
            admin.Id,
            order.Id,
            paymentAttemptId);
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

}
