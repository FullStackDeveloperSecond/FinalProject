using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Builds;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Idempotency;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Shopping;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Builds;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Checkout;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Checkout;

[CollectionDefinition(nameof(EfCheckoutTransactionGatewayCollection))]
public sealed class EfCheckoutTransactionGatewayCollection
    : ICollectionFixture<EfCheckoutTransactionGatewayFixture>;

[Collection(nameof(EfCheckoutTransactionGatewayCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfCheckoutTransactionGatewayTests
{
    private const string CouponGuestUsageKey =
        "checkout-coupon-guest-usage-v1-test-key-32-bytes-minimum";
    private const string IdempotencyActorScopePepper =
        "checkout-idempotency-actor-scope-test-key-32-bytes";
    private static readonly DateTime NowUtc =
        new(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <c>DEC-BATCH-017</c>／<c>DEC-P285</c>：最終應付金額在建立付款嘗試前，
    /// 必須以 <c>AwayFromZero</c> 四捨五入到整數新臺幣。
    /// </summary>
    /// <remarks>
    /// 訂單明細、優惠券分攤、運費與組裝費都是 <c>decimal(18,2)</c>，可以合法產生角分。
    /// 沒有這一步，含角分的訂單會把小數帶進 <c>PaymentAttempt.Amount</c>，
    /// 而發票表頭是整數元 —— 開票時 <c>IssuedAmount != OrderPaidAmount</c>，
    /// <c>InvoiceCalculator</c> 會判定為交易快照不一致並拒絕開立。
    /// 錯誤會出現在離原因很遠的地方。
    /// </remarks>
    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_RoundsAFractionalTotalToWholeNewTaiwanDollars()
    {
        // 1000.55 + 150 運費 = 1150.55 → 1151。
        var seed = await SeedAsync(onHandQuantity: 5, listPrice: 1_000.55m);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gateway = CreateGateway(context);

        var created = await gateway.ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders
            .SingleAsync(candidate => candidate.PublicId == created.PublicId);

        // 明細保留交易快照的角分；只有最終應付被整數化。
        Assert.Equal(1_000.55m, order.MerchandiseSubtotal);
        Assert.Equal(1_151m, order.GrandTotal);
        Assert.Equal(0m, order.GrandTotal % 1m);
    }

    /// <summary>
    /// <c>PaymentPolicies</c> 記載的金額鏈第一段：
    /// <c>Order.GrandTotal = PaymentAttempt.Amount</c>。
    /// </summary>
    /// <remarks>
    /// 用含角分的訂單驗，才證明得了「整數化之後的值」有流到付款嘗試上 ——
    /// 整數輸入的情況下，漏掉整數化與否送出的金額一模一樣。
    /// </remarks>
    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_ChargesThePaymentAttemptExactlyTheOrderTotal()
    {
        var seed = await SeedAsync(onHandQuantity: 5, listPrice: 1_000.55m);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gateway = CreateGateway(context);

        var created = await gateway.ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders
            .SingleAsync(candidate => candidate.PublicId == created.PublicId);
        var attempt = await verification.PaymentAttempts
            .SingleAsync(candidate => candidate.OrderId == order.Id);

        Assert.Equal(order.GrandTotal, attempt.Amount);
        Assert.Equal(1_151m, attempt.Amount);
    }

    /// <summary>
    /// 整數化只作用在最終應付，不改寫任何一筆明細。
    /// </summary>
    /// <remarks>
    /// <c>DEC-BATCH-017</c> 明確禁止在下游改寫金額：明細要保留交易快照，
    /// 而發票端若發現加總對不上，是拒絕開立而不是自己調整。
    /// </remarks>
    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_KeepsTheCentsOnTheOrderLineItself()
    {
        var seed = await SeedAsync(onHandQuantity: 5, listPrice: 1_000.55m);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gateway = CreateGateway(context);

        var created = await gateway.ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders
            .SingleAsync(candidate => candidate.PublicId == created.PublicId);
        var item = await verification.OrderItems
            .SingleAsync(candidate => candidate.OrderId == order.Id);

        Assert.Equal(1_000.55m, item.FinalUnitPrice);
        Assert.Equal(1_000.55m, item.LineTotal);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_CreatesTheAtomicCheckoutAggregate()
    {
        var seed = await SeedAsync(onHandQuantity: 5);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gateway = CreateGateway(context);

        var created = await gateway.ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(candidate => candidate.PublicId == created.PublicId);
        var reservation = await verification.InventoryReservations.SingleAsync(candidate => candidate.OrderId == order.Id);
        var attempt = await verification.PaymentAttempts.SingleAsync(candidate => candidate.OrderId == order.Id);
        var cart = await verification.Carts.SingleAsync(candidate => candidate.PublicId == seed.Command.CartPublicId);
        var balance = await verification.InventoryBalances.SingleAsync(candidate => candidate.SkuId == reservation.SkuId);

        Assert.Equal(1_150m, order.GrandTotal);
        Assert.Equal(1_000m, order.MerchandiseSubtotal);
        Assert.Equal(150m, order.ShippingFee);
        Assert.Equal(150m, order.ShippingMethodBaseFeeSnapshot);
        Assert.Equal(DoSelect.Domain.Orders.OrderStatus.PendingPayment, order.OrderStatus);
        Assert.Equal(DoSelect.Domain.Orders.PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(DoSelect.Domain.Inventory.InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(4, balance.AvailableQuantity);
        Assert.Equal(1, balance.ReservedQuantity);
        Assert.Equal(PaymentAttemptStatus.AwaitingPayment, attempt.Status);
        Assert.Equal(NowUtc.AddMinutes(15), attempt.InstructionExpiresAtUtc);
        Assert.Equal(CartStatus.Converted, cart.Status);
        Assert.Equal(5, await verification.OrderStatusHistories.CountAsync(candidate => candidate.OrderId == order.Id));
        Assert.Single(await verification.OrderItems.Where(candidate => candidate.OrderId == order.Id).ToListAsync());
        Assert.Single(created.Items);
        Assert.Equal(1_150m, created.Amounts.GrandTotal);
        Assert.Equal("Guest", created.Recipient.RecipientName);
        Assert.Contains("cancel", created.AvailableActions);

        var replay = await CreateGateway(verification).FindCreatedOrderAsync(created.PublicId);
        Assert.NotNull(replay);
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(replay));
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task CheckoutService_ReplaysTheSameRequestWithoutDuplicatingSqlSideEffects()
    {
        var seed = await SeedAsync(onHandQuantity: 5);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        var service = CreateCheckoutService(context, seed.Command.PolicyVersions);
        var request = ToCreateOrderRequest(seed.Command);

        var first = await service.CreateOrderAsync(
            seed.Command.Actor,
            request,
            seed.Command.IdempotencyKey);
        var replay = await service.CreateOrderAsync(
            seed.Command.Actor,
            request,
            seed.Command.IdempotencyKey);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Body.PublicId, replay.Body.PublicId);
        Assert.Equal(first.Body.OrderNumber, replay.Body.OrderNumber);

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(
            candidate => candidate.SourceCartPublicId == seed.Command.CartPublicId);
        Assert.Single(await verification.InventoryReservations
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync());
        Assert.Single(await verification.PaymentAttempts
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync());
        Assert.Single(await verification.IdempotencyRecords
            .Where(candidate => candidate.Operation == CheckoutService.Operation &&
                                candidate.Key == seed.Command.IdempotencyKey)
            .ToListAsync());

        var sku = await verification.Skus.SingleAsync(
            candidate => candidate.PublicId == seed.SkuPublicId);
        var balance = await verification.InventoryBalances.SingleAsync(
            candidate => candidate.SkuId == sku.Id);
        Assert.Equal(4, balance.AvailableQuantity);
        Assert.Equal(1, balance.ReservedQuantity);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task CheckoutService_WhenTwoCartsRaceForTheLastItem_OnlyOneCreatesAnOrder()
    {
        var firstSeed = await SeedAsync(onHandQuantity: 1);
        var secondCommand = await SeedCompetingCartAsync(firstSeed);
        await using var firstContext = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var secondContext = EfCheckoutTransactionGatewayFixture.CreateContext();
        var firstService = CreateCheckoutService(firstContext, firstSeed.Command.PolicyVersions);
        var secondService = CreateCheckoutService(secondContext, secondCommand.PolicyVersions);

        var outcomes = await Task.WhenAll(
            RunCheckoutOrCaptureErrorAsync(firstService, firstSeed.Command),
            RunCheckoutOrCaptureErrorAsync(secondService, secondCommand));

        Assert.Single(outcomes, outcome => outcome.Order is not null);
        Assert.Single(outcomes, outcome => outcome.ErrorCode == "inventory_insufficient");

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var sourceCartIds = new[] { firstSeed.Command.CartPublicId, secondCommand.CartPublicId };
        var createdOrders = await verification.Orders
            .Where(candidate => candidate.SourceCartPublicId.HasValue &&
                sourceCartIds.Contains(candidate.SourceCartPublicId.Value))
            .ToListAsync();
        var order = Assert.Single(createdOrders);
        var sku = await verification.Skus.SingleAsync(
            candidate => candidate.PublicId == firstSeed.SkuPublicId);
        var balance = await verification.InventoryBalances.SingleAsync(
            candidate => candidate.SkuId == sku.Id);

        Assert.Equal(0, balance.AvailableQuantity);
        Assert.Equal(1, balance.ReservedQuantity);
        Assert.Single(await verification.InventoryReservations
            .Where(candidate => candidate.OrderId == order.Id &&
                                candidate.Status == InventoryReservationStatus.Active)
            .ToListAsync());
        Assert.Single(await verification.PaymentAttempts
            .Where(candidate => candidate.OrderId == order.Id)
            .ToListAsync());
        Assert.Equal(
            1,
            await verification.Carts.CountAsync(candidate =>
                sourceCartIds.Contains(candidate.PublicId) && candidate.Status == CartStatus.Converted));
        Assert.Equal(
            1,
            await verification.Carts.CountAsync(candidate =>
                sourceCartIds.Contains(candidate.PublicId) && candidate.Status == CartStatus.Active));
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WhenInventoryIsInsufficient_OuterTransactionRollsBackEverything()
    {
        // 先讓資料庫裡確實存在一筆別人的已完成結帳。沒有這一步，
        // 下面的範圍限定就算寫錯成「整張表」也一樣會過。
        await CommitAnUnrelatedCheckoutAsync();
        var seed = await SeedAsync(onHandQuantity: 0);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<DoSelect.Application.Common.DomainProblemException>(
                () => CreateGateway(context).ExecuteAsync(seed.Command));

            Assert.Equal("inventory_insufficient", exception.Code);
            await transaction.RollbackAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();

        // 這條讓範圍限定變成可驗證的：資料庫裡確實有別的訂單，所以若把下面任何一條
        // 寫回「整張表」，測試會立刻紅。
        Assert.NotEmpty(await verification.Orders.ToListAsync());

        // 斷言範圍限定在「這一次結帳」，不是整張表。整個 collection 共用一個資料庫，
        // 而別的測試會 commit 訂單 —— 用整張表當條件，等於要求這條測試永遠排在
        // 那些測試之前，宣告順序一改就紅。
        var orderIds = verification.Orders
            .Where(candidate => candidate.SourceCartPublicId == seed.Command.CartPublicId)
            .Select(candidate => candidate.Id);

        Assert.Empty(await verification.Orders
            .Where(candidate => candidate.SourceCartPublicId == seed.Command.CartPublicId)
            .ToListAsync());
        Assert.Empty(await verification.InventoryReservations
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());
        Assert.Empty(await verification.PaymentAttempts
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());

        // 庫存的證據不經過訂單，所以就算上面三條因為沒有訂單而變成空集合，
        // 這一條仍然證明保留數量沒有被寫進去。
        var sku = await verification.Skus
            .SingleAsync(candidate => candidate.PublicId == seed.SkuPublicId);
        var balance = await verification.InventoryBalances
            .SingleAsync(candidate => candidate.SkuId == sku.Id);
        Assert.Equal(0, balance.ReservedQuantity);

        Assert.Equal(
            CartStatus.Active,
            (await verification.Carts.SingleAsync(candidate => candidate.PublicId == seed.Command.CartPublicId)).Status);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WithFreeShippingCoupon_SnapshotsEligibilityNameAndSeat()
    {
        var seed = await SeedAsync(onHandQuantity: 5, withCoupon: true);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await CreateGateway(context).ExecuteAsync(seed.Command);
            await transaction.CommitAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(
            candidate => candidate.CheckoutIdempotencyKey == seed.Command.IdempotencyKey);
        var item = await verification.OrderItems.SingleAsync(candidate => candidate.OrderId == order.Id);
        var orderCoupon = await verification.OrderCoupons.SingleAsync(candidate => candidate.OrderId == order.Id);
        var redemption = await verification.CouponRedemptions.SingleAsync(candidate => candidate.OrderId == order.Id);

        Assert.Equal(0m, order.ShippingFee);
        Assert.Equal(150m, order.ShippingMethodBaseFeeSnapshot);
        Assert.Equal(1_000m, order.GrandTotal);
        Assert.True(item.IsCouponEligible);
        Assert.Equal("Checkout 免運券", orderCoupon.NameSnapshot);
        Assert.True(orderCoupon.IsFreeShipping);
        Assert.Equal(CouponRedemptionStatus.Reserved, redemption.Status);
        Assert.Equal(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(CouponGuestUsageKey),
                Encoding.UTF8.GetBytes("guest@example.test")),
            redemption.GuestUsageKeyHash);
    }

    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_WhenCouponReducesGrandTotalBelowOne_RejectsWithoutSideEffects()
    {
        await CommitAnUnrelatedCheckoutAsync();
        var seed = await SeedAsync(onHandQuantity: 5, zeroTotalCoupon: true);
        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<DomainProblemException>(
                () => CreateGateway(context).ExecuteAsync(seed.Command));

            Assert.Equal(DomainErrorCodes.OrderTotalBelowMinimum, exception.Code);
            Assert.Equal(409, exception.StatusCode);
            await transaction.RollbackAsync();
        }

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();

        // 同上：先證明資料庫裡有別的訂單，範圍限定才有意義。
        Assert.NotEmpty(await verification.Orders.ToListAsync());

        // 與 WhenInventoryIsInsufficient 同一個問題（alex #68 P2）：這裡原本斷言整張
        // Orders／OrderItems／InventoryReservations／InventoryMovements／
        // CouponRedemptions／OrderCoupons／PaymentAttempts 都是空的，而整個 collection
        // 共用一個資料庫、別的測試會 commit 訂單 —— 那不是斷言，是「這條測試永遠排在
        // 那些測試之前」的排程假設。xUnit 沒有固定這個 collection 的順序，所以綠燈只
        // 代表這次剛好排到可通過的順序。
        //
        // 上一輪我只修了實際變紅的那一條，沒有在同一個檔案裡找同型的寫法。
        var sku = await verification.Skus
            .SingleAsync(candidate => candidate.PublicId == seed.SkuPublicId);
        var orderIds = verification.Orders
            .Where(candidate => candidate.SourceCartPublicId == seed.Command.CartPublicId)
            .Select(candidate => candidate.Id);

        Assert.Empty(await verification.Orders
            .Where(candidate => candidate.SourceCartPublicId == seed.Command.CartPublicId)
            .ToListAsync());
        Assert.Empty(await verification.OrderItems
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());
        Assert.Empty(await verification.InventoryReservations
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());
        Assert.Empty(await verification.CouponRedemptions
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());
        Assert.Empty(await verification.OrderCoupons
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());
        Assert.Empty(await verification.PaymentAttempts
            .Where(candidate => orderIds.Contains(candidate.OrderId))
            .ToListAsync());

        // 庫存的兩條證據不經過訂單，所以上面那些在「沒有訂單」時天然通過的斷言
        // 之外，這裡才真的證明這次結帳沒有動到庫存。
        Assert.Empty(await verification.InventoryMovements
            .Where(candidate => candidate.SkuId == sku.Id)
            .ToListAsync());

        var cart = await verification.Carts.SingleAsync(
            candidate => candidate.PublicId == seed.Command.CartPublicId);
        // 原本是無條件的 SingleAsync()，等於假設整個資料庫只有一列庫存 ——
        // 每次 SeedAsync 都會新增一列，所以那同樣是排程假設。
        var balance = await verification.InventoryBalances
            .SingleAsync(candidate => candidate.SkuId == sku.Id);
        Assert.Equal(CartStatus.Active, cart.Status);
        Assert.Equal(5, balance.AvailableQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
    }

    /// <summary>
    /// 組長 PR #34 round-7 review (DEC-BATCH-027): proves Build API (<see cref="EfCompatibilityCheckService"/>)
    /// and Checkout (<see cref="EfCheckoutTransactionGateway"/>) reach the SAME verdict for the SAME SKU
    /// pair, since both now read facts through the identical <see cref="EfCompatibilityCatalogReader"/> and
    /// evaluate them through the identical <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> — the
    /// two code paths used to disagree because Build API read its own, now-deleted parallel model. Exercises
    /// both real production entry points against a real SQL Server database (not a mock), for both a
    /// compatible pair (both must agree "OK") and a socket-mismatched pair (both must agree "blocked").
    /// </summary>
    [global::DoSelect.Infrastructure.Tests.Idempotency.SqlServerFact]
    public async Task ExecuteAsync_AndCompatibilityCheckService_AgreeOnTheSameCpuMotherboardPair()
    {
        var (cpuPublicId, matchingBoardPublicId, mismatchedBoardPublicId, sharedComponentPublicIds, matchingCommand, mismatchedCommand) =
            await SeedAssemblyBuildAsync();

        IReadOnlyList<BuildItemInput> BuildItems(Guid boardPublicId) =>
            [
                new BuildItemInput(cpuPublicId, 1),
                new BuildItemInput(boardPublicId, 1),
                .. sharedComponentPublicIds.Select(id => new BuildItemInput(id, 1)),
            ];

        await using var checkContext = EfCheckoutTransactionGatewayFixture.CreateContext();
        var checkService = new EfCompatibilityCheckService(checkContext, new EfCompatibilityCatalogReader(checkContext));
        var matchingCheck = await checkService.CheckAsync(
            new CompatibilityCheckRequest(BuildItems(matchingBoardPublicId)), null, CancellationToken.None);
        var mismatchedCheck = await checkService.CheckAsync(
            new CompatibilityCheckRequest(BuildItems(mismatchedBoardPublicId)), null, CancellationToken.None);

        Assert.Equal("compatible", matchingCheck.Overall);
        Assert.Equal("blocked", mismatchedCheck.Overall);

        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var created = await CreateGateway(context).ExecuteAsync(matchingCommand);
            await transaction.CommitAsync();
            Assert.NotEqual(Guid.Empty, created.PublicId);
        }

        await using (var context = EfCheckoutTransactionGatewayFixture.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var exception = await Assert.ThrowsAsync<DomainProblemException>(
                () => CreateGateway(context).ExecuteAsync(mismatchedCommand));
            Assert.Equal("cart_item_requires_attention", exception.Code);
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// Seeds a full, otherwise-compatible 8-category build (<see cref="DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories"/>
    /// — the <see cref="DoSelect.Domain.Builds.CompatibilityEvaluator"/> requires every singleton role present
    /// before it evaluates any pairwise rule, so a bare CPU+Motherboard pair alone only ever reaches
    /// <c>insufficientData</c>) plus a SECOND Motherboard SKU whose socket deliberately mismatches the CPU's.
    /// Returns two ready-to-execute <see cref="CheckoutCommand"/>s, each with all 8 SKUs in one cart sharing
    /// one <c>AssemblyGroupKey</c> — one using the matching Motherboard (expected compatible), one using the
    /// mismatched Motherboard (expected blocked on <c>CPU_SOCKET</c>).
    /// </summary>
    private static async Task<(
        Guid CpuPublicId,
        Guid MatchingBoardPublicId,
        Guid MismatchedBoardPublicId,
        IReadOnlyList<Guid> SharedComponentPublicIds,
        CheckoutCommand MatchingCommand,
        CheckoutCommand MismatchedCommand)> SeedAssemblyBuildAsync()
    {
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await global::DoSelect.Infrastructure.Tests.Builds.CompatibilityCheckServiceFixture
            .SeedCategoriesAndSpecTemplatesAsync(context);

        async Task<Sku> SeedAsync(string categoryCode, Dictionary<string, object?>? specValues = null, Dictionary<string, string[]>? multiValues = null) =>
            await global::DoSelect.Infrastructure.Tests.Builds.CompatibilityCheckServiceFixture.SeedComponentSkuAsync(
                context, categoryCode, specValues, multiValues);

        var cpu = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Cpu,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CpuGeneration] = "RYZEN_7000",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 105m,
            });
        var matchingBoard = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "AM5",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var mismatchedBoard = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Motherboard,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CpuSocket] = "LGA1700",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardChipset] = "X670E",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemorySlotCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryMaxCapacityGb] = 128m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardFormFactor] = "ATX",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.M2SlotCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.SataPortCount] = 4m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MotherboardCpuEps8PinRequiredCount] = 1m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 20m,
            });
        var memory = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Memory,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryType] = "DDR5",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryModuleCount] = 1m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.MemoryKitCapacityGb] = 16m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var psu = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Psu,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PsuRatedWatts] = 650m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PsuFormFactor] = "ATX",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PsuPcie62PinCount] = 2m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.Psu12VhpwrCount] = 1m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PsuCpuEps8PinCount] = 2m,
            });
        var pcCase = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Case,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CaseGpuMaxLengthMm] = 320m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CaseCoolerMaxHeightMm] = 170m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CaseSupportedMotherboardFormFactor] = ["ATX"],
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CaseSupportedPsuFormFactor] = ["ATX"],
            });
        var gpu = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Gpu,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.GpuLengthMm] = 280m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.GpuRecommendedPsuWatts] = 450m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 200m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.GpuPcie62PinRequiredCount] = 1m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.Gpu12VhpwrRequiredCount] = 0m,
            });
        var storage = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.Storage,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.StorageInterface] = "M2_NVME",
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 5m,
            });
        var cooler = await SeedAsync(
            DoSelect.Domain.Catalog.CompatibilityCatalogContract.Categories.CpuCooler,
            new Dictionary<string, object?>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm] = 150m,
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.PowerDrawWatts] = 10m,
            },
            multiValues: new Dictionary<string, string[]>
            {
                [DoSelect.Domain.Catalog.CompatibilityCatalogContract.SemanticKeys.CpuSocket] = ["AM5"],
            });

        var allSkus = new[] { cpu, matchingBoard, mismatchedBoard, memory, psu, pcCase, gpu, storage, cooler };
        foreach (var sku in allSkus)
        {
            sku.UpdatePackageDimensions(1m, 20m, 15m, 10m, NowUtc);
            context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
                Guid.CreateVersion7(), sku.Id, onHandQuantity: 10, reorderLevel: 1, NowUtc));
        }

        await context.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var method = new ShippingMethod(
            Guid.CreateVersion7(), $"ASM-{suffix}", "組裝宅配", "HomeDeliveryAssembly",
            150m, 5_000m, allowsCod: true, requiresPrepayment: false, $"PROVIDER-{suffix}", NowUtc);
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"PROVIDER-{suffix}", 1, "Published", null, null, "{}", 1, NowUtc);
        context.AddRange(method, profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m, null, null, NowUtc));
        await context.SaveChangesAsync();

        CheckoutCommand BuildCommand(Cart cart, string idempotencyKeySuffix) => new(
            CheckoutActor.ForGuest($"checkout-test-secret-{idempotencyKeySuffix}"),
            cart.PublicId,
            cart.RowVersion.ToArray(),
            new CheckoutRecipientSnapshot(
                "Guest", "0912345678", "guest@example.test", "TW",
                "100", "Taipei", "Zhongzheng", "No. 1", null),
            null,
            method.Code,
            null,
            PaymentMethod.CreditCard,
            null,
            new CheckoutInvoicePreferenceSnapshot(
                SimulatedInvoiceBuyerType.Individual, "guest@example.test", null, null, null, null),
            new CheckoutPolicySnapshot(1, 1, 1, 1),
            $"checkout-parity-test-{idempotencyKeySuffix}");

        async Task<CheckoutCommand> SeedCartAsync(Sku boardSku, string label)
        {
            var guestKey = $"{suffix}-{label}";
            var cart = Cart.CreateForGuest(
                Guid.CreateVersion7(), SHA256.HashData(Encoding.UTF8.GetBytes($"checkout-test-secret-{guestKey}")),
                NowUtc.AddDays(30), NowUtc);
            context.Carts.Add(cart);
            await context.SaveChangesAsync();
            var assemblyGroupKey = Guid.CreateVersion7();
            var components = new[] { cpu, boardSku, memory, psu, pcCase, gpu, storage, cooler };
            context.CartItems.AddRange(components.Select(
                sku => new CartItem(Guid.CreateVersion7(), cart.Id, sku.Id, 1, assemblyGroupKey, NowUtc)));
            cart.Touch(NowUtc);
            await context.SaveChangesAsync();
            return BuildCommand(cart, guestKey);
        }

        var matchingCommand = await SeedCartAsync(matchingBoard, "match");
        var mismatchedCommand = await SeedCartAsync(mismatchedBoard, "mismatch");
        var sharedComponentPublicIds = new[] { memory, psu, pcCase, gpu, storage, cooler }
            .Select(sku => sku.PublicId)
            .ToArray();

        return (
            cpu.PublicId, matchingBoard.PublicId, mismatchedBoard.PublicId, sharedComponentPublicIds,
            matchingCommand, mismatchedCommand);
    }

    private static EfCheckoutTransactionGateway CreateGateway(DoSelectDbContext context) =>
        new(
            context,
            new EfCompatibilityCatalogReader(context),
            new CouponRuleReader(context),
            new SqlOrderNumberGenerator(context),
            new CouponGuestUsageHasher(
                Microsoft.Extensions.Options.Options.Create(new CouponGuestUsageOptions
                {
                    CouponGuestUsageHmacKeyV1 = CouponGuestUsageKey,
                })),
            new FixedTimeProvider(NowUtc));

    /// <summary>
    /// 送一筆會成功的結帳並 commit，讓「這次結帳沒有留下東西」的斷言有東西可以區分。
    /// </summary>
    private static async Task CommitAnUnrelatedCheckoutAsync()
    {
        var other = await SeedAsync(onHandQuantity: 5);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await CreateGateway(context).ExecuteAsync(other.Command);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// 組長 PR #73 review A1／item 1: the store's ProviderCode is the CVS brand (7-11／FamilyMart)
    /// while the method's ProviderCode is the logistics profile class, so the old
    /// store.ProviderCode == method.ProviderCode comparison made every store-pickup checkout fail
    /// with "store not found". These are the first store-pickup checkouts covered at all — the
    /// conflated comparison shipped precisely because no test exercised this path.
    /// </summary>
    [Theory]
    [InlineData("7-11")]
    [InlineData("FamilyMart")]
    public async Task ExecuteAsync_StorePickup_CreatesTheOrderForEitherStoreBrand(string storeBrand)
    {
        var seed = await SeedStorePickupAsync(storeBrand, storeIsActive: true);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var created = await CreateGateway(context).ExecuteAsync(seed.Command);
        await transaction.CommitAsync();

        await using var verification = EfCheckoutTransactionGatewayFixture.CreateContext();
        var order = await verification.Orders.SingleAsync(candidate => candidate.PublicId == created.PublicId);
        Assert.Equal(DoSelect.Domain.Orders.OrderStatus.PendingPayment, order.OrderStatus);
        // The order keeps the store *display* snapshot, immune to later store edits.
        Assert.Equal(seed.StoreName, order.StoreName);
    }

    [Fact]
    public async Task ExecuteAsync_StorePickup_WhenTheStoreIsInactive_RejectsWithShippingStoreInactive()
    {
        var seed = await SeedStorePickupAsync("7-11", storeIsActive: false);
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => CreateGateway(context).ExecuteAsync(seed.Command));

        Assert.Equal("shipping_store_inactive", exception.Code);
    }

    private sealed record StorePickupSeed(CheckoutCommand Command, string StoreName);

    private static async Task<StorePickupSeed> SeedStorePickupAsync(string storeBrand, bool storeIsActive)
    {
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var brand = new Brand(Guid.CreateVersion7(), $"BR-{suffix}", "Checkout 品牌", NowUtc);
        var category = new Category(
            Guid.CreateVersion7(), $"CAT-{suffix}", $"category-{suffix.ToLowerInvariant()}", "周邊", null, NowUtc);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(), $"PROD-{suffix}", brand.Id, category.Id, "測試商品", NowUtc);
        product.ChangeStatus(ProductStatus.Published, NowUtc);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(
            Guid.CreateVersion7(), $"SKU-{suffix}", product.Id, "測試 SKU", 1_000m, 600m, NowUtc);
        sku.UpdatePackageDimensions(1m, 30m, 20m, 10m, NowUtc);
        sku.ChangeStatus(SkuStatus.Published, NowUtc);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, 5, 1, NowUtc));

        // Method Kind must be checkout's own "ConvenienceStorePickup" constant; the method's
        // ProviderCode is the logistics profile class — deliberately different from storeBrand.
        var method = new ShippingMethod(
            Guid.CreateVersion7(),
            $"CVS-{suffix}",
            "超商取貨",
            "ConvenienceStorePickup",
            60m,
            2_000m,
            allowsCod: true,
            requiresPrepayment: false,
            $"PROVIDER-{suffix}",
            NowUtc);
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"PROVIDER-{suffix}", 1, "Published", null, null, "{}", 1, NowUtc);
        context.AddRange(method, profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 5m, 45m, 45m, 45m, 105m, 20_000m,
            null, null, NowUtc));

        var storeName = $"{storeBrand} 測試門市 {suffix}";
        var store = new ConvenienceStore(
            Guid.CreateVersion7(), storeBrand, $"ST{suffix}", storeName,
            "測試路 1 號", "台北市", "大安區", isDemoData: true, NowUtc);
        if (!storeIsActive)
        {
            store.UpdateDetails(storeName, "測試路 1 號", "台北市", "大安區", isActive: false, NowUtc);
        }

        context.ConvenienceStores.Add(store);
        await context.SaveChangesAsync();

        var guestKey = "checkout-cvs-secret-" + suffix;
        var cart = Cart.CreateForGuest(
            Guid.CreateVersion7(),
            SHA256.HashData(Encoding.UTF8.GetBytes(guestKey)),
            NowUtc.AddDays(30),
            NowUtc);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem(
            Guid.CreateVersion7(), cart.Id, sku.Id, 1, null, NowUtc));
        cart.Touch(NowUtc);
        await context.SaveChangesAsync();

        var command = new CheckoutCommand(
            CheckoutActor.ForGuest(guestKey),
            cart.PublicId,
            cart.RowVersion.ToArray(),
            new CheckoutRecipientSnapshot(
                "Guest", "0912345678", "guest@example.test", "TW",
                "100", "Taipei", "Zhongzheng", "No. 1", null),
            null,
            method.Code,
            store.PublicId,
            PaymentMethod.CreditCard,
            null,
            new CheckoutInvoicePreferenceSnapshot(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.test",
                null,
                null,
                null,
                null),
            new CheckoutPolicySnapshot(1, 1, 1, 1),
            "checkout-cvs-test-" + Guid.NewGuid().ToString("N"));
        return new StorePickupSeed(command, storeName);
    }

    private static async Task<CheckoutSeed> SeedAsync(
        int onHandQuantity,
        bool withCoupon = false,
        bool zeroTotalCoupon = false,
        decimal listPrice = 1_000m)
    {
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var brand = new Brand(Guid.CreateVersion7(), $"BR-{suffix}", "Checkout 品牌", NowUtc);
        var category = new Category(
            Guid.CreateVersion7(), $"CAT-{suffix}", $"category-{suffix.ToLowerInvariant()}", "周邊", null, NowUtc);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(), $"PROD-{suffix}", brand.Id, category.Id, "測試商品", NowUtc);
        product.ChangeStatus(ProductStatus.Published, NowUtc);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(
            Guid.CreateVersion7(), $"SKU-{suffix}", product.Id, "測試 SKU", listPrice, 600m, NowUtc);
        sku.UpdatePackageDimensions(1m, 40m, 30m, 20m, NowUtc);
        sku.ChangeStatus(SkuStatus.Published, NowUtc);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity, 1, NowUtc));

        var method = new ShippingMethod(
            Guid.CreateVersion7(),
            $"HOME-{suffix}",
            "一般宅配",
            "HomeDeliveryStandard",
            zeroTotalCoupon ? 0m : 150m,
            5_000m,
            allowsCod: true,
            requiresPrepayment: false,
            $"PROVIDER-{suffix}",
            NowUtc);
        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(),
            $"PROVIDER-{suffix}",
            1,
            "Published",
            null,
            null,
            "{}",
            1,
            NowUtc);
        context.AddRange(method, profile);
        await context.SaveChangesAsync();
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, NowUtc));

        string? couponCode = null;
        if (withCoupon || zeroTotalCoupon)
        {
            couponCode = zeroTotalCoupon ? $"ZERO-{suffix}" : $"FREE-{suffix}";
            var coupon = new Coupon(
                Guid.CreateVersion7(),
                new CouponCreation(
                    couponCode,
                    zeroTotalCoupon ? "Checkout 全額折抵券" : "Checkout 免運券",
                    zeroTotalCoupon ? CouponDiscountType.Percentage : CouponDiscountType.FreeShipping,
                    zeroTotalCoupon ? 1m : null,
                    0m,
                    zeroTotalCoupon ? 1_000m : null,
                    NowUtc.AddDays(-1),
                    NowUtc.AddDays(10),
                    100,
                    1,
                    false,
                    false,
                    CouponScopeType.Restricted),
                NowUtc.AddDays(-2));
            coupon.ActivateNow(CouponUsageState.Unused, NowUtc);
            context.Coupons.Add(coupon);
            await context.SaveChangesAsync();
            context.CouponCategories.Add(new CouponCategory(coupon.Id, category.Id, NowUtc));
        }

        var guestKey = "checkout-test-secret-" + suffix;
        var cart = Cart.CreateForGuest(
            Guid.CreateVersion7(),
            SHA256.HashData(Encoding.UTF8.GetBytes(guestKey)),
            NowUtc.AddDays(30),
            NowUtc);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem(
            Guid.CreateVersion7(), cart.Id, sku.Id, 1, null, NowUtc));
        cart.Touch(NowUtc);
        await context.SaveChangesAsync();

        var command = new CheckoutCommand(
            CheckoutActor.ForGuest(guestKey),
            cart.PublicId,
            cart.RowVersion.ToArray(),
            new CheckoutRecipientSnapshot(
                "Guest", "0912345678", "guest@example.test", "TW",
                "100", "Taipei", "Zhongzheng", "No. 1", null),
            null,
            method.Code,
            null,
            PaymentMethod.CreditCard,
            couponCode,
            new CheckoutInvoicePreferenceSnapshot(
                SimulatedInvoiceBuyerType.Individual,
                "guest@example.test",
                null,
                null,
                null,
                null),
            new CheckoutPolicySnapshot(1, 1, 1, 1),
            "checkout-gateway-test-" + Guid.NewGuid().ToString("N"));
        return new CheckoutSeed(command, sku.PublicId);
    }

    private static CheckoutService CreateCheckoutService(
        DoSelectDbContext context,
        CheckoutPolicySnapshot policies)
    {
        var timeProvider = new FixedTimeProvider(NowUtc);
        return new CheckoutService(
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions
                {
                    ActorScopePepper = IdempotencyActorScopePepper,
                }),
                timeProvider),
            CreateGateway(context),
            new StaticCheckoutPolicyProvider(policies));
    }

    private static CreateOrderRequest ToCreateOrderRequest(CheckoutCommand command) => new(
        command.CartPublicId,
        command.CartRowVersion.ToArray(),
        new CheckoutBuyerInput(
            command.Recipient.Email,
            command.Recipient.Name,
            command.Recipient.Phone),
        new CheckoutShippingInput(
            command.ShippingMethodCode,
            command.StorePublicId.HasValue
                ? null
                : new CheckoutAddressInput(
                    command.Recipient.Name,
                    command.Recipient.Phone,
                    command.Recipient.PostalCode,
                    command.Recipient.City,
                    command.Recipient.District,
                    command.Recipient.AddressLine1,
                    command.Recipient.AddressLine2),
            command.StorePublicId,
            command.DeliveryNote),
        command.PaymentMethod,
        command.CouponCode,
        new CheckoutInvoiceInput(
            CheckoutInvoiceType.Simulated,
            command.InvoicePreference.BuyerType == SimulatedInvoiceBuyerType.Company
                ? CheckoutInvoiceBuyerType.Company
                : CheckoutInvoiceBuyerType.Personal,
            command.InvoicePreference.CarrierType,
            command.InvoicePreference.CarrierValueMasked,
            command.InvoicePreference.CompanyTaxId,
            command.InvoicePreference.CompanyName),
        new AcceptedPolicyVersions(
            command.PolicyVersions.Terms,
            command.PolicyVersions.Return,
            command.PolicyVersions.Privacy));

    private static async Task<CheckoutCommand> SeedCompetingCartAsync(CheckoutSeed firstSeed)
    {
        await using var context = EfCheckoutTransactionGatewayFixture.CreateContext();
        var sku = await context.Skus.SingleAsync(
            candidate => candidate.PublicId == firstSeed.SkuPublicId);
        var suffix = Guid.NewGuid().ToString("N");
        var guestKey = "checkout-competing-secret-" + suffix;
        var cart = Cart.CreateForGuest(
            Guid.CreateVersion7(),
            SHA256.HashData(Encoding.UTF8.GetBytes(guestKey)),
            NowUtc.AddDays(30),
            NowUtc);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.CartItems.Add(new CartItem(
            Guid.CreateVersion7(), cart.Id, sku.Id, 1, null, NowUtc));
        cart.Touch(NowUtc);
        await context.SaveChangesAsync();

        var email = $"competing-{suffix}@example.test";
        return firstSeed.Command with
        {
            Actor = CheckoutActor.ForGuest(guestKey),
            CartPublicId = cart.PublicId,
            CartRowVersion = cart.RowVersion.ToArray(),
            Recipient = firstSeed.Command.Recipient with { Email = email },
            InvoicePreference = firstSeed.Command.InvoicePreference with { BuyerEmail = email },
            IdempotencyKey = "checkout-competing-" + suffix,
        };
    }

    private static async Task<CheckoutOutcome> RunCheckoutOrCaptureErrorAsync(
        CheckoutService service,
        CheckoutCommand command)
    {
        try
        {
            var result = await service.CreateOrderAsync(
                command.Actor,
                ToCreateOrderRequest(command),
                command.IdempotencyKey);
            return new CheckoutOutcome(result.Body, null);
        }
        catch (DomainProblemException exception)
        {
            return new CheckoutOutcome(null, exception.Code);
        }
    }

    private sealed record CheckoutSeed(CheckoutCommand Command, Guid SkuPublicId);

    private sealed record CheckoutOutcome(
        DoSelect.Application.Orders.OrderDto? Order,
        string? ErrorCode);

    private sealed class StaticCheckoutPolicyProvider(CheckoutPolicySnapshot current)
        : ICheckoutPolicyProvider
    {
        public CheckoutPolicySnapshot Current => current;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

public sealed class EfCheckoutTransactionGatewayFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!global::DoSelect.Infrastructure.Tests.Idempotency.IdempotencyExecutorFixture.IsEnabled)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build(
                    "DoSelectCheckoutGatewayTests"))
            .Options);
}
