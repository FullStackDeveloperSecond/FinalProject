using System.Net;
using DoSelect.Application.Auditing;
using DoSelect.Application.Idempotency;
using DoSelect.Application.Invoicing;
using DoSelect.Application.OperationalReports;
using DoSelect.Application.Refunds;
using DoSelect.Application.Returns;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.OperationalReports;
using DoSelect.Infrastructure.Refunds;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Returns;
using DoSelect.Infrastructure.Tests.OperationalReports;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// INT-04: one deterministic after-sales journey through the real SQL Server provider.
/// The test exercises return inspection, inventory restock, invoice allowance and
/// operational-report projections while consuming the settled-refund output owned by M-13.
/// </summary>
[Collection(nameof(OperationalReportSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AfterSalesOperationalReportSqlServerTests
{
    private const string IdempotencyPepper = "int-04-after-sales-idempotency-pepper";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 4, 0, 0, TimeSpan.Zero);

    [OperationalReportSqlFact]
    public async Task ResellablePartialReturnRemainsConsistentAcrossRefundAllowanceInventoryAndReports()
    {
        await using var context = OperationalReportSqlFixture.CreateContext();
        var seeded = await SeedAsync(context);
        var timeProvider = new FixedTimeProvider(Now);

        var returnService = new AdminReturnService(
            new ReturnStore(context),
            new ReturnOrderEligibilityLookup(context),
            new ReturnInventoryRestockWriter(context),
            new ReturnRefundCreationPort(context),
            timeProvider);

        var inspected = await returnService.InspectAsync(
            seeded.ReturnPublicId,
            seeded.AdminUserId,
            new InspectReturnRequest(
                [new InspectReturnItemLine(
                    seeded.ReturnItemPublicId,
                    "Unopened",
                    RestockDisposition.Resellable,
                    Note: null)],
                seeded.ReturnRowVersion,
                AssemblyFeeDisposition.NotApplicable,
                returnShippingCost: 0m),
            CancellationToken.None);

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, inspected.Status);

        // M-13 is owned by PR #16. INT-04 starts from the durable state that
        // upstream refund execution publishes instead of testing that executor here.
        context.ChangeTracker.Clear();
        var returnRequestId = await context.ReturnRequests
            .Where(candidate => candidate.PublicId == seeded.ReturnPublicId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        // 唯一 Refund 不變量：InspectAsync 走 production 路徑只能為這張退貨留下一筆。
        var settledRefund = Assert.Single(await context.Refunds
            .Where(candidate => candidate.ReturnRequestId == returnRequestId)
            .ToListAsync());
        Assert.Equal(RefundStatus.PendingReview, settledRefund.Status);
        Assert.Equal(500m, settledRefund.RequestedAmount);

        // M-13 owns Refund.Approve too; WP2（獨立核准 API）尚未交付，這裡與既有做法一致，
        // 直接呼叫 Domain 方法核准，而不假裝走了一個還不存在的端點。
        settledRefund.Approve(500m, seeded.AdminUserId, Now.UtcDateTime.AddMinutes(-3));
        settledRefund.BeginProcessing(seeded.AdminUserId, Now.UtcDateTime.AddMinutes(-2));
        settledRefund.Complete(500m, Now.UtcDateTime.AddMinutes(-1));
        context.RefundAllocations.Add(new RefundAllocation(
            Guid.CreateVersion7(),
            settledRefund.Id,
            seeded.OrderItemId,
            RefundAllocationType.ItemRefund,
            500m,
            originalDiscountAllocation: 0m,
            createdAtUtc: Now.UtcDateTime.AddMinutes(-1),
            quantity: 1));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var invoice = await context.SimulatedInvoices
            .SingleAsync(candidate => candidate.PublicId == seeded.InvoicePublicId);
        var allowanceWriter = new InvoiceAllowanceWriter(
            context,
            new IssueInvoiceAllowanceService(new InvoiceAllowanceReader(context), timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = IdempotencyPepper }),
                timeProvider),
            new EfAuditWriter(context, timeProvider));

        var allowanceResult = await allowanceWriter.CreateAsync(
            new CreateInvoiceAllowanceCommand(
                invoice.PublicId,
                settledRefund.PublicId,
                invoice.RowVersion.ToArray(),
                $"int04-allowance-{settledRefund.PublicId:N}",
                seeded.AdminUserId,
                "int04-allowance",
                new string('b', 32),
                IPAddress.Parse("203.0.113.42")));

        Assert.Equal(201, allowanceResult.StatusCode);
        Assert.Equal(500m, allowanceResult.Body.GrossAmount);
        var allowanceLine = Assert.Single(allowanceResult.Body.Items);
        Assert.Equal(1, allowanceLine.Quantity);
        Assert.Equal(500m, allowanceLine.GrossAmount);

        context.ChangeTracker.Clear();
        var balance = await context.InventoryBalances.AsNoTracking()
            .SingleAsync(candidate => candidate.SkuId == seeded.SkuId);
        Assert.Equal(5, balance.OnHandQuantity);
        Assert.Equal(5, balance.AvailableQuantity);

        var movement = await context.InventoryMovements.AsNoTracking()
            .SingleAsync(candidate => candidate.ReferencePublicId == seeded.ReturnItemPublicId);
        Assert.Equal("ReturnToStock", movement.MovementType);
        Assert.Equal(1, movement.OnHandDelta);
        Assert.Equal(4, movement.BeforeOnHand);
        Assert.Equal(5, movement.AfterOnHand);
        Assert.Equal(300m, movement.UnitCostSnapshot);

        var refund = await context.Refunds.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == settledRefund.PublicId);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(500m, refund.SucceededAmount);

        var allocation = await context.RefundAllocations.AsNoTracking()
            .SingleAsync(candidate => candidate.RefundId == refund.Id);
        Assert.Equal(RefundAllocationType.ItemRefund, allocation.AllocationType);
        Assert.Equal(1, allocation.Quantity);
        Assert.Equal(500m, allocation.Amount);

        var reportService = new EfOperationalReportQueryService(context, timeProvider);
        var query = OperationalReportQueryValidator.Normalize(new ReportQuery(
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 1),
            OperationalReportQueryValidator.SupportedTimeZone,
            CategoryCode: null,
            seeded.BrandCode,
            OrderStatuses: null,
            ReportGranularities.Day,
            Cursor: null,
            PageSize: 20));

        var sales = await reportService.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.SalesOverview),
            query,
            CancellationToken.None);
        Assert.Equal(1060m, Metric(sales, "paid_amount"));
        Assert.Equal(500m, Metric(sales, "refund_amount"));
        Assert.Equal(560m, Metric(sales, "net_revenue"));

        var grossMargin = await reportService.QueryAsync(
            OperationalReportCatalog.Require(OperationalReportKeys.GrossMargin),
            query,
            CancellationToken.None);
        var row = Assert.IsType<GrossMarginReportRowDto>(Assert.Single(grossMargin.Rows.Items));
        Assert.Equal(1, row.RefundedQuantity);
        Assert.Equal(500m, row.NetRevenue);
        Assert.Equal(300m, row.CostOfGoodsSold);
        Assert.Equal(200m, row.GrossProfit);
    }

    private static decimal? Metric(ReportResultDto report, string key) =>
        report.Summary.Single(metric => metric.MetricKey == key).Value;

    private static async Task<SeededJourney> SeedAsync(DoSelectDbContext context)
    {
        var createdAtUtc = Now.UtcDateTime.AddDays(-2);
        var adminUserId = await context.Users.AsNoTracking()
            .Select(user => user.Id)
            .FirstAsync();
        var financeRole = new IdentityRole(AuditRoleNames.FinanceManager);
        context.Roles.Add(financeRole);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = adminUserId,
            RoleId = financeRole.Id,
        });
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var brand = new Brand(Guid.CreateVersion7(), $"I4B{suffix}", "INT-04 品牌", createdAtUtc);
        var category = new Category(
            Guid.CreateVersion7(), $"I4C{suffix}", $"int04-{suffix}", "INT-04 分類", null, createdAtUtc);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(), $"I4P{suffix}", brand.Id, category.Id, "INT-04 商品", createdAtUtc);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(
            Guid.CreateVersion7(), $"I4S{suffix}", product.Id, "INT-04 SKU", 500m, 300m, createdAtUtc);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 4, reorderLevel: 1, createdAtUtc));

        var profile = new ShippingProviderProfile(
            Guid.CreateVersion7(), $"I4SHIP{suffix}", 1, "Active", null, null, "{}", 1, createdAtUtc);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m, 250m, 50_000m,
            null, null, createdAtUtc);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
                $"I4O{Guid.NewGuid():N}"[..20],
                null,
                $"int04-{suffix}@example.test",
                OrderStatus.Processing,
                PaymentStatus.Paid,
                FulfillmentStatus.Delivered,
                AssemblyStatus.NotRequired,
                1000m,
                0m,
                60m,
                0m,
                1060m,
                "INT04 Recipient",
                "0900000000",
                $"int04-{suffix}@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "Test address",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                $"int04-checkout-{suffix}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    $"int04-{suffix}@example.test",
                    null,
                    null,
                    null,
                    null),
                1060m,
                null,
                new OrderPackageSnapshot(packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 1060m),
                60m),
            createdAtUtc);
        order.ChangeOrderStatus(OrderStatus.Completed, createdAtUtc.AddHours(1));
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderItem = new OrderItem(
            Guid.CreateVersion7(), order.Id, sku.Id, sku.SkuCode, "INT-04 商品", "INT-04 SKU",
            2, 500m, 500m, 500m, 300m, 1000m, 0m, 1000m, null, 2,
            createdAtUtc, false, new OrderItemSpecificationSnapshot("{}", "{}", 1));
        var payment = new PaymentAttempt(
            Guid.CreateVersion7(), order.Id, PaymentMethod.CreditCard, 1060m, null,
            $"int04-payment-{suffix}", null, createdAtUtc);
        payment.Transition(PaymentAttemptStatus.AwaitingPayment, createdAtUtc);
        payment.Transition(PaymentAttemptStatus.Processing, createdAtUtc.AddMinutes(1));
        payment.Transition(PaymentAttemptStatus.Paid, createdAtUtc.AddMinutes(2));
        context.AddRange(orderItem, payment);
        await context.SaveChangesAsync();

        var invoice = new SimulatedInvoice(
            Guid.CreateVersion7(),
            new SimulatedInvoiceCreation(
                order.Id,
                $"I4INV{Guid.NewGuid():N}"[..24],
                SimulatedInvoiceBuyerType.Individual,
                $"int04-{suffix}@example.test",
                null,
                null,
                null,
                null,
                952.38m,
                47.62m,
                1000m),
            createdAtUtc);
        invoice.Issue(createdAtUtc.AddHours(1));
        context.SimulatedInvoices.Add(invoice);
        await context.SaveChangesAsync();
        context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
            Guid.CreateVersion7(), invoice.Id, orderItem.Id, "INT-04 商品", sku.SkuCode,
            2, 500m, 0m, 952.38m, 47.62m, 1000m, createdAtUtc));

        var returnRequest = new ReturnRequest(
            Guid.CreateVersion7(), $"I4R{Guid.NewGuid():N}"[..20], order.Id, null,
            "Defective", "INT-04 partial return", 1, createdAtUtc);
        returnRequest.Transition(ReturnRequestStatus.UnderReview, createdAtUtc.AddHours(2));
        returnRequest.Approve(adminUserId, ReturnApprovalOutcome.RequiresShipment, createdAtUtc.AddHours(3));
        returnRequest.Transition(ReturnRequestStatus.InTransit, createdAtUtc.AddHours(4));
        returnRequest.Transition(ReturnRequestStatus.Received, createdAtUtc.AddHours(5));
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        // P3（alex 2026-09-03 #99）：不預先建立／核准 Refund。InspectAsync 現在會在
        // 同一筆交易自動建立 PendingReview Refund（PR #99），這裡先建立退貨到 Received
        // 為止，讓主測試方法呼叫 InspectAsync 走 production 路徑，再依 ReturnRequestId
        // 取得那一筆自動建立的 Refund 並斷言唯一 —— 否則會在同一張退貨留下兩筆 Refund，
        // 違反 #98 裁定的唯一 Refund 不變量，也沒驗證到 WP1 這次正式建立的 Refund
        // 能接上後續流程。
        var returnItem = new ReturnItem(
            Guid.CreateVersion7(), returnRequest.Id, orderItem.Id, 1, 500m, "NotInspected", createdAtUtc);
        context.Add(returnItem);
        await context.SaveChangesAsync();

        return new SeededJourney(
            adminUserId,
            brand.Code,
            sku.Id,
            orderItem.Id,
            returnRequest.PublicId,
            returnItem.PublicId,
            returnRequest.RowVersion.ToArray(),
            invoice.PublicId);
    }

    private sealed record SeededJourney(
        string AdminUserId,
        string BrandCode,
        long SkuId,
        long OrderItemId,
        Guid ReturnPublicId,
        Guid ReturnItemPublicId,
        byte[] ReturnRowVersion,
        Guid InvoicePublicId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
