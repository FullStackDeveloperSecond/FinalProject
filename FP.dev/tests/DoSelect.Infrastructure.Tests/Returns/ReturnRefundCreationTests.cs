using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Persistence.Returns;
using DoSelect.Infrastructure.Refunds;
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
            new ReturnRefundCreationPort(context),
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
        DoSelectDbContext context, bool withPaidAttempt)
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

        var orderItem = new OrderItem(
            Guid.CreateVersion7(), order.Id, skuId: null, "SKU-1", "27型螢幕", "27型螢幕 White",
            quantity: 2, listUnitPrice: 100m, saleUnitPrice: 100m, finalUnitPrice: 100m,
            unitCostSnapshot: 60m, lineSubtotal: 200m, discountAllocation: 0m,
            lineTotal: 200m, assemblyGroupKey: null, returnableQuantity: 2, NowUtc,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("Test specification", "{}", 1));
        context.OrderItems.Add(orderItem);
        await context.SaveChangesAsync();

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
        returnRequest.Approve(admin.Id, requiresShipment: true, NowUtc);
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
