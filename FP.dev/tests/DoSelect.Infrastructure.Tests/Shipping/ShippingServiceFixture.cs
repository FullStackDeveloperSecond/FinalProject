using DoSelect.Domain.Catalog;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Shipping;

public sealed class ShippingServiceFixture : IAsyncLifetime
{
    // Same defect 組長 caught on PR #47 and PR #36 already fixed for the Inventory
    // fixtures: a hardcoded local instance passes locally, but CI's SQL Server runs in a
    // container reachable only via DOSELECT_SQLSERVER_TEST_CONNECTION, so every test here
    // failed with a connection error in CI. Route through the shared helper instead.
    private static readonly string ConnectionString =
        SqlServerTestConnection.Build("DoSelectShippingServiceTests");

    public Task InitializeAsync() => ResetDatabaseAsync();

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }

    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// ShippingMethod.Code is unique in the database, but this fixture's data survives across
    /// every test in the collection (reset once per collection, not per test — same pattern as
    /// CartServiceFixture) — so tests that seed a method by a fixed literal code like
    /// "HomeDelivery" collide with each other. Kind is what the eligibility logic actually
    /// switches on, so tests can keep a stable Kind while giving each seeded row its own Code.
    /// </summary>
    public static string UniqueMethodCode(string kind) => $"{kind}-{Guid.NewGuid():N}"[..30];

    /// <summary>
    /// GetOptionsForCartAsync has no per-test scoping mechanism (it reads *every* active
    /// ShippingMethod row), and this fixture's database is reset once per collection, not per
    /// test — so any test that cares about the exact set/count of returned options must call
    /// this first to guarantee a clean table, regardless of what earlier tests in the same
    /// collection have seeded.
    /// </summary>
    public static async Task ClearShippingMethodsAsync(DoSelectDbContext context)
    {
        await context.ShippingMethods.ExecuteDeleteAsync();
    }

    /// <summary>Same rationale as <see cref="ClearShippingMethodsAsync"/> — package-limit
    /// version/overlap logic reads every row for a providerCode, so tests need a clean slate.
    /// PackageLimitVersions first: Restrict FK to ShippingProviderProfiles.</summary>
    public static async Task ClearPackageLimitDataAsync(DoSelectDbContext context)
    {
        await context.PackageLimitVersions.ExecuteDeleteAsync();
        await context.ShippingProviderProfiles.ExecuteDeleteAsync();
    }

    public static async Task ClearConvenienceStoresAsync(DoSelectDbContext context)
    {
        await context.ConvenienceStores.ExecuteDeleteAsync();
    }

    public static string UniqueGuestKey() => $"guest-{Guid.NewGuid():N}";

    public static readonly DoSelect.Application.Auditing.AuditRequestContext TestAuditContext =
        new("test-correlation", "0123456789abcdef0123456789abcdef", null);

    /// <summary>The shipping-admin audit actor must be a real Admin holding OrderManager or
    /// SuperAdmin — mirrors CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync.</summary>
    public static async Task<string> SeedShippingAdminAsync(DoSelectDbContext context)
    {
        var admin = DoSelect.Infrastructure.Persistence.Identity.ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        var role = new Microsoft.AspNetCore.Identity.IdentityRole(
            DoSelect.Application.Auditing.AuditRoleNames.OrderManager);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string>
        {
            UserId = admin.Id,
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    /// <summary>
    /// 組長 PR #73 review item 3 made the options screen resolve each method's effective
    /// PackageLimitVersion the same way checkout does, so a seeded method without one would now
    /// (correctly) report ineligible. Every SeedShippingMethodAsync therefore also guarantees its
    /// provider has one Published profile with one effective, generous limit — tests that probe a
    /// specific limit seed their own tighter version instead.
    /// </summary>
    public static async Task EnsureProviderWithLimitAsync(
        DoSelectDbContext context,
        string providerCode,
        decimal maxWeightKg = 20m,
        decimal maxSideCm = 150m,
        decimal maxTotalCm = 150m,
        decimal maxDeclaredValue = 999_999m)
    {
        var now = DateTime.UtcNow;
        var profile = context.ShippingProviderProfiles.Local
                .FirstOrDefault(candidate => candidate.ProviderCode == providerCode)
            ?? await context.ShippingProviderProfiles
                .FirstOrDefaultAsync(candidate => candidate.ProviderCode == providerCode);
        if (profile is null)
        {
            profile = new ShippingProviderProfile(
                Guid.CreateVersion7(), providerCode, 1, "Published", null, null, "{}", 1, now);
            context.ShippingProviderProfiles.Add(profile);
            await context.SaveChangesAsync();
            context.PackageLimitVersions.Add(new PackageLimitVersion(
                Guid.CreateVersion7(), profile.Id, 1,
                maxWeightKg, maxSideCm, maxSideCm, maxSideCm, maxTotalCm, maxDeclaredValue,
                null, null, now));
            await context.SaveChangesAsync();
        }
    }

    /// <summary>Swaps a provider's effective limit for a test-specific one (the auto-seeded
    /// generous limit would otherwise mask the boundary being probed).</summary>
    public static async Task ReplaceProviderLimitAsync(
        DoSelectDbContext context,
        string providerCode,
        decimal maxWeightKg,
        decimal maxSideCm,
        decimal maxTotalCm,
        decimal maxDeclaredValue = 999_999m)
    {
        await EnsureProviderWithLimitAsync(context, providerCode);
        var profile = await context.ShippingProviderProfiles
            .SingleAsync(candidate => candidate.ProviderCode == providerCode);
        var limits = await context.PackageLimitVersions
            .Where(candidate => candidate.ProviderProfileId == profile.Id)
            .ToListAsync();
        context.PackageLimitVersions.RemoveRange(limits);
        context.PackageLimitVersions.Add(new PackageLimitVersion(
            Guid.CreateVersion7(), profile.Id, limits.Max(candidate => candidate.Version) + 1,
            maxWeightKg, maxSideCm, maxSideCm, maxSideCm, maxTotalCm, maxDeclaredValue,
            null, null, DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    public static async Task<Sku> SeedPublishedSkuWithoutDimensionsAsync(
        DoSelectDbContext context, decimal listPrice)
    {
        var sku = await SeedPublishedSkuAsync(context, listPrice);
        // Strip the default dimensions back off: the entity keeps them nullable.
        sku.UpdatePackageDimensions(null, null, null, null, DateTime.UtcNow);
        await context.SaveChangesAsync();
        return sku;
    }

    public static async Task<Sku> SeedPublishedSkuAsync(
        DoSelectDbContext context,
        decimal listPrice,
        bool requiresPrepayment = false,
        decimal weightKg = 1m,
        decimal sideCm = 10m)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(
            Guid.CreateVersion7(), UniqueCode("CAT"), "cat-" + Guid.NewGuid().ToString("N")[..12], "測試分類", null, now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        // 組長 PR #73 review item 3: shipping options now evaluate the cart package against the
        // effective PackageLimitVersion exactly like checkout, and a SKU without dimensions makes
        // the package incomplete (= ineligible everywhere, mirroring checkout's rejection). Seeded
        // SKUs therefore default to a small, well-within-limits parcel; tests probing limits or
        // missing dimensions pass their own values.
        sku.UpdatePackageDimensions(weightKg, sideCm, sideCm, sideCm, now);
        sku.ChangeStatus(SkuStatus.Published, now);
        if (requiresPrepayment)
        {
            sku.UpdateCommercialDetails("測試SKU", listPrice, listPrice * 0.6m, true, true, now);
        }

        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        return sku;
    }

    /// <summary>
    /// Bypasses ICartService on purpose — an assembly-group cart item can only be created
    /// through the build-list "add to cart" action (slice 3), which isn't a dependency this
    /// slice should take on just to seed a test. Inserting the CartItem row directly through
    /// the same Domain constructor production code uses is enough to exercise
    /// EfShippingOptionsService's read side, which only cares that AssemblyGroupKey is non-null.
    /// </summary>
    public static async Task AddAssemblyItemAsync(
        DoSelectDbContext context,
        string guestCartKey,
        Sku sku,
        int quantity = 1)
    {
        var guestHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(guestCartKey));
        var cart = await context.Carts.SingleAsync(candidate =>
            candidate.GuestCartKeyHash != null && candidate.GuestCartKeyHash == guestHash);
        context.CartItems.Add(new CartItem(
            Guid.CreateVersion7(), cart.Id, sku.Id, quantity, Guid.NewGuid(), DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// No isActive parameter: ShippingMethod has no deactivate mutator in v1 (組長's ruling —
    /// 購物車、訂單、付款與物流.md: "第一版宅配與超商取貨固定啟用，不提供後台停用功能"). Returns the seeded entity
    /// (its Code is auto-generated and unique — see <see cref="UniqueMethodCode"/>) so callers
    /// can assert against the actual Code rather than a literal that might collide across tests.
    /// </summary>
    public static async Task<ShippingMethod> SeedShippingMethodAsync(
        DoSelectDbContext context,
        string kind,
        decimal baseFee,
        decimal? freeShippingThreshold,
        bool allowsCod,
        bool requiresPrepayment)
    {
        var now = DateTime.UtcNow;
        var code = UniqueMethodCode(kind);
        var providerCode = kind == ShippingMethodKinds.StorePickup
            ? ShippingProviderCodes.StorePickup
            : ShippingProviderCodes.HomeDelivery;
        var method = new ShippingMethod(
            Guid.CreateVersion7(), code, code, kind, baseFee, freeShippingThreshold, allowsCod, requiresPrepayment, providerCode, now);
        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();
        await EnsureProviderWithLimitAsync(context, providerCode);
        return method;
    }

    public static async Task<ConvenienceStore> SeedStoreAsync(
        DoSelectDbContext context,
        string providerCode,
        string storeCode,
        string city,
        string district,
        bool isActive = true)
    {
        var now = DateTime.UtcNow;
        var store = new ConvenienceStore(
            Guid.CreateVersion7(), providerCode, storeCode, $"{city}{district}門市", $"{city}{district}某路 1 號", city, district, isDemoData: true, now);
        if (!isActive)
        {
            store.SetActive(false, now);
        }

        context.ConvenienceStores.Add(store);
        await context.SaveChangesAsync();
        return store;
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
