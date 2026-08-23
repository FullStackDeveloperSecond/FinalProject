using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
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

    /// <summary>
    /// Regression test: CreateAsync used to SaveChanges the Product before validating its
    /// tags, so an invalid tag reference threw after the Product row already existed.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTagIsInvalid_DoesNotPersistTheProduct()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var code = CatalogAdminFixture.UniqueCode("PROD");

        await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            new CreateProductRequest(
                code,
                "測試商品",
                brand.PublicId,
                category.PublicId,
                null,
                null,
                [Guid.NewGuid()],
                "Draft"),
            CancellationToken.None));

        await using var verifyContext = CatalogAdminFixture.CreateContext();
        var exists = await verifyContext.Products.AnyAsync(p => p.ProductCode == code.ToUpperInvariant());
        Assert.False(exists);
    }

    [Fact]
    public async Task ListAsync_WhenSortIsCodeAscOrCodeDesc_OrdersByProductCode()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var codeA = CatalogAdminFixture.UniqueCode("PROD");
        var codeB = CatalogAdminFixture.UniqueCode("PROD");
        await service.CreateAsync(
            new CreateProductRequest(codeA, "A", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);
        await service.CreateAsync(
            new CreateProductRequest(codeB, "B", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);
        var expectedAscending = new[] { codeA.ToUpperInvariant(), codeB.ToUpperInvariant() }
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var ascending = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, AdminProductSortOptions.CodeAsc, 1, 20),
            CancellationToken.None);
        var descending = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, AdminProductSortOptions.CodeDesc, 1, 20),
            CancellationToken.None);

        Assert.Equal(expectedAscending, ascending.Items.Select(item => item.ProductCode));
        Assert.Equal(expectedAscending.Reverse(), descending.Items.Select(item => item.ProductCode));
    }

    [Fact]
    public async Task ListAsync_WhenSortIsUpdatedAscOrUpdatedDesc_OrdersByUpdatedAtUtc()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var service = new EfProductAdminService(context);
        var older = await service.CreateAsync(
            new CreateProductRequest(CatalogAdminFixture.UniqueCode("PROD"), "先建立", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);
        var newer = await service.CreateAsync(
            new CreateProductRequest(CatalogAdminFixture.UniqueCode("PROD"), "後建立", brand.PublicId, category.PublicId, null, null, [], "Draft"),
            CancellationToken.None);

        // Touch "older" again so it becomes the more-recently-updated of the two, decoupling
        // update-recency order from creation order.
        older = await service.UpdateAsync(
            older.PublicId,
            new UpdateProductRequest("先建立(更新過)", brand.PublicId, category.PublicId, null, null, [], "Draft", older.RowVersion),
            CancellationToken.None);

        var ascending = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, AdminProductSortOptions.UpdatedAsc, 1, 20),
            CancellationToken.None);
        var descending = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, AdminProductSortOptions.UpdatedDesc, 1, 20),
            CancellationToken.None);

        Assert.Equal(newer.ProductCode, ascending.Items[0].ProductCode);
        Assert.Equal(older.ProductCode, descending.Items[0].ProductCode);
    }

    /// <summary>
    /// Regression test: stockState used to be applied in memory after Count/Skip/Take, which
    /// corrupted totalCount and could short a page. Filters two out-of-stock and one in-stock
    /// product with pageSize=1 to prove the filter runs before paging.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenStockStateIsOutOfStock_FiltersBeforePagingSoTotalCountAndPagesAreCorrect()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var skuService = new EfSkuAdminService(context);
        var service = new EfProductAdminService(context);

        var outOfStockA = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var outOfStockB = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var inStock = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        await CreateSkuWithOnHandAsync(context, skuService, outOfStockA, onHand: 0);
        await CreateSkuWithOnHandAsync(context, skuService, outOfStockB, onHand: 0);
        await CreateSkuWithOnHandAsync(context, skuService, inStock, onHand: 5);

        var page1 = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, AdminStockStates.OutOfStock, null, 1, 1),
            CancellationToken.None);
        var page2 = await service.ListAsync(
            new AdminProductQuery(null, [brand.Code], null, null, AdminStockStates.OutOfStock, null, 2, 1),
            CancellationToken.None);

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page2.TotalCount);
        var returnedCodes = new[] { Assert.Single(page1.Items).ProductCode, Assert.Single(page2.Items).ProductCode };
        Assert.Contains(outOfStockA.ProductCode, returnedCodes);
        Assert.Contains(outOfStockB.ProductCode, returnedCodes);
        Assert.DoesNotContain(inStock.ProductCode, returnedCodes);
    }

    [Fact]
    public async Task ListAsync_WhenSortIsInvalid_ThrowsValidationFailed()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfProductAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.ListAsync(
            new AdminProductQuery(null, null, null, null, null, "not-a-real-sort", 1, 20),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_WhenStockStateIsInvalid_ThrowsValidationFailed()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfProductAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.ListAsync(
            new AdminProductQuery(null, null, null, null, "not-a-real-state", null, 1, 20),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_WhenStatusesContainsInvalidValue_ThrowsValidationFailed()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var service = new EfProductAdminService(context);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() => service.ListAsync(
            new AdminProductQuery(null, null, null, ["Draft", "not-a-real-status"], null, null, 1, 20),
            CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    private static async Task CreateSkuWithOnHandAsync(
        DoSelectDbContext context,
        EfSkuAdminService skuService,
        Product product,
        int onHand)
    {
        var sku = await skuService.CreateAsync(
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
        context.InventoryBalances.Add(new InventoryBalance(Guid.CreateVersion7(), skuId, onHand, 0, DateTime.UtcNow));
        await context.SaveChangesAsync();
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

    /// <summary>
    /// Regression test: CreateAsync used to SaveChanges the SKU (and any pending
    /// default-clearing of the previous default) before validating specifications, so an
    /// invalid specification threw after the SKU already existed and the old default had
    /// already been cleared.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenSpecificationIsInvalid_DoesNotPersistTheSkuOrClearThePreviousDefault()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);

        var existingDefault = await service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                CatalogAdminFixture.UniqueCode("SKU"),
                "原本的預設版",
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

        var failingCode = CatalogAdminFixture.UniqueCode("SKU");
        await Assert.ThrowsAsync<CatalogWriteException>(() => service.CreateAsync(
            product.PublicId,
            new CreateSkuRequest(
                failingCode,
                "新的預設版",
                12_000m,
                8_000m,
                null,
                null,
                null,
                null,
                "Draft",
                IsDefault: true,
                RequiresPrepayment: false,
                [new SpecValueInput("does-not-exist", "String", "x", null, null, null)]),
            CancellationToken.None));

        await using var verifyContext = CatalogAdminFixture.CreateContext();
        var skuExists = await verifyContext.Skus.AnyAsync(s => s.SkuCode == failingCode.ToUpperInvariant());
        Assert.False(skuExists);

        var reloadedExistingDefault = await service.GetByPublicIdAsync(existingDefault.PublicId, CancellationToken.None);
        Assert.True(reloadedExistingDefault!.IsDefault);
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
    public async Task DeleteAsync_WhenSkuHasOrderItem_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var (sku, skuId) = await CreateDraftSkuAsync(context, service, product);
        var now = DateTime.UtcNow;

        var shippingProfile = new ShippingProviderProfile(
            Guid.CreateVersion7(), CatalogAdminFixture.UniqueCode("SHIP"), 1, "Active", null, null, "{}", 1, now);
        context.ShippingProviderProfiles.Add(shippingProfile);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.CreateVersion7(),
            new OrderCreation(
                CatalogAdminFixture.UniqueCode("ORDER"),
                null,
                "guest@doselect.test",
                OrderStatus.PendingPayment,
                PaymentStatus.AwaitingPayment,
                FulfillmentStatus.Pending,
                AssemblyStatus.NotRequired,
                1000m,
                0m,
                0m,
                0m,
                1000m,
                "Guest",
                "0912345678",
                "guest@doselect.test",
                null,
                null,
                null,
                null,
                null,
                "HOME_DELIVERY",
                shippingProfile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                CatalogAdminFixture.UniqueCode("IDEM"),
                null),
            now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        context.OrderItems.Add(new OrderItem(
            Guid.CreateVersion7(),
            order.Id,
            skuId,
            sku.SkuCode,
            product.NameZhTw,
            sku.NameZhTw,
            1,
            1000m,
            1000m,
            1000m,
            700m,
            1000m,
            0m,
            1000m,
            null,
            0,
            now));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenSkuHasInventoryReconciliationCase_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var (sku, skuId) = await CreateDraftSkuAsync(context, service, product);
        var now = DateTime.UtcNow;

        context.InventoryReconciliationCases.Add(
            new InventoryReconciliationCase(Guid.CreateVersion7(), skuId, 10, 8, 0, 0, now, now));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenSkuHasProductImage_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var (sku, skuId) = await CreateDraftSkuAsync(context, service, product);
        var now = DateTime.UtcNow;

        context.ProductImages.Add(new ProductImage(
            Guid.CreateVersion7(),
            product.Id,
            skuId,
            CatalogAdminFixture.UniqueCode("IMG"),
            "photo.jpg",
            "image/jpeg",
            1024,
            800,
            600,
            new byte[32],
            "測試圖片",
            now));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenSkuHasSkuTranslation_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var (sku, skuId) = await CreateDraftSkuAsync(context, service, product);
        var now = DateTime.UtcNow;

        context.SkuTranslations.Add(new SkuTranslation(skuId, SupportedLocale.JaJp, "テスト SKU", now));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenSkuHasSalePrice_ThrowsSkuDeleteReferenced()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var service = new EfSkuAdminService(context);
        var (sku, skuId) = await CreateDraftSkuAsync(context, service, product);
        var now = DateTime.UtcNow;

        var adminUser = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", now);
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        context.SalePrices.Add(new SalePrice(
            Guid.CreateVersion7(), skuId, 900m, now.AddDays(-1), now.AddDays(30), adminUser.Id, now));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(
            () => service.DeleteAsync(sku.PublicId, sku.RowVersion, CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.SkuDeleteReferenced, exception.ErrorCode);
    }

    private static async Task<(SkuDto Sku, long SkuId)> CreateDraftSkuAsync(
        DoSelectDbContext context,
        EfSkuAdminService service,
        Product product)
    {
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
        return (sku, skuId);
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
