using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

[CollectionDefinition(nameof(CatalogAdminCollection))]
public sealed class CatalogAdminCollection : ICollectionFixture<CatalogAdminFixture>;

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class BrandAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenCodeIsNew_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfBrandAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("BRAND");

        var brand = await service.CreateAsync(
            new CreateBrandRequest(code, "測試品牌", null, null, 0, true),
            CancellationToken.None);

        Assert.Equal(code.ToUpperInvariant(), brand.Code);
        Assert.True(brand.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WhenCodeAlreadyExists_ThrowsBrandCodeDuplicate()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfBrandAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("BRAND");
        await service.CreateAsync(new CreateBrandRequest(code, "測試品牌", null, null, 0, true), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            new CreateBrandRequest(code, "另一個名稱", null, null, 0, true),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.BrandCodeDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenRowVersionIsStale_ThrowsConcurrencyConflict()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfBrandAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("BRAND");
        var brand = await service.CreateAsync(new CreateBrandRequest(code, "測試品牌", null, null, 0, true), CancellationToken.None);

        await service.UpdateAsync(
            brand.PublicId,
            new UpdateBrandRequest("第一次更新", null, null, 0, true, brand.RowVersion),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.UpdateAsync(
            brand.PublicId,
            new UpdateBrandRequest("第二次更新(用舊版本)", null, null, 0, true, brand.RowVersion),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }
}

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CategoryAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WithParentCategory_ResolvesParentPublicId()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfCategoryAdminService(context);
        var parentCode = CatalogAdminFixture.UniqueCode("CAT");
        var parent = await service.CreateAsync(
            new CreateCategoryRequest(parentCode, "父分類", "parent-" + parentCode, null, null, 0, true),
            CancellationToken.None);

        var childCode = CatalogAdminFixture.UniqueCode("CAT");
        var child = await service.CreateAsync(
            new CreateCategoryRequest(childCode, "子分類", "child-" + childCode, null, parent.PublicId, 0, true),
            CancellationToken.None);

        Assert.Equal(parent.PublicId, child.ParentCategoryPublicId);
    }

    [Fact]
    public async Task UpdateAsync_WhenParentIsSelf_ThrowsCategoryParentInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfCategoryAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("CAT");
        var category = await service.CreateAsync(
            new CreateCategoryRequest(code, "分類", "slug-" + code, null, null, 0, true),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.UpdateAsync(
            category.PublicId,
            new UpdateCategoryRequest("分類", "slug-" + code, null, category.PublicId, 0, true, category.RowVersion),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.CategoryParentInvalid, exception.ErrorCode);
    }
}

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class TagAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenCodeIsNew_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfTagAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("TAG");

        var tag = await service.CreateAsync(new CreateTagRequest(code, "新品", 0, true), CancellationToken.None);

        Assert.Equal(code.ToUpperInvariant(), tag.Code);
    }
}

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidReferences_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("PROD");

        var product = await service.CreateAsync(
            new CreateProductRequest(code, "測試商品", brand.PublicId, category.PublicId, "描述", 12, [], "Draft"),
            CancellationToken.None);

        Assert.Equal(code.ToUpperInvariant(), product.ProductCode);
        Assert.Equal("Draft", product.Status);
        Assert.Empty(product.Skus);
    }

    [Fact]
    public async Task CreateAsync_WhenProductCodeAlreadyExists_ThrowsProductCodeDuplicate()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("PROD");
        await service.CreateAsync(
            new CreateProductRequest(code, "測試商品", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            new CreateProductRequest(code, "另一個商品", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ProductCodeDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenBrandDoesNotExist_ThrowsReferenceNotFound()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (_, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            new CreateProductRequest(
                CatalogAdminFixture.UniqueCode("PROD"),
                "測試商品",
                Guid.CreateVersion7(),
                category.PublicId,
                null,
                null,
                [],
                "Draft"),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ReferenceNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_WhenFilteredByBrandCode_ReturnsOnlyMatchingProducts()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var (otherBrand, _, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var matchingCode = CatalogAdminFixture.UniqueCode("PROD");
        await service.CreateAsync(
            new CreateProductRequest(matchingCode, "符合", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);
        await service.CreateAsync(
            new CreateProductRequest(
                CatalogAdminFixture.UniqueCode("PROD"),
                "不符合",
                otherBrand.PublicId,
                category.PublicId,
                null,
                null,
                [],
                "Draft"),
            CancellationToken.None);

        var result = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Contains(result.Items, item => item.ProductCode == matchingCode.ToUpperInvariant());
        Assert.All(result.Items, item => Assert.Equal(brand.Code, item.Brand.Code));
    }
}

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class SkuAdminServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidSpecificationValues_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, definition) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var skuCode = CatalogAdminFixture.UniqueCode("SKU");

        var sku = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                skuCode,
                "標準版",
                10_000m,
                7_000m,
                null,
                null,
                null,
                null,
                "Draft",
                IsDefault: true,
                RequiresPrepayment: false,
                [new SpecValueInput(definition.SemanticKey, "Decimal", null, 300m, null, null)]),
            CancellationToken.None);

        Assert.Equal(skuCode.ToUpperInvariant(), sku.SkuCode);
        Assert.True(sku.IsDefault);
        var spec = Assert.Single(sku.Specifications);
        Assert.Equal(300m, spec.DecimalValue);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownSemanticKey_ThrowsSpecificationInvalid()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "標準版",
                10_000m,
                7_000m,
                null,
                null,
                null,
                null,
                "Draft",
                false,
                false,
                [new SpecValueInput("does-not-exist", "String", "x", null, null, null)]),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SpecificationInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_SecondDefaultSku_ClearsPreviousDefault()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);

        var first = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "第一版",
                10_000m,
                7_000m,
                null,
                null,
                null,
                null,
                "Draft",
                true,
                false,
                []),
            CancellationToken.None);

        var second = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "第二版",
                12_000m,
                8_000m,
                null,
                null,
                null,
                null,
                "Draft",
                true,
                false,
                []),
            CancellationToken.None);

        var reloadedFirst = await service.GetByPublicIdAsync(first.PublicId, CancellationToken.None);

        Assert.False(reloadedFirst!.IsDefault);
        Assert.True(second.IsDefault);
    }

    [Fact]
    public async Task DeleteAsync_WhenSkuHasInventoryBalance_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var sku = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "標準版",
                10_000m,
                7_000m,
                null,
                null,
                null,
                null,
                "Draft",
                false,
                false,
                []),
            CancellationToken.None);
        var skuId = await context.Skus
            .Where(candidate => candidate.PublicId == sku.PublicId)
            .Select(candidate => candidate.Id)
            .FirstAsync();
        context.InventoryBalances.Add(new InventoryBalance(Guid.CreateVersion7(), skuId, 5, 1, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenDraftAndUnreferenced_Succeeds()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var sku = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "誤建",
                10_000m,
                7_000m,
                null,
                null,
                null,
                null,
                "Draft",
                false,
                false,
                []),
            CancellationToken.None);

        await service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None);

        var reloaded = await service.GetByPublicIdAsync(sku.PublicId, CancellationToken.None);
        Assert.Null(reloaded);
    }
}

public sealed class CatalogAdminFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectCatalogAdminTests;Trusted_Connection=True;" +
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

    // Guid.NewGuid() (random) is used here instead of Guid.CreateVersion7() (time-ordered)
    // because CreateVersion7's leading hex characters encode a millisecond timestamp and
    // can collide when this helper is called more than once within the same millisecond,
    // e.g. when a test seeds two brands back-to-back.
    public static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public static async Task<(Brand Brand, Category Category, SpecificationDefinition Definition)> SeedCatalogAsync(
        DoSelectDbContext context)
    {
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), UniqueCode("BRAND"), "測試品牌", now);
        context.Brands.Add(brand);
        var category = new Category(Guid.CreateVersion7(), UniqueCode("CAT"), "cat-" + Guid.NewGuid().ToString("N")[..12], "測試分類", null, now);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var definition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            category.Id,
            UniqueCode("SPEC"),
            "長度",
            SpecificationValueType.Decimal,
            null,
            isRequired: false,
            isProtected: false,
            sortOrder: 0,
            now);
        context.SpecificationDefinitions.Add(definition);
        await context.SaveChangesAsync();

        return (brand, category, definition);
    }

    public static async Task<Product> CreateProductAsync(DoSelectDbContext context, Brand brand, Category category)
    {
        var now = DateTime.UtcNow;
        var product = new Product(Guid.CreateVersion7(), UniqueCode("PROD"), brand.Id, category.Id, "測試商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}
