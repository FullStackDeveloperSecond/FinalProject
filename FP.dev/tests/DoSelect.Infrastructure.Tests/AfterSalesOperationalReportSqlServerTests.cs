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
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Returns;
using DoSelect.Infrastructure.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// INT-04: one deterministic after-sales journey through the real SQL Server provider.
/// The test exercises return inspection, inventory restock, refund execution, invoice
/// allowance and operational-report projections without directly rewriting module results.
/// </summary>
[Collection(nameof(RefundExecutorSqlCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AfterSalesOperationalReportSqlServerTests
{
    private const string IdempotencyPepper = "int-04-after-sales-idempotency-pepper";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 4, 0, 0, TimeSpan.Zero);

    [RefundExecutorSqlFact]
    public async Task ResellablePartialReturnRemainsConsistentAcrossRefundAllowanceInventoryAndReports()
    {
        await using var context = RefundExecutorSqlFixture.CreateContext();
        var seeded = await SeedAsync(context);
        var timeProvider = new FixedTimeProvider(Now);

        var returnService = new AdminReturnService(
            new ReturnStore(context),
            new ReturnOrderEligibilityLookup(context),
            new ReturnInventoryRestockWriter(context),
            timeProvider);

        var inspected = await returnService.InspectAsync(
            seeded.ReturnPublicId,
            RefundExecutorSqlFixture.AdminUserId,
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

        var refundExecutor = new RefundExecutor(
            context,
            new EfAuditWriter(context, timeProvider),
            new EfIdempotencyExecutor(
                context,
                Options.Create(new IdempotencyOptions { ActorScopePepper = IdempotencyPepper }),
                timeProvider),
            timeProvider);

        var refundResult = await refundExecutor.ExecuteAsync(new ExecuteRefundRequest(
            seeded.RefundPublicId,
            seeded.RefundRowVersion,
            $"int04-refund-{seeded.RefundPublicId:N}",
            RefundExecutorSqlFixture.AdminUserId,
            "customer_request",
            Note: null,
            CorrelationId: "int04-refund",
            TraceId: new string('a', 32),
            RemoteIpAddress: IPAddress.Parse("203.0.113.42")));

        Assert.True(refundResult.IsSuccess);
        Assert.Equal(500m, refundResult.SettledAmount);

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
                seeded.RefundPublicId,
                invoice.RowVersion.ToArray(),
                $"int04-allowance-{seeded.RefundPublicId:N}",
                RefundExecutorSqlFixture.AdminUserId,
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
            .SingleAsync(candidate => candidate.PublicId == seeded.RefundPublicId);
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
        returnRequest.Approve(RefundExecutorSqlFixture.AdminUserId, requiresShipment: true, createdAtUtc.AddHours(3));
        returnRequest.Transition(ReturnRequestStatus.InTransit, createdAtUtc.AddHours(4));
        returnRequest.Transition(ReturnRequestStatus.Received, createdAtUtc.AddHours(5));
        context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();

        var returnItem = new ReturnItem(
            Guid.CreateVersion7(), returnRequest.Id, orderItem.Id, 1, 500m, "NotInspected", createdAtUtc);
        var refund = new Refund(
            Guid.CreateVersion7(), order.Id, returnRequest.Id, payment.Id,
            $"I4RF{Guid.NewGuid():N}"[..20], 500m, "customer_request",
            RefundExecutorSqlFixture.AdminUserId, $"int04-create-{suffix}", createdAtUtc);
        refund.Approve(500m, RefundExecutorSqlFixture.AdminUserId, createdAtUtc.AddHours(6));
        context.AddRange(returnItem, refund);
        await context.SaveChangesAsync();

        return new SeededJourney(
            brand.Code,
            sku.Id,
            returnRequest.PublicId,
            returnItem.PublicId,
            returnRequest.RowVersion.ToArray(),
            refund.PublicId,
            refund.RowVersion.ToArray(),
            invoice.PublicId);
    }

    private sealed record SeededJourney(
        string BrandCode,
        long SkuId,
        Guid ReturnPublicId,
        Guid ReturnItemPublicId,
        byte[] ReturnRowVersion,
        Guid RefundPublicId,
        byte[] RefundRowVersion,
        Guid InvoicePublicId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
