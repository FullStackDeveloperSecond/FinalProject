using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

[CollectionDefinition(nameof(CatalogDetailAndFilterOptionsCollection))]
public sealed class CatalogDetailAndFilterOptionsCollection : ICollectionFixture<CatalogDetailAndFilterOptionsFixture>;

[Collection(nameof(CatalogDetailAndFilterOptionsCollection))]
public sealed class EfProductDetailServiceTests
{
    [Fact]
    public async Task GetByPublicIdAsync_WhenProductIsPublished_ReturnsDetailWithSkusTagsAndSpecificationGroups()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfProductDetailService(context);

        var detail = await service.GetByPublicIdAsync(
            CatalogDetailAndFilterOptionsFixture.ProductPublicId,
            SupportedLocale.ZhTw,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(CatalogDetailAndFilterOptionsFixture.ProductCode, detail!.ProductCode);
        Assert.Equal(2, detail.Skus.Count);
        Assert.Contains(detail.Skus, sku => sku.IsDefault);
        Assert.Single(detail.Tags, tag => tag.Code == CatalogDetailAndFilterOptionsFixture.TagCode);
        Assert.Contains(
            detail.SpecificationGroups,
            group => group.SemanticKey == CatalogDetailAndFilterOptionsFixture.LengthSemanticKey &&
                group.Values.Count == 2);
        Assert.Contains(
            detail.SpecificationGroups,
            group => group.SemanticKey == CatalogDetailAndFilterOptionsFixture.InterfaceSemanticKey);
    }

    [Fact]
    public async Task GetByPublicIdAsync_WhenProductDoesNotExist_ReturnsNull()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfProductDetailService(context);

        var detail = await service.GetByPublicIdAsync(Guid.CreateVersion7(), SupportedLocale.ZhTw, CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetByPublicIdAsync_WhenProductIsDraft_ReturnsNull()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfProductDetailService(context);

        var detail = await service.GetByPublicIdAsync(
            CatalogDetailAndFilterOptionsFixture.DraftProductPublicId,
            SupportedLocale.ZhTw,
            CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetByPublicIdAsync_WhenLocaleHasPublishedTranslation_UsesTranslatedNameAndDescription()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfProductDetailService(context);

        var detail = await service.GetByPublicIdAsync(
            CatalogDetailAndFilterOptionsFixture.ProductPublicId,
            SupportedLocale.JaJp,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(CatalogDetailAndFilterOptionsFixture.ProductJapaneseName, detail!.Name);
        Assert.Equal(CatalogDetailAndFilterOptionsFixture.ProductJapaneseDescription, detail.Description);
    }
}

[Collection(nameof(CatalogDetailAndFilterOptionsCollection))]
public sealed class EfCatalogFilterOptionsServiceTests
{
    [Fact]
    public async Task GetAsync_WithoutCategory_ReturnsTopLevelCategoriesAndBrandsWithPublishedProductsOnly()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfCatalogFilterOptionsService(context);

        var result = await service.GetAsync(
            new CatalogFilterOptionsQuery(null, SupportedLocale.ZhTw),
            CancellationToken.None);

        Assert.Contains(result.Categories, category => category.Code == CatalogDetailAndFilterOptionsFixture.GpuCategoryCode);
        Assert.DoesNotContain(
            result.Categories,
            category => category.Code == CatalogDetailAndFilterOptionsFixture.GpuProCategoryCode);
        Assert.Contains(result.Brands, brand => brand.Code == CatalogDetailAndFilterOptionsFixture.AsusBrandCode);
        Assert.DoesNotContain(
            result.Brands,
            brand => brand.Code == CatalogDetailAndFilterOptionsFixture.EmptyBrandCode);
        Assert.NotNull(result.PriceRange);
        Assert.Empty(result.SpecificationFilters);
        Assert.Equal(ProductSortOptions.All.Count, result.SortOptions.Count);
    }

    [Fact]
    public async Task GetAsync_WithCategory_ReturnsChildCategoryAndSpecificationFilters()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfCatalogFilterOptionsService(context);

        var result = await service.GetAsync(
            new CatalogFilterOptionsQuery(CatalogDetailAndFilterOptionsFixture.GpuCategoryCode, SupportedLocale.ZhTw),
            CancellationToken.None);

        Assert.Single(result.Categories, category => category.Code == CatalogDetailAndFilterOptionsFixture.GpuProCategoryCode);
        Assert.Contains(
            result.SpecificationFilters,
            filter => filter.SemanticKey == CatalogDetailAndFilterOptionsFixture.LengthSemanticKey &&
                filter.Operators.Contains(SpecFilterOperatorCodes.Gte));
        var interfaceFilter = Assert.Single(
            result.SpecificationFilters,
            filter => filter.SemanticKey == CatalogDetailAndFilterOptionsFixture.InterfaceSemanticKey);
        Assert.NotNull(interfaceFilter.Options);
        Assert.Contains(interfaceFilter.Options!, option => option.Code == CatalogDetailAndFilterOptionsFixture.Pcie4OptionCode);
    }

    [Fact]
    public async Task GetAsync_WhenCategoryIsUnknown_ThrowsFilterUnsupported()
    {
        await using var context = CatalogDetailAndFilterOptionsFixture.CreateContext();
        var service = new EfCatalogFilterOptionsService(context);

        var exception = await Assert.ThrowsAsync<CatalogSearchException>(() => service.GetAsync(
            new CatalogFilterOptionsQuery("does-not-exist", SupportedLocale.ZhTw),
            CancellationToken.None));

        Assert.Equal(CatalogSearchException.ErrorCodes.FilterUnsupported, exception.ErrorCode);
    }
}

public sealed class CatalogDetailAndFilterOptionsFixture : IAsyncLifetime
{
    public const string ProductCode = "RTX4070-DETAIL";
    public const string TagCode = "NEW";
    public const string LengthSemanticKey = "GPU_LENGTH_MM";
    public const string InterfaceSemanticKey = "GPU_INTERFACE";
    public const string Pcie4OptionCode = "PCIE4";
    public const string GpuCategoryCode = "GPU-DETAIL";
    public const string GpuProCategoryCode = "GPU-DETAIL-PRO";
    public const string AsusBrandCode = "ASUS-DETAIL";
    public const string EmptyBrandCode = "EMPTY-DETAIL";
    public const string ProductJapaneseName = "RTX 4070 グラフィックカード";
    public const string ProductJapaneseDescription = "高性能グラフィックカード";

    public static Guid ProductPublicId { get; private set; }

    public static Guid DraftProductPublicId { get; private set; }

    private const string ConnectionString =
        "Server=.\\SQL2025;Database=DoSelectCatalogDetailTests;Trusted_Connection=True;" +
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

        var asus = new Brand(Guid.CreateVersion7(), AsusBrandCode, "華碩", now);
        var emptyBrand = new Brand(Guid.CreateVersion7(), EmptyBrandCode, "無商品品牌", now);
        context.Brands.AddRange(asus, emptyBrand);
        await context.SaveChangesAsync();

        var gpuCategory = new Category(Guid.CreateVersion7(), GpuCategoryCode, "gpu-detail", "顯示卡", null, now);
        context.Categories.Add(gpuCategory);
        await context.SaveChangesAsync();

        var gpuProCategory = new Category(
            Guid.CreateVersion7(),
            GpuProCategoryCode,
            "gpu-detail-pro",
            "專業顯示卡",
            gpuCategory.Id,
            now);
        context.Categories.Add(gpuProCategory);
        await context.SaveChangesAsync();

        var lengthDefinition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            gpuCategory.Id,
            LengthSemanticKey,
            "長度 (mm)",
            SpecificationValueType.Decimal,
            null,
            isRequired: false,
            isProtected: true,
            sortOrder: 0,
            now);
        var interfaceDefinition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            gpuCategory.Id,
            InterfaceSemanticKey,
            "介面",
            SpecificationValueType.Option,
            null,
            isRequired: false,
            isProtected: true,
            sortOrder: 1,
            now);
        context.SpecificationDefinitions.AddRange(lengthDefinition, interfaceDefinition);
        await context.SaveChangesAsync();

        var pcie4 = new SpecificationOption(Guid.CreateVersion7(), interfaceDefinition.Id, Pcie4OptionCode, "PCIe 4.0", 0, now);
        var pcie5 = new SpecificationOption(Guid.CreateVersion7(), interfaceDefinition.Id, "PCIE5", "PCIe 5.0", 1, now);
        context.SpecificationOptions.AddRange(pcie4, pcie5);
        await context.SaveChangesAsync();

        var product = new Product(Guid.CreateVersion7(), ProductCode, asus.Id, gpuCategory.Id, "RTX 4070 顯示卡", now);
        product.UpdateDetails(
            asus.Id,
            gpuCategory.Id,
            product.NameZhTw,
            "高效能顯示卡",
            warrantyMonths: 36,
            isFeatured: true,
            now);
        product.ChangeStatus(ProductStatus.Published, now);

        var draftProduct = new Product(Guid.CreateVersion7(), "RTX4090-DETAIL-DRAFT", asus.Id, gpuCategory.Id, "草稿商品", now);
        context.Products.AddRange(product, draftProduct);
        await context.SaveChangesAsync();
        ProductPublicId = product.PublicId;
        DraftProductPublicId = draftProduct.PublicId;

        var defaultSku = new Sku(Guid.CreateVersion7(), $"{ProductCode}-A", product.Id, "標準版", 20_000m, 15_000m, now);
        defaultSku.ChangeStatus(SkuStatus.Published, now);
        defaultSku.UpdateCommercialDetails(
            defaultSku.NameZhTw,
            defaultSku.ListPrice,
            defaultSku.UnitCost,
            isDefault: true,
            requiresPrepayment: false,
            now);

        var otherSku = new Sku(Guid.CreateVersion7(), $"{ProductCode}-B", product.Id, "超頻版", 22_000m, 17_000m, now);
        otherSku.ChangeStatus(SkuStatus.Published, now);
        otherSku.UpdateCommercialDetails(
            otherSku.NameZhTw,
            otherSku.ListPrice,
            otherSku.UnitCost,
            isDefault: false,
            requiresPrepayment: false,
            now);

        context.Skus.AddRange(defaultSku, otherSku);
        await context.SaveChangesAsync();

        context.InventoryBalances.AddRange(
            new InventoryBalance(Guid.CreateVersion7(), defaultSku.Id, onHandQuantity: 10, reorderLevel: 2, now),
            new InventoryBalance(Guid.CreateVersion7(), otherSku.Id, onHandQuantity: 5, reorderLevel: 2, now));

        context.SkuSpecificationValues.AddRange(
            new SkuSpecificationValue(defaultSku.Id, lengthDefinition.Id, null, 300m, null, null, null, now),
            new SkuSpecificationValue(otherSku.Id, lengthDefinition.Id, null, 320m, null, null, null, now),
            new SkuSpecificationValue(defaultSku.Id, interfaceDefinition.Id, null, null, null, pcie4.Id, null, now),
            new SkuSpecificationValue(otherSku.Id, interfaceDefinition.Id, null, null, null, pcie5.Id, null, now));

        var tag = new Tag(Guid.CreateVersion7(), TagCode, "新品", now);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        context.ProductTags.Add(new ProductTag(product.Id, tag.Id, now));

        var translation = new ProductTranslation(product.Id, SupportedLocale.JaJp, ProductJapaneseName, ProductJapaneseDescription, now);
        translation.Review(adminUser.Id, publish: true, now);
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
}
