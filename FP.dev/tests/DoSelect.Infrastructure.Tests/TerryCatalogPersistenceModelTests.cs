using DoSelect.Domain.Builds;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;
using DoSelect.Domain.Members;
using DoSelect.Domain.Reviews;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Shopping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests;

public sealed class TerryCatalogPersistenceModelTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    public static TheoryData<Type, string> CatalogTables => new()
    {
        { typeof(Brand), "Brands" },
        { typeof(Category), "Categories" },
        { typeof(Product), "Products" },
        { typeof(Sku), "Skus" },
        { typeof(Tag), "Tags" },
        { typeof(ProductTag), "ProductTags" },
        { typeof(BrandTranslation), "BrandTranslations" },
        { typeof(CategoryTranslation), "CategoryTranslations" },
        { typeof(ProductTranslation), "ProductTranslations" },
        { typeof(SkuTranslation), "SkuTranslations" },
        { typeof(SpecificationDefinitionTranslation), "SpecificationDefinitionTranslations" },
        { typeof(SpecificationOptionTranslation), "SpecificationOptionTranslations" },
        { typeof(ProductImage), "ProductImages" },
        { typeof(MeasurementUnit), "MeasurementUnits" },
        { typeof(SpecificationDefinition), "SpecificationDefinitions" },
        { typeof(SpecificationOption), "SpecificationOptions" },
        { typeof(SpecificationSource), "SpecificationSources" },
        { typeof(SkuSpecificationValue), "SkuSpecificationValues" },
        { typeof(SalePrice), "SalePrices" },
        { typeof(ImportBatch), "ImportBatches" },
        { typeof(ImportRow), "ImportRows" },
        { typeof(Cart), "Carts" },
        { typeof(CartItem), "CartItems" },
        { typeof(InventoryBalance), "InventoryBalances" },
        { typeof(InventoryReservation), "InventoryReservations" },
        { typeof(InventoryMovement), "InventoryMovements" },
        { typeof(InventoryReconciliationCase), "InventoryReconciliationCases" },
        { typeof(ShippingMethod), "ShippingMethods" },
        { typeof(ShippingProviderProfile), "ShippingProviderProfiles" },
        { typeof(PackageLimitVersion), "PackageLimitVersions" },
        { typeof(ConvenienceStore), "ConvenienceStores" },
        { typeof(Shipment), "Shipments" },
        { typeof(ShipmentStatusHistory), "ShipmentStatusHistories" },
        { typeof(BuildList), "BuildLists" },
        { typeof(BuildListItem), "BuildListItems" },
        { typeof(BuildShareToken), "BuildShareTokens" },
        { typeof(CompatibilityRuleSetting), "CompatibilityRuleSettings" },
        { typeof(CompatibilityCheckRun), "CompatibilityCheckRuns" },
        { typeof(CompatibilityCheckResult), "CompatibilityCheckResults" },
        { typeof(ProductReview), "ProductReviews" },
        { typeof(ReviewImage), "ReviewImages" },
        { typeof(ProductReviewRevision), "ProductReviewRevisions" },
    };

    [Theory]
    [MemberData(nameof(CatalogTables))]
    public void Model_MapsCatalogEntityToExpectedTable(Type entityType, string tableName)
    {
        using var context = CreateContext();

        Assert.Equal(tableName, context.Model.FindEntityType(entityType)?.GetTableName());
    }

    [Fact]
    public void Sku_ModelUsesFilteredUniqueDefaultAndMoneyPrecision()
    {
        using var context = CreateContext();
        var entity = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType>(
            context.Model.FindEntityType(typeof(Sku)));

        var defaultIndex = Assert.Single(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "UX_Skus_ProductId_IsDefault");
        Assert.True(defaultIndex.IsUnique);
        Assert.Equal("[IsDefault] = 1", defaultIndex.GetFilter());
        Assert.Equal(18, entity.FindProperty(nameof(Sku.ListPrice))?.GetPrecision());
        Assert.Equal(2, entity.FindProperty(nameof(Sku.ListPrice))?.GetScale());
    }

    [Fact]
    public void Translation_ModelConvertsLocaleToPublishedLocaleCode()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(ProductTranslation))?
            .FindProperty(nameof(ProductTranslation.Locale));
        var converter = property?.GetValueConverter();

        Assert.NotNull(converter);
        Assert.Equal("ja-JP", converter.ConvertToProvider(SupportedLocale.JaJp));
    }

    [Fact]
    public void HaruCrossModuleForeignKeys_AreNowMappedAsRestrict()
    {
        using var context = CreateContext();
        var favorite = context.Model.FindEntityType(typeof(Favorite));
        var orderItem = context.Model.FindEntityType(typeof(DoSelect.Domain.Orders.OrderItem));

        Assert.Contains(favorite!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Product) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(orderItem!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Sku) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void InventoryBalance_AvailableQuantityIsPersistedComputedColumn()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(InventoryBalance))?
            .FindProperty(nameof(InventoryBalance.AvailableQuantity));

        Assert.Equal(
            "[OnHandQuantity] - [ReservedQuantity]",
            property?.GetComputedColumnSql());
        Assert.True(property?.GetIsStored());
    }

    [Fact]
    public void TerryCascadeWhitelistChildren_AreMappedAsCascade()
    {
        using var context = CreateContext();
        var importRow = context.Model.FindEntityType(typeof(ImportRow));
        var cartItem = context.Model.FindEntityType(typeof(CartItem));
        var buildListItem = context.Model.FindEntityType(typeof(BuildListItem));

        Assert.Contains(importRow!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(ImportBatch) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(cartItem!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Cart) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(buildListItem!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(BuildList) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
    }

    [Fact]
    public void BuildListItem_UsesCorrectedPublicIdIndexName()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BuildListItem));

        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.GetDatabaseName() == "UX_BuildListItems_PublicId");
        Assert.DoesNotContain(entity.GetIndexes(), index =>
            index.GetDatabaseName() == "UX_ReviewImages_PublicId");
    }

    [Fact]
    public void CompatibilityOverall_UsesFormalFourValueContract()
    {
        Assert.Equal(
            ["Compatible", "Warning", "Blocked", "InsufficientData"],
            Enum.GetNames<CompatibilityOverall>());
    }

    private static DoSelectDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options;
        return new DoSelectDbContext(options);
    }
}
