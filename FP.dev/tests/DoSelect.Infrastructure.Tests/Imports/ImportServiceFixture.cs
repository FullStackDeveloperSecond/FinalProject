using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class ImportServiceFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectImportServiceTests;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

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

    public static async Task<(Brand Brand, Category Category)> SeedBrandAndCategoryAsync(DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        var category = new Category(
            Guid.CreateVersion7(), UniqueCode("CAT"), "cat-" + Guid.NewGuid().ToString("N")[..12], "測試分類", null, now);
        context.Brands.Add(brand);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return (brand, category);
    }

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    // ImportBatches.CreatedByAdminUserId has a real FK to AspNetUsers — a plain string like
    // "admin-1" fails with a foreign key violation, not a domain validation error.
    public static async Task<string> SeedAdminUserIdAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(nameof(ImportServiceCollection))]
public sealed class ImportServiceCollection : ICollectionFixture<ImportServiceFixture>;
