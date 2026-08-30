using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// INT-04 HTTP acceptance journey over the real SQL Server-backed WebApplicationFactory.
/// Lower-level SQL coverage owns the exact formula details; this test proves the public routes,
/// MFA policy, antiforgery boundary and persisted cross-module outcome compose correctly.
/// </summary>
[Collection(nameof(ReturnsApiCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class AfterSalesOperationalReportHttpTests(ReturnsApiFixture fixture)
{
    [Fact]
    public async Task FinanceAdminCanInspectAllowAndReadTheReconciledReport()
    {
        var seeded = await SeedAsync(fixture.CreateScopedContext());
        using var client = fixture.CreateClient();
        await SignInAsync(client, seeded.AdminUserId);

        using (var inspectRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/v1/admin/returns/{seeded.ReturnPublicId}/actions/inspect")
        {
            Content = JsonContent.Create(new
            {
                items = new[]
                       {
                           new
                           {
                               returnItemPublicId = seeded.ReturnItemPublicId,
                               conditionCode = "Unopened",
                               disposition = "resellable",
                               note = (string?)null,
                           },
                       },
                returnRowVersion = seeded.ReturnRowVersion,
                assemblyFeeDisposition = "notApplicable",
                returnShippingCost = 0m,
            }),
        })
        using (var inspectResponse = await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(
                   client, inspectRequest))
        {
            Assert.Equal(HttpStatusCode.OK, inspectResponse.StatusCode);
            var body = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("awaitingRefund", body.GetProperty("status").GetString());
        }

        // M-13 is owned by PR #16. This HTTP journey consumes its persisted
        // succeeded-refund contract and validates only the downstream INT-04 routes.
        await using (var settle = fixture.CreateScopedContext())
        {
            var refund = await settle.Refunds
                .SingleAsync(candidate => candidate.PublicId == seeded.RefundPublicId);
            var processingAtUtc = new DateTime(2026, 8, 29, 3, 58, 0, DateTimeKind.Utc);
            var succeededAtUtc = processingAtUtc.AddMinutes(1);
            refund.BeginProcessing(seeded.AdminUserId, processingAtUtc);
            refund.Complete(500m, succeededAtUtc);
            settle.RefundAllocations.Add(new RefundAllocation(
                Guid.CreateVersion7(),
                refund.Id,
                seeded.OrderItemId,
                RefundAllocationType.ItemRefund,
                500m,
                originalDiscountAllocation: 0m,
                createdAtUtc: succeededAtUtc,
                quantity: 1));
            await settle.SaveChangesAsync();
        }

        byte[] invoiceRowVersion;
        await using (var read = fixture.CreateScopedContext())
        {
            invoiceRowVersion = await read.SimulatedInvoices.AsNoTracking()
                .Where(invoice => invoice.PublicId == seeded.InvoicePublicId)
                .Select(invoice => invoice.RowVersion)
                .SingleAsync();
        }

        using (var allowanceRequest = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/v1/admin/invoices/{seeded.InvoicePublicId}/allowances")
        {
            Content = JsonContent.Create(new
            {
                refundPublicId = seeded.RefundPublicId,
                invoiceRowVersion,
            }),
        })
        {
            allowanceRequest.Headers.Add(
                "Idempotency-Key",
                $"int04-http-allowance-{seeded.RefundPublicId:N}");
            using var allowanceResponse = await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(
                client, allowanceRequest);
            Assert.Equal(HttpStatusCode.Created, allowanceResponse.StatusCode);
            var body = await allowanceResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(500m, body.GetProperty("grossAmount").GetDecimal());
            Assert.Equal(1, body.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        using var reportResponse = await client.GetAsync(
            $"/api/v1/admin/reports/sales-overview?fromDate=2026-08-25&toDate=2026-09-01" +
            $"&timeZone=Asia%2FTaipei&brandCode={seeded.BrandCode}&granularity=day&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<JsonElement>();
        var metrics = report.GetProperty("summary")
            .EnumerateArray()
            .ToDictionary(
                metric => metric.GetProperty("metricKey").GetString()!,
                metric => metric.GetProperty("value").GetDecimal());
        Assert.Equal(1060m, metrics["paid_amount"]);
        Assert.Equal(500m, metrics["refund_amount"]);
        Assert.Equal(560m, metrics["net_revenue"]);

        await using var verify = fixture.CreateScopedContext();
        var balance = await verify.InventoryBalances.AsNoTracking()
            .SingleAsync(candidate => candidate.SkuId == seeded.SkuId);
        Assert.Equal(5, balance.OnHandQuantity);
        Assert.True(await verify.InventoryMovements.AsNoTracking().AnyAsync(movement =>
            movement.ReferencePublicId == seeded.ReturnItemPublicId &&
            movement.MovementType == "ReturnToStock" &&
            movement.OnHandDelta == 1));
        Assert.True(await verify.SimulatedInvoiceAllowances.AsNoTracking().AnyAsync(allowance =>
            allowance.RefundId == seeded.RefundId && allowance.Amount == 500m));
    }

    private static async Task SignInAsync(HttpClient client, string adminUserId)
    {
        var token = await ReturnsApiFixture.GetAdminAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new
            {
                includeMfa = true,
                roles = new[]
                {
                    DoSelectRoles.OrderManager,
                    DoSelectRoles.FinanceManager,
                },
                userId = adminUserId,
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<SeededJourney> SeedAsync(DoSelectDbContext context)
    {
        await using (context)
        {
            var createdAtUtc = new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc);
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var admin = ApplicationUser.CreateAdmin(
                Guid.CreateVersion7(), $"int04-http-{suffix}@example.test", createdAtUtc);
            var financeRole = new IdentityRole(DoSelectRoles.FinanceManager);
            context.AddRange(admin, financeRole);
            await context.SaveChangesAsync();
            context.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = admin.Id,
                RoleId = financeRole.Id,
            });

            var brand = new Brand(Guid.CreateVersion7(), $"H4B{suffix}", "INT-04 HTTP 品牌", createdAtUtc);
            var category = new Category(
                Guid.CreateVersion7(), $"H4C{suffix}", $"http-int04-{suffix}",
                "INT-04 HTTP 分類", null, createdAtUtc);
            context.AddRange(brand, category);
            await context.SaveChangesAsync();
            var product = new Product(
                Guid.CreateVersion7(), $"H4P{suffix}", brand.Id, category.Id,
                "INT-04 HTTP 商品", createdAtUtc);
            context.Products.Add(product);
            await context.SaveChangesAsync();
            var sku = new Sku(
                Guid.CreateVersion7(), $"H4S{suffix}", product.Id,
                "INT-04 HTTP SKU", 500m, 300m, createdAtUtc);
            context.Skus.Add(sku);
            await context.SaveChangesAsync();
            context.InventoryBalances.Add(new InventoryBalance(
                Guid.CreateVersion7(), sku.Id, 4, 1, createdAtUtc));

            var profile = new ShippingProviderProfile(
                Guid.CreateVersion7(), $"H4SHIP{suffix}", 1, "Active",
                null, null, "{}", 1, createdAtUtc);
            context.ShippingProviderProfiles.Add(profile);
            await context.SaveChangesAsync();
            var packageLimit = new PackageLimitVersion(
                Guid.CreateVersion7(), profile.Id, 1, 30m, 150m, 100m, 100m,
                250m, 50_000m, null, null, createdAtUtc);
            context.PackageLimitVersions.Add(packageLimit);
            await context.SaveChangesAsync();

            var order = Order.Create(
                Guid.CreateVersion7(),
                new OrderCreation(
                    $"H4O{Guid.NewGuid():N}"[..20], null,
                    $"int04-http-{suffix}@example.test", OrderStatus.Processing,
                    PaymentStatus.Paid, FulfillmentStatus.Delivered,
                    AssemblyStatus.NotRequired, 1000m, 0m, 60m, 0m, 1060m,
                    "INT04 Recipient", "0900000000",
                    $"int04-http-{suffix}@example.test", "100", "Taipei",
                    "Zhongzheng", "Test address", null, "HOME", profile.Id,
                    null, null, null, 1, 1, null, null,
                    $"int04-http-checkout-{suffix}", null, 1, 1,
                    new OrderInvoicePreference(
                        SimulatedInvoiceBuyerType.Individual,
                        $"int04-http-{suffix}@example.test",
                        null, null, null, null),
                    1060m, null,
                    new OrderPackageSnapshot(
                        packageLimit.Id, 1m, 40m, 30m, 20m, 90m, 1060m),
                    60m),
                createdAtUtc);
            order.ChangeOrderStatus(OrderStatus.Completed, createdAtUtc.AddHours(1));
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var orderItem = new OrderItem(
                Guid.CreateVersion7(), order.Id, sku.Id, sku.SkuCode,
                "INT-04 HTTP 商品", "INT-04 HTTP SKU", 2, 500m, 500m,
                500m, 300m, 1000m, 0m, 1000m, null, 2, createdAtUtc,
                false, new OrderItemSpecificationSnapshot("{}", "{}", 1));
            var payment = new PaymentAttempt(
                Guid.CreateVersion7(), order.Id, PaymentMethod.CreditCard, 1060m,
                null, $"int04-http-payment-{suffix}", null, createdAtUtc);
            payment.Transition(PaymentAttemptStatus.AwaitingPayment, createdAtUtc);
            payment.Transition(PaymentAttemptStatus.Processing, createdAtUtc.AddMinutes(1));
            payment.Transition(PaymentAttemptStatus.Paid, createdAtUtc.AddMinutes(2));
            context.AddRange(orderItem, payment);
            await context.SaveChangesAsync();

            var invoice = new SimulatedInvoice(
                Guid.CreateVersion7(),
                new SimulatedInvoiceCreation(
                    order.Id, $"H4INV{Guid.NewGuid():N}"[..24],
                    SimulatedInvoiceBuyerType.Individual,
                    $"int04-http-{suffix}@example.test", null, null, null, null,
                    952.38m, 47.62m, 1000m),
                createdAtUtc);
            invoice.Issue(createdAtUtc.AddHours(1));
            context.SimulatedInvoices.Add(invoice);
            await context.SaveChangesAsync();
            context.SimulatedInvoiceItems.Add(new SimulatedInvoiceItem(
                Guid.CreateVersion7(), invoice.Id, orderItem.Id,
                "INT-04 HTTP 商品", sku.SkuCode, 2, 500m, 0m,
                952.38m, 47.62m, 1000m, createdAtUtc));

            var returnRequest = new ReturnRequest(
                Guid.CreateVersion7(), $"H4R{Guid.NewGuid():N}"[..20],
                order.Id, null, "Defective", "INT-04 HTTP partial return", 1,
                createdAtUtc);
            returnRequest.Transition(ReturnRequestStatus.UnderReview, createdAtUtc.AddHours(2));
            returnRequest.Approve(admin.Id, true, createdAtUtc.AddHours(3));
            returnRequest.Transition(ReturnRequestStatus.InTransit, createdAtUtc.AddHours(4));
            returnRequest.Transition(ReturnRequestStatus.Received, createdAtUtc.AddHours(5));
            context.ReturnRequests.Add(returnRequest);
            await context.SaveChangesAsync();

            var returnItem = new ReturnItem(
                Guid.CreateVersion7(), returnRequest.Id, orderItem.Id, 1, 500m,
                "NotInspected", createdAtUtc);
            var refund = new Refund(
                Guid.CreateVersion7(), order.Id, returnRequest.Id, payment.Id,
                $"H4RF{Guid.NewGuid():N}"[..20], 500m, "customer_request",
                admin.Id, $"int04-http-create-{suffix}", createdAtUtc);
            refund.Approve(500m, admin.Id, createdAtUtc.AddHours(6));
            context.AddRange(returnItem, refund);
            await context.SaveChangesAsync();

            return new SeededJourney(
                admin.Id,
                brand.Code,
                sku.Id,
                orderItem.Id,
                returnRequest.PublicId,
                returnItem.PublicId,
                returnRequest.RowVersion.ToArray(),
                refund.PublicId,
                refund.Id,
                invoice.PublicId);
        }
    }

    private sealed record SeededJourney(
        string AdminUserId,
        string BrandCode,
        long SkuId,
        long OrderItemId,
        Guid ReturnPublicId,
        Guid ReturnItemPublicId,
        byte[] ReturnRowVersion,
        Guid RefundPublicId,
        long RefundId,
        Guid InvoicePublicId);
}
