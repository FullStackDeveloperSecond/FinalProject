using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Shopping;

public sealed class CartServiceFixture : IAsyncLifetime
{
    private static readonly string ConnectionString =
        global::DoSelect.Infrastructure.Tests.SqlServerTestConnection.Build("DoSelectCartServiceTests");

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

    // Guid.NewGuid() (random), not Guid.CreateVersion7() (time-ordered), because
    // CreateVersion7's leading hex characters encode a millisecond timestamp and can
    // collide when a test seeds more than one row within the same millisecond.
    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public static string UniqueGuestKey() => $"guest-{Guid.NewGuid():N}";

    // Carts.OwnerUserId has a foreign key to AspNetUsers, so member-cart tests need a real
    // seeded ApplicationUser id rather than an arbitrary string.
    public static async Task<string> SeedMemberUserIdAsync(DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>
    /// <paramref name="availableQuantity"/> defaults to a generous 1000 (not null): after PR
    /// #28's review a missing InventoryBalance row means "insufficient_stock", so any test
    /// that isn't specifically exercising that edge case needs a real balance row to avoid
    /// tripping over it incidentally. Pass <c>null</c> explicitly to test the no-balance-row
    /// case itself.
    /// </summary>
    public async Task<Sku> SeedPublishedSkuAsync(
        DoSelectDbContext context,
        decimal listPrice,
        bool publish = true,
        int? availableQuantity = 1000)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(
            Guid.CreateVersion7(),
            UniqueCode("CAT"),
            "cat-" + Guid.NewGuid().ToString("N")[..12],
            "測試分類",
            null,
            now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var sku = new Sku(Guid.CreateVersion7(), UniqueCode("SKU"), product.Id, "測試SKU", listPrice, listPrice * 0.6m, now);
        if (publish)
        {
            sku.ChangeStatus(SkuStatus.Published, now);
        }

        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        if (availableQuantity.HasValue)
        {
            context.InventoryBalances.Add(new InventoryBalance(
                Guid.CreateVersion7(), sku.Id, onHandQuantity: availableQuantity.Value, reorderLevel: 0, now));
            await context.SaveChangesAsync();
        }

        return sku;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
