using System.Security.Cryptography;
using System.Text;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Promotions;
using DoSelect.Domain.Refunds;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Reviews;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Seeding;

public sealed class DemoDataSeeder(DoSelectDbContext dbContext)
{
    private const string MarkerBrandCode = "DEMO-BRAND-001";

    public async Task<DemoSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedDatabase();
        if (await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            var pendingMigrations = await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                var userTableCount = await dbContext.Database
                    .SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE is_ms_shipped = 0")
                    .SingleAsync(cancellationToken);
                if (userTableCount == 0)
                {
                    await dbContext.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "An existing non-empty demo schema has pending migrations. The seed " +
                        "command will not alter it; create a new allowlisted demo database or " +
                        "review and apply migrations separately.");
                }
            }
        }
        else
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        var before = await ReadCountsAsync(cancellationToken);
        var beforeTotal = before.Values.Sum();

        if (beforeTotal == DemoSeedManifest.MainBusinessRecordTotal &&
            CountsMatchManifest(before) &&
            await dbContext.Brands.AnyAsync(
                brand => brand.Code == MarkerBrandCode,
                cancellationToken))
        {
            return CreateResult(created: false, before);
        }

        if (beforeTotal != 0 ||
            await dbContext.Users.AnyAsync(cancellationToken) ||
            await dbContext.Brands.AnyAsync(cancellationToken) ||
            await dbContext.Categories.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Demo seed requires an empty database, or an already complete matching demo seed. " +
                "It never deletes or overwrites existing data.");
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await SeedUsersAsync(cancellationToken);
        var catalog = await SeedCatalogAsync(cancellationToken);
        var shipping = await SeedShippingAsync(cancellationToken);
        var orders = await SeedOrdersAsync(catalog.Skus, shipping, cancellationToken);
        await SeedInventoryAsync(catalog.Skus, cancellationToken);
        await SeedSupportAsync(orders.Orders, cancellationToken);
        await SeedReturnsAndRefundsAsync(
            orders.Orders,
            orders.Items,
            orders.PaidAttempts,
            cancellationToken);
        await SeedReviewsAndFavoritesAsync(
            orders.Orders,
            orders.Items,
            catalog.Products,
            catalog.Skus,
            cancellationToken);
        await SeedCouponsAsync(orders.Orders, cancellationToken);

        var after = await ReadCountsAsync(cancellationToken);
        if (!CountsMatchManifest(after))
        {
            throw new InvalidOperationException(
                $"Demo seed count validation failed. Expected " +
                $"{DemoSeedManifest.MainBusinessRecordTotal}, actual {after.Values.Sum()}.");
        }

        await transaction.CommitAsync(cancellationToken);
        return CreateResult(created: true, after);
    }

    private void EnsureAllowedDatabase()
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        var validSuffix = databaseName.StartsWith("DoSelectDemo_", StringComparison.Ordinal) &&
            databaseName.Length == "DoSelectDemo_".Length + 32 &&
            databaseName["DoSelectDemo_".Length..].All(Uri.IsHexDigit);
        if (!string.Equals(databaseName, "DoSelectDemo", StringComparison.Ordinal) && !validSuffix)
        {
            throw new InvalidOperationException(
                "Demo seed is restricted to 'DoSelectDemo' or 'DoSelectDemo_<32 hex>' databases.");
        }

        var dataSource = builder.DataSource.Trim();
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = dataSource[4..];
        }
        var host = dataSource.Split(['\\', ','], 2)[0];
        var isLocalHost = host is "." or "(local)" ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        if (!isLocalHost)
        {
            throw new InvalidOperationException(
                "Demo seed is restricted to a local or loopback SQL Server data source.");
        }
    }

    private async Task SeedUsersAsync(CancellationToken cancellationToken)
    {
        var users = new List<ApplicationUser>(DemoSeedManifest.Members + 1);
        var profiles = new List<MemberProfile>(DemoSeedManifest.Members);

        for (var i = 0; i < DemoSeedManifest.Members; i++)
        {
            var createdAt = SeedDate("member", i);
            var email = $"member-{i + 1:D4}@example.invalid";
            var user = ApplicationUser.CreateMember(StableGuid($"member:{i}"), email, createdAt);
            user.Id = $"demo-member-{i + 1:D4}";
            user.NormalizedEmail = email.ToUpperInvariant();
            user.NormalizedUserName = email.ToUpperInvariant();
            user.SecurityStamp = StableGuid($"member-security:{i}").ToString("N");
            user.ConcurrencyStamp = StableGuid($"member-concurrency:{i}").ToString("N");
            user.ConfirmEmail(createdAt.AddMinutes(1));
            users.Add(user);
            profiles.Add(new MemberProfile(
                user.Id,
                user.PublicId,
                $"展示會員 {i + 1:D4}",
                new DateOnly(1980 + i % 25, 1 + i % 12, 1 + i % 27),
                createdAt));
        }

        var adminCreatedAt = DemoSeedManifest.PeriodStartUtc;
        var adminEmail = "demo-admin@example.invalid";
        var admin = ApplicationUser.CreateAdmin(
            StableGuid("admin"),
            adminEmail,
            adminCreatedAt);
        admin.Id = "demo-admin-0001";
        admin.NormalizedEmail = adminEmail.ToUpperInvariant();
        admin.NormalizedUserName = adminEmail.ToUpperInvariant();
        admin.SecurityStamp = StableGuid("admin-security").ToString("N");
        admin.ConcurrencyStamp = StableGuid("admin-concurrency").ToString("N");
        admin.ConfirmEmail(adminCreatedAt.AddMinutes(1));
        users.Add(admin);

        dbContext.Users.AddRange(users);
        dbContext.MemberProfiles.AddRange(profiles);
        dbContext.AdminProfiles.Add(new AdminProfile(
            admin.Id,
            admin.PublicId,
            "DEMO-ADMIN-001",
            "展示資料管理員",
            adminCreatedAt));
        await dbContext.SaveChangesAsync(cancellationToken);

        var addresses = Enumerable.Range(0, DemoSeedManifest.Addresses)
            .Select(i => new MemberAddress(
                StableGuid($"address:{i}"),
                profiles[i].UserId,
                "展示地址",
                $"收件人 {i + 1:D4}",
                $"0900{i % 10_000:D4}",
                $"{100 + i % 900:D3}",
                i % 2 == 0 ? "臺北市" : "新北市",
                i % 2 == 0 ? "中正區" : "板橋區",
                $"展示路 {i + 1} 號",
                null,
                SeedDate("address", i)))
            .ToArray();
        dbContext.MemberAddresses.AddRange(addresses);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CatalogSeed> SeedCatalogAsync(CancellationToken cancellationToken)
    {
        var brandNames = new[]
        {
            "ASUS", "Acer", "MSI", "Gigabyte", "Kingston",
            "Northstar Labs", "Cobalt Forge", "Nimbus Components", "Pinecone Tech", "Aurora Circuit",
        };
        var brands = brandNames.Select((name, i) => new Brand(
            StableGuid($"brand:{i}"),
            $"DEMO-BRAND-{i + 1:D3}",
            name,
            DemoSeedManifest.PeriodStartUtc)).ToArray();
        var categories = Enumerable.Range(0, 5).Select(i => new Category(
            StableGuid($"category:{i}"),
            $"DEMO-CATEGORY-{i + 1:D2}",
            $"demo-category-{i + 1:D2}",
            $"展示分類 {i + 1}",
            null,
            DemoSeedManifest.PeriodStartUtc)).ToArray();
        dbContext.Brands.AddRange(brands);
        dbContext.Categories.AddRange(categories);
        await dbContext.SaveChangesAsync(cancellationToken);

        var products = Enumerable.Range(0, DemoSeedManifest.Products).Select(i =>
        {
            var product = new Product(
                StableGuid($"product:{i}"),
                $"DEMO-P-{i + 1:D4}",
                brands[i % brands.Length].Id,
                categories[i % categories.Length].Id,
                $"展示商品 {i + 1:D4}",
                SeedDate("product", i));
            product.UpdateDetails(
                product.BrandId,
                product.CategoryId,
                product.NameZhTw,
                "固定 Seed 產生的合成展示商品，不含外部圖片。",
                36,
                i < 20,
                product.CreatedAtUtc.AddMinutes(1));
            product.ChangeStatus(ProductStatus.Published, product.CreatedAtUtc.AddMinutes(2));
            return product;
        }).ToArray();
        dbContext.Products.AddRange(products);
        await dbContext.SaveChangesAsync(cancellationToken);

        var skus = new List<Sku>(DemoSeedManifest.Skus);
        for (var i = 0; i < products.Length; i++)
        {
            for (var variant = 0; variant < 3; variant++)
            {
                var index = i * 3 + variant;
                var price = 1_000m + i * 40m + variant * 100m;
                var sku = new Sku(
                    StableGuid($"sku:{index}"),
                    $"DEMO-SKU-{index + 1:D4}",
                    products[i].Id,
                    $"{products[i].NameZhTw} 規格 {variant + 1}",
                    price,
                    decimal.Round(price * 0.7m, 0),
                    SeedDate("sku", index));
                sku.UpdateCommercialDetails(
                    sku.NameZhTw,
                    sku.ListPrice,
                    sku.UnitCost,
                    variant == 0,
                    index % 5 == 0,
                    sku.CreatedAtUtc.AddMinutes(1));
                sku.ChangeStatus(SkuStatus.Published, sku.CreatedAtUtc.AddMinutes(2));
                skus.Add(sku);
            }
        }
        dbContext.Skus.AddRange(skus);
        await dbContext.SaveChangesAsync(cancellationToken);

        var definitionsByCategory = new Dictionary<long, SpecificationDefinition[]>();
        foreach (var (category, categoryIndex) in categories.Select((value, index) => (value, index)))
        {
            var definitions = Enumerable.Range(0, 3).Select(i => new SpecificationDefinition(
                StableGuid($"spec-definition:{categoryIndex}:{i}"),
                category.Id,
                $"DEMO_SPEC_{categoryIndex + 1}_{i + 1}",
                $"展示規格 {i + 1}",
                SpecificationValueType.String,
                null,
                false,
                false,
                i,
                DemoSeedManifest.PeriodStartUtc)).ToArray();
            definitionsByCategory.Add(category.Id, definitions);
            dbContext.SpecificationDefinitions.AddRange(definitions);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var values = new List<SkuSpecificationValue>(DemoSeedManifest.ProductSpecificationValues);
        var productsById = products.ToDictionary(product => product.Id);
        for (var i = 0; i < skus.Count; i++)
        {
            var product = productsById[skus[i].ProductId];
            var definitions = definitionsByCategory[product.CategoryId];
            values.Add(new SkuSpecificationValue(
                skus[i].Id, definitions[0].Id, $"值-{i:D4}-A", null, null, null, null,
                SeedDate("spec-value-a", i)));
            values.Add(new SkuSpecificationValue(
                skus[i].Id, definitions[1].Id, $"值-{i:D4}-B", null, null, null, null,
                SeedDate("spec-value-b", i)));
            if (i < 100)
            {
                values.Add(new SkuSpecificationValue(
                    skus[i].Id, definitions[2].Id, $"值-{i:D4}-C", null, null, null, null,
                    SeedDate("spec-value-c", i)));
            }
        }
        dbContext.SkuSpecificationValues.AddRange(values);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CatalogSeed(products, skus);
    }

    private async Task<ShippingSeed> SeedShippingAsync(CancellationToken cancellationToken)
    {
        var provider = new ShippingProviderProfile(
            StableGuid("shipping-provider"),
            "DEMO",
            1,
            "Active",
            DemoSeedManifest.PeriodStartUtc,
            null,
            "{\"profile\":\"implemented-features-v1\"}",
            1,
            DemoSeedManifest.PeriodStartUtc);
        var method = new ShippingMethod(
            StableGuid("shipping-method"),
            "DEMO-HOME",
            "展示宅配",
            "HomeDeliveryStandard",
            100m,
            10_000m,
            false,
            true,
            "DEMO",
            DemoSeedManifest.PeriodStartUtc);
        dbContext.ShippingProviderProfiles.Add(provider);
        dbContext.ShippingMethods.Add(method);
        await dbContext.SaveChangesAsync(cancellationToken);

        var package = new PackageLimitVersion(
            StableGuid("package-limit"),
            provider.Id,
            1,
            30m,
            120m,
            120m,
            120m,
            250m,
            200_000m,
            DemoSeedManifest.PeriodStartUtc,
            null,
            DemoSeedManifest.PeriodStartUtc);
        dbContext.PackageLimitVersions.Add(package);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ShippingSeed(provider, method, package);
    }

    private async Task<OrderSeed> SeedOrdersAsync(
        IReadOnlyList<Sku> skus,
        ShippingSeed shipping,
        CancellationToken cancellationToken)
    {
        var orders = new List<Order>(DemoSeedManifest.Orders);
        for (var i = 0; i < DemoSeedManifest.Orders; i++)
        {
            var itemCount = i < 300 ? 3 : 2;
            var merchandiseSubtotal = itemCount * 1_000m;
            var createdAt = SeedDate(
                "order",
                i,
                DemoSeedManifest.PeriodEndUtc.AddDays(-10));
            var userId = $"demo-member-{i % DemoSeedManifest.Members + 1:D4}";
            var order = Order.Create(
                StableGuid($"order:{i}"),
                new OrderCreation(
                    $"DEMO-ORDER-{i + 1:D5}",
                    userId,
                    null,
                    OrderStatus.PendingPayment,
                    PaymentStatus.Pending,
                    FulfillmentStatus.Pending,
                    AssemblyStatus.NotRequired,
                    merchandiseSubtotal,
                    0m,
                    100m,
                    0m,
                    merchandiseSubtotal + 100m,
                    $"收件人 {i + 1:D4}",
                    $"0911{i % 10_000:D4}",
                    $"member-{i % DemoSeedManifest.Members + 1:D4}@example.invalid",
                    "100",
                    "臺北市",
                    "中正區",
                    $"展示路 {i + 1} 號",
                    null,
                    shipping.Method.Code,
                    shipping.Provider.Id,
                    null,
                    null,
                    null,
                    1,
                    1,
                    null,
                    createdAt.AddDays(2),
                    $"demo-checkout-{i + 1:D5}",
                    null,
                    1,
                    1,
                    new OrderInvoicePreference(
                        SimulatedInvoiceBuyerType.Individual,
                        $"member-{i % DemoSeedManifest.Members + 1:D4}@example.invalid",
                        null,
                        null,
                        null,
                        null),
                    10_000m,
                    null,
                    new OrderPackageSnapshot(
                        shipping.Package.Id, 1m, 20m, 20m, 20m, 60m, merchandiseSubtotal),
                    100m),
                createdAt);

            if (i < 500)
            {
                order.ApplyPaymentProjection(PaymentStatus.Paid, order.GrandTotal, createdAt.AddHours(1));
                order.ChangeOrderStatus(OrderStatus.Confirmed, createdAt.AddHours(2));
                if (i < 450)
                {
                    order.ChangeOrderStatus(OrderStatus.Processing, createdAt.AddHours(3));
                }
                if (i < 250)
                {
                    order.ApplyFulfillmentProjection(FulfillmentStatus.Delivered, createdAt.AddDays(3));
                    order.ChangeOrderStatus(OrderStatus.Completed, createdAt.AddDays(4));
                }
                else if (i < 450)
                {
                    order.ApplyFulfillmentProjection(FulfillmentStatus.InTransit, createdAt.AddDays(2));
                }
                else
                {
                    order.ApplyFulfillmentProjection(FulfillmentStatus.Preparing, createdAt.AddDays(1));
                }
            }
            else if (i < 550)
            {
                order.ApplyPaymentProjection(
                    i < 525 ? PaymentStatus.Failed : PaymentStatus.Expired,
                    0m,
                    createdAt.AddHours(1));
            }
            else
            {
                var paymentStatus = i < 600 ? PaymentStatus.Failed : PaymentStatus.Expired;
                order.ApplyPaymentProjection(paymentStatus, 0m, createdAt.AddHours(1));
                order.ChangeOrderStatus(OrderStatus.Cancelled, createdAt.AddHours(2));
            }
            orders.Add(order);
        }
        dbContext.Orders.AddRange(orders);
        await dbContext.SaveChangesAsync(cancellationToken);

        var items = new List<OrderItem>(DemoSeedManifest.OrderItems);
        for (var i = 0; i < orders.Count; i++)
        {
            var itemCount = i < 300 ? 3 : 2;
            for (var line = 0; line < itemCount; line++)
            {
                var index = items.Count;
                var sku = skus[(i * 3 + line) % skus.Count];
                items.Add(new OrderItem(
                    StableGuid($"order-item:{index}"),
                    orders[i].Id,
                    sku.Id,
                    sku.SkuCode,
                    $"展示商品 {sku.ProductId}",
                    sku.NameZhTw,
                    1,
                    1_000m,
                    1_000m,
                    1_000m,
                    700m,
                    1_000m,
                    0m,
                    1_000m,
                    null,
                    1,
                    orders[i].CreatedAtUtc,
                    true,
                    new OrderItemSpecificationSnapshot("展示規格", "{\"seed\":20260907}", 1)));
            }
        }
        dbContext.OrderItems.AddRange(items);
        await dbContext.SaveChangesAsync(cancellationToken);

        var attempts = new List<PaymentAttempt>(DemoSeedManifest.PaymentAttempts);
        var paidAttempts = new Dictionary<long, PaymentAttempt>();
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var attempt = new PaymentAttempt(
                StableGuid($"payment:{i}"),
                order.Id,
                PaymentMethod.CreditCard,
                order.GrandTotal,
                "DEMO",
                $"demo-payment-{i + 1:D5}",
                order.CreatedAtUtc.AddDays(2),
                order.CreatedAtUtc.AddMinutes(5));
            attempt.SetPaymentInstruction($"DEMO-PAY-{i + 1:D5}", order.CreatedAtUtc.AddMinutes(6));
            if (i < 500)
            {
                attempt.Transition(PaymentAttemptStatus.Processing, order.CreatedAtUtc.AddMinutes(7));
                attempt.Transition(PaymentAttemptStatus.Paid, order.CreatedAtUtc.AddMinutes(8));
                paidAttempts.Add(order.Id, attempt);
            }
            else if (i < 525)
            {
                attempt.Transition(PaymentAttemptStatus.Processing, order.CreatedAtUtc.AddMinutes(7));
                attempt.Transition(
                    PaymentAttemptStatus.Failed,
                    order.CreatedAtUtc.AddMinutes(8),
                    "DEMO_DECLINED");
            }
            else if (i < 550)
            {
                attempt.Transition(PaymentAttemptStatus.Expired, order.CreatedAtUtc.AddDays(2));
            }
            else if (i < 600)
            {
                attempt.Transition(PaymentAttemptStatus.Processing, order.CreatedAtUtc.AddMinutes(7));
                attempt.Transition(
                    PaymentAttemptStatus.Failed,
                    order.CreatedAtUtc.AddMinutes(8),
                    "DEMO_DECLINED");
            }
            else
            {
                attempt.Transition(PaymentAttemptStatus.Expired, order.CreatedAtUtc.AddDays(2));
            }
            attempts.Add(attempt);
        }
        for (var i = 0; i < 50; i++)
        {
            var order = orders[i];
            var attempt = new PaymentAttempt(
                StableGuid($"payment-expired:{i}"),
                order.Id,
                PaymentMethod.ATM,
                order.GrandTotal,
                "DEMO",
                $"demo-payment-expired-{i + 1:D5}",
                order.CreatedAtUtc.AddMinutes(20),
                order.CreatedAtUtc);
            attempt.SetPaymentInstruction(
                $"DEMO-EXPIRED-{i + 1:D5}",
                order.CreatedAtUtc.AddMinutes(1));
            if (i < 25)
            {
                attempt.Transition(PaymentAttemptStatus.Processing, order.CreatedAtUtc.AddMinutes(2));
                attempt.Transition(
                    PaymentAttemptStatus.Failed,
                    order.CreatedAtUtc.AddMinutes(3),
                    "DEMO_RETRY_REQUIRED");
            }
            else
            {
                attempt.Transition(PaymentAttemptStatus.Expired, order.CreatedAtUtc.AddMinutes(20));
            }
            attempts.Add(attempt);
        }
        dbContext.PaymentAttempts.AddRange(attempts);
        await dbContext.SaveChangesAsync(cancellationToken);

        var shipments = new List<Shipment>(DemoSeedManifest.Shipments);
        for (var i = 0; i < DemoSeedManifest.Shipments; i++)
        {
            var order = orders[i];
            var shipment = new Shipment(
                StableGuid($"shipment:{i}"),
                order.Id,
                shipping.Method.Id,
                shipping.Provider.Id,
                null,
                $"DEMO-SHIP-{i + 1:D5}",
                100m,
                order.CreatedAtUtc.AddHours(3));
            if (i < 250)
            {
                AdvanceShipment(shipment, FulfillmentStatus.Delivered, order.CreatedAtUtc.AddDays(1));
            }
            else if (i < 400)
            {
                AdvanceShipment(shipment, FulfillmentStatus.InTransit, order.CreatedAtUtc.AddDays(1));
            }
            else if (i < 450)
            {
                AdvanceShipment(shipment, FulfillmentStatus.Preparing, order.CreatedAtUtc.AddDays(1));
            }
            shipments.Add(shipment);
        }
        dbContext.Shipments.AddRange(shipments);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new OrderSeed(orders, items, paidAttempts);
    }

    private async Task SeedInventoryAsync(
        IReadOnlyList<Sku> skus,
        CancellationToken cancellationToken)
    {
        var balances = skus.Select((sku, i) => new InventoryBalance(
            StableGuid($"inventory-balance:{i}"),
            sku.Id,
            i < 100 ? 2 : 40,
            i < 100 ? 5 : 10,
            DemoSeedManifest.PeriodStartUtc)).ToArray();
        dbContext.InventoryBalances.AddRange(balances);

        var movements = Enumerable.Range(0, DemoSeedManifest.InventoryMovements)
            .Select(i =>
            {
                var skuIndex = i % skus.Count;
                var onHand = skuIndex < 100 ? 2 : 40;
                var isInitialStock = i < skus.Count;
                return new InventoryMovement(
                    StableGuid($"inventory-movement:{i}"),
                    skus[skuIndex].Id,
                    null,
                    isInitialStock
                        ? InventoryMovementTypes.StockIn
                        : InventoryMovementTypes.CostChange,
                    isInitialStock ? onHand : 0,
                    0,
                    isInitialStock ? 0 : onHand,
                    onHand,
                    0,
                    0,
                    skus[skuIndex].UnitCost,
                    isInitialStock ? "DemoInitialStock" : "DemoCostSnapshot",
                    "DemoSeed",
                    StableGuid($"inventory-reference:{i}"),
                    "demo-admin-0001",
                    SeedDate("inventory-movement", i));
            })
            .ToArray();
        dbContext.InventoryMovements.AddRange(movements);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSupportAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var tickets = new List<SupportTicket>(DemoSeedManifest.SupportTickets);
        for (var i = 0; i < DemoSeedManifest.SupportTickets; i++)
        {
            var createdAt = SeedDate("support-ticket", i);
            var ticket = new SupportTicket(
                StableGuid($"support-ticket:{i}"),
                $"DEMO-TICKET-{i + 1:D4}",
                $"demo-member-{i % DemoSeedManifest.Members + 1:D4}",
                orders[i % orders.Count].Id,
                (SupportTicketCategory)(i % Enum.GetValues<SupportTicketCategory>().Length),
                $"展示客服案件 {i + 1:D4}",
                (CasePriority)(i % Enum.GetValues<CasePriority>().Length),
                createdAt.AddHours(4),
                createdAt.AddDays(2),
                createdAt);
            if (i % 5 != 0)
            {
                ticket.Assign("demo-admin-0001", createdAt.AddMinutes(5));
                ticket.Transition(SupportTicketStatus.InProgress, createdAt.AddMinutes(10));
                ticket.RecordFirstHumanResponse(createdAt.AddMinutes(15));
                if (i % 5 == 1)
                {
                    ticket.Transition(SupportTicketStatus.WaitingForCustomer, createdAt.AddMinutes(20));
                }
                else if (i % 5 == 2)
                {
                    ticket.Transition(SupportTicketStatus.Resolved, createdAt.AddHours(2));
                    ticket.Transition(SupportTicketStatus.Closed, createdAt.AddHours(3));
                }
            }
            tickets.Add(ticket);
        }
        dbContext.SupportTickets.AddRange(tickets);
        await dbContext.SaveChangesAsync(cancellationToken);

        var messages = new List<SupportMessage>(DemoSeedManifest.SupportMessages);
        for (var i = 0; i < DemoSeedManifest.SupportMessages; i++)
        {
            var ticket = tickets[i % tickets.Count];
            var admin = i % 3 == 1;
            messages.Add(new SupportMessage(
                StableGuid($"support-message:{i}"),
                ticket.Id,
                admin ? SupportSenderType.Admin : SupportSenderType.Member,
                admin ? "demo-admin-0001" : ticket.MemberUserId,
                $"合成展示客服訊息 {i + 1:D4}",
                false,
                false,
                null,
                "zh-TW",
                ticket.CreatedAtUtc.AddMinutes(30 + i / tickets.Count)));
        }
        dbContext.SupportMessages.AddRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedReturnsAndRefundsAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyList<OrderItem> items,
        IReadOnlyDictionary<long, PaymentAttempt> paidAttempts,
        CancellationToken cancellationToken)
    {
        var returns = new List<ReturnRequest>(DemoSeedManifest.ReturnRequests);
        for (var i = 0; i < DemoSeedManifest.ReturnRequests; i++)
        {
            var order = orders[i];
            var createdAt = order.CompletedAtUtc!.Value.AddDays(1);
            var request = new ReturnRequest(
                StableGuid($"return:{i}"),
                $"DEMO-RETURN-{i + 1:D4}",
                order.Id,
                order.MemberUserId,
                "ChangedMind",
                "合成展示退貨申請",
                1,
                createdAt);
            request.Assign("demo-admin-0001", createdAt.AddMinutes(5));
            request.Transition(ReturnRequestStatus.UnderReview, createdAt.AddMinutes(10));
            request.CaptureRefundTrustedInputs(
                AssemblyFeeDisposition.NotApplicable,
                0m,
                createdAt.AddMinutes(15));
            request.Approve(
                "demo-admin-0001",
                i < DemoSeedManifest.Refunds
                    ? ReturnApprovalOutcome.RefundDue
                    : ReturnApprovalOutcome.RequiresShipment,
                createdAt.AddMinutes(20));
            returns.Add(request);
        }
        dbContext.ReturnRequests.AddRange(returns);
        await dbContext.SaveChangesAsync(cancellationToken);

        var itemByOrder = items.GroupBy(item => item.OrderId)
            .ToDictionary(group => group.Key, group => group.First());
        var returnItems = returns.Select((request, i) => new ReturnItem(
            StableGuid($"return-item:{i}"),
            request.Id,
            itemByOrder[request.OrderId].Id,
            1,
            1_000m,
            "PendingInspection",
            request.CreatedAtUtc,
            "合成展示退貨品項")).ToArray();
        dbContext.ReturnItems.AddRange(returnItems);
        await dbContext.SaveChangesAsync(cancellationToken);

        var refunds = new List<Refund>(DemoSeedManifest.Refunds);
        for (var i = 0; i < DemoSeedManifest.Refunds; i++)
        {
            var order = orders[i];
            var request = returns[i];
            var createdAt = request.ApprovedAtUtc!.Value.AddMinutes(1);
            var refund = new Refund(
                StableGuid($"refund:{i}"),
                order.Id,
                request.Id,
                paidAttempts[order.Id].Id,
                $"DEMO-REFUND-{i + 1:D4}",
                1_000m,
                "ChangedMind",
                order.MemberUserId,
                $"demo-refund-{i + 1:D4}",
                createdAt);
            if (i < 90)
            {
                refund.Approve(1_000m, "demo-admin-0001", createdAt.AddMinutes(1));
                if (i < 60)
                {
                    refund.BeginProcessing("demo-admin-0001", createdAt.AddMinutes(2));
                    refund.Complete(1_000m, createdAt.AddMinutes(3));
                    request.Transition(ReturnRequestStatus.Completed, createdAt.AddMinutes(4));
                    order.ApplyRefundProjection(
                        OrderRefundStatus.PartiallyRefunded,
                        1_000m,
                        createdAt.AddMinutes(4));
                }
            }
            refunds.Add(refund);
        }
        dbContext.Refunds.AddRange(refunds);
        await dbContext.SaveChangesAsync(cancellationToken);

        var allocations = refunds.Select((refund, i) => new RefundAllocation(
            StableGuid($"refund-allocation:{i}"),
            refund.Id,
            itemByOrder[refund.OrderId].Id,
            RefundAllocationType.ItemRefund,
            1_000m,
            0m,
            refund.CreatedAtUtc,
            1)).ToArray();
        dbContext.RefundAllocations.AddRange(allocations);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedReviewsAndFavoritesAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyList<OrderItem> items,
        IReadOnlyList<Product> products,
        IReadOnlyList<Sku> skus,
        CancellationToken cancellationToken)
    {
        var orderById = orders.ToDictionary(order => order.Id);
        var productsById = products.ToDictionary(product => product.Id);
        var skusById = skus.ToDictionary(sku => sku.Id);
        var reviews = Enumerable.Range(0, DemoSeedManifest.ProductReviews).Select(i =>
        {
            var item = items[i];
            var order = orderById[item.OrderId];
            var product = productsById[skusById[item.SkuId!.Value].ProductId];
            var review = new ProductReview(
                StableGuid($"review:{i}"),
                order.MemberUserId!,
                item.Id,
                product.Id,
                (byte)(1 + i % 5),
                $"展示評價 {i + 1:D4}",
                "固定 Seed 產生的合成評價內容。",
                order.CompletedAtUtc ?? order.CreatedAtUtc.AddDays(5));
            review.Submit(review.CreatedAtUtc.AddMinutes(1));
            if (i < 200)
            {
                review.Review("demo-admin-0001", true, null, review.CreatedAtUtc.AddMinutes(2));
            }
            return review;
        }).ToArray();
        dbContext.ProductReviews.AddRange(reviews);

        var favorites = Enumerable.Range(0, DemoSeedManifest.Favorites)
            .Select(i => new Favorite(
                $"demo-member-{i + 1:D4}",
                products[i].Id,
                SeedDate("favorite", i)))
            .ToArray();
        dbContext.Favorites.AddRange(favorites);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedCouponsAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var coupons = Enumerable.Range(0, DemoSeedManifest.Coupons).Select(i =>
        {
            var coupon = new Coupon(
                StableGuid($"coupon:{i}"),
                new CouponCreation(
                    $"DEMO{i + 1:D3}",
                    $"展示優惠券 {i + 1:D3}",
                    CouponDiscountType.FixedAmount,
                    100m,
                    1_000m,
                    null,
                    DemoSeedManifest.PeriodStartUtc.AddDays(-1),
                    DemoSeedManifest.PeriodEndUtc.AddDays(31),
                    1_000,
                    10,
                    true,
                    false,
                    CouponScopeType.All),
                DemoSeedManifest.PeriodStartUtc.AddDays(-2));
            coupon.ActivateNow(CouponUsageState.Unused, DemoSeedManifest.PeriodStartUtc);
            return coupon;
        }).ToArray();
        dbContext.Coupons.AddRange(coupons);
        await dbContext.SaveChangesAsync(cancellationToken);

        var redemptions = Enumerable.Range(0, DemoSeedManifest.CouponRedemptions).Select(i =>
        {
            var order = orders[i];
            var redemption = new CouponRedemption(
                StableGuid($"coupon-redemption:{i}"),
                coupons[i % coupons.Length].Id,
                order.Id,
                order.MemberUserId,
                null,
                order.CreatedAtUtc,
                order.CreatedAtUtc.AddDays(1),
                order.CreatedAtUtc);
            redemption.Consume(order.CreatedAtUtc.AddMinutes(1));
            return redemption;
        }).ToArray();
        dbContext.CouponRedemptions.AddRange(redemptions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, int>> ReadCountsAsync(
        CancellationToken cancellationToken)
    {
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["members"] = await dbContext.MemberProfiles.CountAsync(cancellationToken),
            ["addresses"] = await dbContext.MemberAddresses.CountAsync(cancellationToken),
            ["products"] = await dbContext.Products.CountAsync(cancellationToken),
            ["skus"] = await dbContext.Skus.CountAsync(cancellationToken),
            ["productSpecificationValues"] = await dbContext.SkuSpecificationValues.CountAsync(cancellationToken),
            ["orders"] = await dbContext.Orders.CountAsync(cancellationToken),
            ["orderItems"] = await dbContext.OrderItems.CountAsync(cancellationToken),
            ["paymentAttempts"] = await dbContext.PaymentAttempts.CountAsync(cancellationToken),
            ["shipments"] = await dbContext.Shipments.CountAsync(cancellationToken),
            ["inventoryMovements"] = await dbContext.InventoryMovements.CountAsync(cancellationToken),
            ["supportTickets"] = await dbContext.SupportTickets.CountAsync(cancellationToken),
            ["supportMessages"] = await dbContext.SupportMessages.CountAsync(cancellationToken),
            ["returnRequests"] = await dbContext.ReturnRequests.CountAsync(cancellationToken),
            ["refunds"] = await dbContext.Refunds.CountAsync(cancellationToken),
            ["productReviews"] = await dbContext.ProductReviews.CountAsync(cancellationToken),
            ["favorites"] = await dbContext.Favorites.CountAsync(cancellationToken),
            ["couponsAndRedemptions"] =
                await dbContext.Coupons.CountAsync(cancellationToken) +
                await dbContext.CouponRedemptions.CountAsync(cancellationToken),
            ["aiSearchFunnelEvents"] = 0,
        };
    }

    private static bool CountsMatchManifest(IReadOnlyDictionary<string, int> actual) =>
        DemoSeedManifest.ExpectedCounts.All(expected =>
            actual.TryGetValue(expected.Key, out var count) && count == expected.Value) &&
        actual.Values.Sum() == DemoSeedManifest.MainBusinessRecordTotal;

    private static DemoSeedResult CreateResult(
        bool created,
        IReadOnlyDictionary<string, int> counts) =>
        new(
            DemoSeedManifest.Version,
            DemoSeedManifest.RandomSeed,
            DemoSeedManifest.PeriodStartUtc,
            DemoSeedManifest.PeriodEndUtc,
            counts.Values.Sum(),
            created,
            counts);

    private static DateTime SeedDate(
        string scope,
        int index,
        DateTime? maximumUtc = null)
    {
        var maximum = maximumUtc ?? DemoSeedManifest.PeriodEndUtc.AddDays(-5);
        var seconds = StableInt($"date:{scope}:{index}",
            (int)(maximum - DemoSeedManifest.PeriodStartUtc).TotalSeconds);
        return DemoSeedManifest.PeriodStartUtc.AddSeconds(seconds);
    }

    private static int StableInt(string value, int exclusiveMaximum)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{DemoSeedManifest.RandomSeed}:{value}"));
        return (int)(BitConverter.ToUInt32(bytes, 0) % (uint)exclusiveMaximum);
    }

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{DemoSeedManifest.RandomSeed}:{value}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static void AdvanceShipment(
        Shipment shipment,
        FulfillmentStatus target,
        DateTime startedAtUtc)
    {
        shipment.ChangeStatus(FulfillmentStatus.Preparing, startedAtUtc);
        if (target == FulfillmentStatus.Preparing) return;
        shipment.ChangeStatus(FulfillmentStatus.Shipped, startedAtUtc.AddHours(1));
        if (target == FulfillmentStatus.Shipped) return;
        shipment.ChangeStatus(FulfillmentStatus.InTransit, startedAtUtc.AddHours(2));
        if (target == FulfillmentStatus.InTransit) return;
        shipment.ChangeStatus(target, startedAtUtc.AddHours(3));
    }

    private sealed record CatalogSeed(Product[] Products, List<Sku> Skus);
    private sealed record ShippingSeed(
        ShippingProviderProfile Provider,
        ShippingMethod Method,
        PackageLimitVersion Package);
    private sealed record OrderSeed(
        List<Order> Orders,
        List<OrderItem> Items,
        Dictionary<long, PaymentAttempt> PaidAttempts);
}
