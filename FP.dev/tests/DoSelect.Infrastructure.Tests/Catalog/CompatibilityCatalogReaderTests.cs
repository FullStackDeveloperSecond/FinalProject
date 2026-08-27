using DoSelect.Application.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class CompatibilityCatalogReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsOnlyPublishedComponentsAndSourcedHardSpecifications()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var now = DateTime.UtcNow;
        var brand = new Brand(Guid.CreateVersion7(), CatalogAdminFixture.UniqueCode("BRAND"), "測試品牌", now);
        var category = new Category(
            Guid.CreateVersion7(),
            CompatibilityCatalogContract.Categories.CpuCooler,
            "cpu-cooler-" + Guid.NewGuid().ToString("N")[..8],
            "CPU 散熱器",
            null,
            now);
        context.AddRange(brand, category);
        await context.SaveChangesAsync();

        var product = new Product(
            Guid.CreateVersion7(),
            CatalogAdminFixture.UniqueCode("PROD"),
            brand.Id,
            category.Id,
            "散熱器",
            now);
        product.ChangeStatus(ProductStatus.Published, now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(
            Guid.CreateVersion7(),
            CatalogAdminFixture.UniqueCode("SKU"),
            product.Id,
            "散熱器 SKU",
            1_500m,
            900m,
            now);
        sku.ChangeStatus(SkuStatus.Published, now);
        context.Skus.Add(sku);

        var socketDefinition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            category.Id,
            CompatibilityCatalogContract.SemanticKeys.CpuSocket,
            "支援 Socket",
            SpecificationValueType.Option,
            null,
            true,
            true,
            1,
            now,
            allowsMultiple: true);
        var heightDefinition = new SpecificationDefinition(
            Guid.CreateVersion7(),
            category.Id,
            CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm,
            "高度",
            SpecificationValueType.Decimal,
            null,
            true,
            true,
            2,
            now);
        context.SpecificationDefinitions.AddRange(socketDefinition, heightDefinition);
        await context.SaveChangesAsync();
        var am4 = new SpecificationOption(Guid.CreateVersion7(), socketDefinition.Id, "AM4", "AM4", 1, now);
        var am5 = new SpecificationOption(Guid.CreateVersion7(), socketDefinition.Id, "AM5", "AM5", 2, now);
        context.SpecificationOptions.AddRange(am4, am5);

        var reviewer = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"compat-reviewer-{Guid.NewGuid():N}@example.test",
            now);
        context.Users.Add(reviewer);
        await context.SaveChangesAsync();
        var source = new SpecificationSource(
            Guid.CreateVersion7(),
            SpecificationSourceType.Manufacturer,
            "Cooler Manufacturer",
            "https://example.test/cooler/specifications",
            "Socket Support",
            now,
            now,
            reviewer.Id,
            "v1",
            now);
        context.SpecificationSources.Add(source);
        await context.SaveChangesAsync();

        context.SkuSpecificationOptionSelections.AddRange(
            new SkuSpecificationOptionSelection(sku.Id, am4.Id, now, source.Id),
            new SkuSpecificationOptionSelection(sku.Id, am5.Id, now, source.Id));
        context.SkuSpecificationValues.Add(new SkuSpecificationValue(
            sku.Id,
            heightDefinition.Id,
            null,
            158m,
            null,
            null,
            specificationSourceId: null,
            createdAtUtc: now));
        await context.SaveChangesAsync();

        var missingSkuId = Guid.NewGuid();
        var reader = new EfCompatibilityCatalogReader(context);
        var result = await reader.ReadAsync(
            [new CompatibilityItemReference(sku.PublicId, 1), new CompatibilityItemReference(missingSkuId, 1)],
            CancellationToken.None);

        var component = Assert.Single(result.Components);
        Assert.Equal(CompatibilityCatalogContract.Categories.CpuCooler, component.CategoryCode);
        Assert.Equal(["AM4", "AM5"],
            component.Specifications[CompatibilityCatalogContract.SemanticKeys.CpuSocket]
                .OptionCodes!
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            CompatibilityCatalogContract.SemanticKeys.CoolerHeightMm,
            component.Specifications.Keys);
        Assert.Equal([missingSkuId], result.MissingSkuPublicIds);
    }
}
