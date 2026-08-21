using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

[CollectionDefinition(nameof(EfProductSearchServiceCollection))]
public sealed class EfProductSearchServiceCollection : ICollectionFixture<EfProductSearchServiceFixture>;

[Collection(nameof(EfProductSearchServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfProductSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_WhenNoFilters_ReturnsOnlyPublishedInStockProductsWithPublishedDefaultSku()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20),
            CancellationToken.None);

        // RTX4060 is seeded with zero on-hand quantity — default search excludes it now
        // (UC-SEARCH-01: unsellable items never appear in purchasable results).
        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, item.ProductCode);
        Assert.DoesNotContain(result.Items, item => item.ProductCode == EfProductSearchServiceFixture.DraftProductCode);
        Assert.DoesNotContain(
            result.Items,
            item => item.ProductCode == EfProductSearchServiceFixture.UnpublishedDefaultSkuProductCode);
    }

    [Fact]
    public async Task SearchAsync_WhenInStockIsExplicitlyFalse_IncludesOutOfStockItems()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { InStock = false },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.ProductCode == EfProductSearchServiceFixture.Rtx4060Code);
    }

    [Fact]
    public async Task SearchAsync_WhenBrandFilterIsGiven_ReturnsOnlyMatchingBrand()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            // RTX4060 (the only MSI product) is out of stock — opt out of the default
            // in-stock filter so this test isolates brand filtering, not stock filtering.
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { BrandCode = "msi", InStock = false },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4060Code, item.ProductCode);
    }

    [Fact]
    public async Task SearchAsync_WhenKeywordMatchesProductCode_ReturnsOnlyThatProduct()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { Keyword = EfProductSearchServiceFixture.Rtx4070Code },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, item.ProductCode);
    }

    [Fact]
    public async Task SearchAsync_WhenPriceRangeIsGiven_UsesActiveSalePriceAsEffectivePrice()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with
            {
                MinPrice = 17_000m,
                MaxPrice = 19_000m,
            },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, item.ProductCode);
        Assert.Equal(18_000m, item.Price.Sale);
        Assert.Equal(20_000m, item.Price.List);
    }

    [Fact]
    public async Task SearchAsync_WhenInStockFilterIsTrue_ExcludesZeroAvailabilitySkus()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { InStock = true },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, item.ProductCode);
        Assert.Equal(ProductAvailabilityCodes.InStock, item.Availability);
    }

    [Fact]
    public async Task SearchAsync_WhenSortIsPriceAsc_OrdersByEffectivePriceAscending()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { Sort = "priceAsc", InStock = false },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4060Code, result.Items[0].ProductCode);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, result.Items[1].ProductCode);
    }

    [Fact]
    public async Task SearchAsync_WhenDecimalSpecFilterIsGte_FiltersBySpecificationValue()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with
            {
                CategoryCode = "GPU",
                Specs =
                [
                    new SpecFilter(
                        EfProductSearchServiceFixture.GpuLengthSemanticKey,
                        SpecFilterOperator.Gte,
                        "280",
                        null),
                ],
            },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070Code, item.ProductCode);
    }

    [Fact]
    public async Task SearchAsync_WhenSortIsUnsupported_ThrowsCatalogSearchExceptionWithSortCode()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var exception = await Assert.ThrowsAsync<CatalogSearchException>(() => service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { Sort = "cheapest" },
            CancellationToken.None));

        Assert.Equal(CatalogSearchException.ErrorCodes.SortUnsupported, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WhenUnknownSpecSemanticKeyIsGiven_ThrowsCatalogSearchExceptionWithFilterCode()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var exception = await Assert.ThrowsAsync<CatalogSearchException>(() => service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with
            {
                CategoryCode = "GPU",
                Specs = [new SpecFilter("does-not-exist", SpecFilterOperator.Eq, "1", null)],
            },
            CancellationToken.None));

        Assert.Equal(CatalogSearchException.ErrorCodes.FilterUnsupported, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WhenSpecFilterIsGivenWithoutACategory_ThrowsCatalogSearchExceptionWithFilterCode()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var exception = await Assert.ThrowsAsync<CatalogSearchException>(() => service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with
            {
                Specs =
                [
                    new SpecFilter(EfProductSearchServiceFixture.GpuLengthSemanticKey, SpecFilterOperator.Gte, "280", null),
                ],
            },
            CancellationToken.None));

        Assert.Equal(CatalogSearchException.ErrorCodes.FilterUnsupported, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WhenSpecDefinitionBelongsToAnotherCategory_ThrowsCatalogSearchExceptionWithFilterCode()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var exception = await Assert.ThrowsAsync<CatalogSearchException>(() => service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with
            {
                CategoryCode = EfProductSearchServiceFixture.OtherCategoryCode,
                Specs =
                [
                    new SpecFilter(EfProductSearchServiceFixture.GpuLengthSemanticKey, SpecFilterOperator.Gte, "280", null),
                ],
            },
            CancellationToken.None));

        Assert.Equal(CatalogSearchException.ErrorCodes.FilterUnsupported, exception.ErrorCode);
    }

    [Fact]
    public async Task SearchAsync_WhenLocaleHasPublishedTranslation_UsesTranslatedName()
    {
        await using var context = EfProductSearchServiceFixture.CreateContext();
        var service = new EfProductSearchService(context);

        var result = await service.SearchAsync(
            EfProductSearchServiceFixture.EmptyQuery(pageSize: 20) with { Locale = SupportedLocale.JaJp, InStock = false },
            CancellationToken.None);

        var translated = Assert.Single(result.Items, item => item.ProductCode == EfProductSearchServiceFixture.Rtx4070Code);
        var fallback = Assert.Single(result.Items, item => item.ProductCode == EfProductSearchServiceFixture.Rtx4060Code);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4070JapaneseName, translated.Name);
        Assert.Equal(EfProductSearchServiceFixture.Rtx4060ZhTwName, fallback.Name);
    }
}

public sealed class EfProductSearchServiceFixture : IAsyncLifetime
{
    public const string Rtx4070Code = "RTX4070";
    public const string Rtx4060Code = "RTX4060";
    public const string DraftProductCode = "RTX4090";
    public const string UnpublishedDefaultSkuProductCode = "RTX3050";
    public const string GpuLengthSemanticKey = "GPU_LENGTH_MM";
    public const string OtherCategoryCode = "CASE";
    public const string Rtx4060ZhTwName = "RTX 4060 顯示卡";
    public const string Rtx4070JapaneseName = "RTX 4070 グラフィックカード";

    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectCatalogSearchTests;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;

        var adminUser = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), "seed-admin@doselect.test", now);
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
        var adminUserId = adminUser.Id;

        var asus = new Brand(Guid.CreateVersion7(), "ASUS", "華碩", now);
        var msi = new Brand(Guid.CreateVersion7(), "MSI", "微星", now);
        context.Brands.AddRange(asus, msi);
        await context.SaveChangesAsync();

        var category = new Category(Guid.CreateVersion7(), "GPU", "gpu", "顯示卡", null, now);
        var otherCategory = new Category(Guid.CreateVersion7(), OtherCategoryCode, "case", "機殼", null, now);
        context.Categories.AddRange(category, otherCategory);
        await context.SaveChangesAsync();

        var definition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            category.Id,
            GpuLengthSemanticKey,
            "長度 (mm)",
            SpecificationValueType.Decimal,
            null,
            isRequired: false,
            isProtected: true,
            sortOrder: 0,
            now);
        context.SpecificationDefinitions.Add(definition);
        await context.SaveChangesAsync();

        var rtx4070 = new Product(Guid.CreateVersion7(), Rtx4070Code, asus.Id, category.Id, "RTX 4070 顯示卡", now);
        rtx4070.ChangeStatus(ProductStatus.Published, now);
        var rtx4060 = new Product(Guid.CreateVersion7(), Rtx4060Code, msi.Id, category.Id, Rtx4060ZhTwName, now);
        rtx4060.ChangeStatus(ProductStatus.Published, now);
        var draftProduct = new Product(Guid.CreateVersion7(), DraftProductCode, asus.Id, category.Id, "RTX 4090 顯示卡", now);
        var unpublishedSkuProduct = new Product(
            Guid.CreateVersion7(),
            UnpublishedDefaultSkuProductCode,
            asus.Id,
            category.Id,
            "RTX 3050 顯示卡",
            now);
        unpublishedSkuProduct.ChangeStatus(ProductStatus.Published, now);
        context.Products.AddRange(rtx4070, rtx4060, draftProduct, unpublishedSkuProduct);
        await context.SaveChangesAsync();

        var rtx4070Sku = new Sku(
            Guid.CreateVersion7(),
            $"{Rtx4070Code}-A1",
            rtx4070.Id,
            "RTX 4070 標準版",
            20_000m,
            15_000m,
            now);
        rtx4070Sku.ChangeStatus(SkuStatus.Published, now);
        rtx4070Sku.UpdateCommercialDetails(
            rtx4070Sku.NameZhTw,
            rtx4070Sku.ListPrice,
            rtx4070Sku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            now);

        var rtx4060Sku = new Sku(
            Guid.CreateVersion7(),
            $"{Rtx4060Code}-B1",
            rtx4060.Id,
            "RTX 4060 標準版",
            12_000m,
            9_000m,
            now);
        rtx4060Sku.ChangeStatus(SkuStatus.Published, now);
        rtx4060Sku.UpdateCommercialDetails(
            rtx4060Sku.NameZhTw,
            rtx4060Sku.ListPrice,
            rtx4060Sku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            now);

        var draftSku = new Sku(
            Guid.CreateVersion7(),
            $"{DraftProductCode}-C1",
            draftProduct.Id,
            "RTX 4090 標準版",
            45_000m,
            35_000m,
            now);
        draftSku.ChangeStatus(SkuStatus.Published, now);
        draftSku.UpdateCommercialDetails(
            draftSku.NameZhTw,
            draftSku.ListPrice,
            draftSku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            now);

        var unpublishedDefaultSku = new Sku(
            Guid.CreateVersion7(),
            $"{UnpublishedDefaultSkuProductCode}-D1",
            unpublishedSkuProduct.Id,
            "RTX 3050 標準版",
            9_000m,
            7_000m,
            now);
        unpublishedDefaultSku.UpdateCommercialDetails(
            unpublishedDefaultSku.NameZhTw,
            unpublishedDefaultSku.ListPrice,
            unpublishedDefaultSku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            now);

        context.Skus.AddRange(rtx4070Sku, rtx4060Sku, draftSku, unpublishedDefaultSku);
        await context.SaveChangesAsync();

        context.InventoryBalances.AddRange(
            new InventoryBalance(Guid.CreateVersion7(), rtx4070Sku.Id, onHandQuantity: 10, reorderLevel: 2, now),
            new InventoryBalance(Guid.CreateVersion7(), rtx4060Sku.Id, onHandQuantity: 0, reorderLevel: 2, now));

        var salePrice = new SalePrice(
            Guid.CreateVersion7(),
            rtx4070Sku.Id,
            18_000m,
            now.AddDays(-1),
            now.AddDays(30),
            adminUserId,
            now);
        salePrice.ChangeStatus(SalePriceStatus.Active, now);
        context.SalePrices.Add(salePrice);

        context.SkuSpecificationValues.AddRange(
            new SkuSpecificationValue(rtx4070Sku.Id, definition.Id, null, 300m, null, null, null, now),
            new SkuSpecificationValue(rtx4060Sku.Id, definition.Id, null, 250m, null, null, null, now));

        var translation = new ProductTranslation(
            rtx4070.Id,
            SupportedLocale.JaJp,
            Rtx4070JapaneseName,
            null,
            now);
        translation.Review(adminUserId, publish: true, now);
        context.ProductTranslations.Add(translation);

        await context.SaveChangesAsync();
    }

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

    public static ProductSearchQuery EmptyQuery(int pageSize) => new(
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        null,
        1,
        pageSize,
        SupportedLocale.ZhTw);
}
