using System.Text.Json;
using DoSelect.Application.Auditing;
using DoSelect.Application.Common;
using DoSelect.Application.Imports;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// 商品匯入確認 (匯入暫存與庫存調整設計.md 商品匯入確認 steps 1–6). Every test drives the real
/// Preview first so Confirm consumes exactly what production would: staged rows with computed
/// actions, a Ready batch, and its RowVersion.
/// </summary>
[Collection(nameof(ImportServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfProductImportConfirmTests
{
    private const string ProductsHeader = "product_key,product_code,name_zh_tw,brand_code,category_code,description_zh_tw,warranty_months,status\r\n";
    private const string SkusHeader = "sku_key,sku_code,product_key,name_zh_tw,list_price,unit_cost,weight_kg,length_cm,width_cm,height_cm,requires_prepayment,status\r\n";
    private const string SpecificationsHeader = "sku_key,semantic_key,value_type,string_value,decimal_value,boolean_value,option_code\r\n";

    private static readonly AuditRequestContext TestAuditContext =
        new("test-correlation", "0123456789abcdef0123456789abcdef", null);

    [Fact]
    public async Task ConfirmAsync_WhenTheBatchInsertsAProductSkuAndSpecification_AppliesEverythingAndCommits()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var definition = new SpecificationDefinition(
            Guid.CreateVersion7(), category.Id, "capacity_gb", "容量", SpecificationValueType.Decimal,
            null, isRequired: false, isProtected: false, sortOrder: 1, DateTime.UtcNow);
        context.SpecificationDefinitions.Add(definition);
        await context.SaveChangesAsync();
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);

        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var skuCode = ImportServiceFixture.UniqueCode("SKU");
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{productCode},匯入商品,{brand.Code},{category.Code},描述,24,Draft\r\n",
            SkusHeader + $"SK1,{skuCode},PK1,匯入SKU,1500,900,\\N,\\N,\\N,\\N,false,Draft\r\n",
            SpecificationsHeader + "SK1,capacity_gb,Decimal,\\N,512,\\N,\\N\r\n"), adminId, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Ready.ToString(), preview.Status);

        var result = await service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None);

        Assert.Equal(ImportBatchStatus.Committed.ToString(), result.Status);
        Assert.NotNull(result.ConfirmedAtUtc);

        await using var verify = ImportServiceFixture.CreateContext();
        var product = await verify.Products.SingleAsync(candidate => candidate.ProductCode == productCode);
        Assert.Equal("匯入商品", product.NameZhTw);
        Assert.Equal(24, product.WarrantyMonths);
        var sku = await verify.Skus.SingleAsync(candidate => candidate.SkuCode == skuCode);
        Assert.Equal(product.Id, sku.ProductId);
        // A brand-new product's first imported SKU becomes its default — every product must keep
        // exactly one default SKU and a new product has no other candidate.
        Assert.True(sku.IsDefault);
        var specification = await verify.SkuSpecificationValues.SingleAsync(candidate => candidate.SkuId == sku.Id);
        Assert.Equal(definition.Id, specification.SpecificationDefinitionId);
        Assert.Equal(512m, specification.DecimalValue);
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Committed, batch.Status);
        var summary = JsonSerializer.Deserialize<JsonElement>(batch.ResultSummaryJson!);
        Assert.Equal(1, summary.GetProperty("ProductsInserted").GetInt32());
        Assert.Equal(1, summary.GetProperty("SkusInserted").GetInt32());
        Assert.Equal(1, summary.GetProperty("SpecificationsInserted").GetInt32());
        // 商品匯入確認 step 6: the audit entry lands in the same transaction as the catalog writes.
        var audit = await verify.AuditLogs.SingleAsync(candidate => candidate.ResourcePublicId == preview.PublicId);
        Assert.Equal("catalog_import.confirm", audit.Action);
    }

    [Fact]
    public async Task ConfirmAsync_WhenAnExistingSkusUnitCostChanges_WritesACostChangeMovement()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var now = DateTime.UtcNow;
        var product = new Product(Guid.CreateVersion7(), ImportServiceFixture.UniqueCode("PROD"), brand.Id, category.Id, "既有商品", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var sku = new Sku(Guid.CreateVersion7(), ImportServiceFixture.UniqueCode("SKU"), product.Id, "既有SKU", 1000m, 600m, now);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();
        context.InventoryBalances.Add(new DoSelect.Domain.Inventory.InventoryBalance(
            Guid.CreateVersion7(), sku.Id, onHandQuantity: 5, reorderLevel: 0, now));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader,
            SkusHeader + $"SK1,{sku.SkuCode},{product.ProductCode},既有SKU,1000,750,\\N,\\N,\\N,\\N,false,Draft\r\n",
            SpecificationsHeader), adminId, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Ready.ToString(), preview.Status);

        await service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None);

        await using var verify = ImportServiceFixture.CreateContext();
        var updated = await verify.Skus.SingleAsync(candidate => candidate.Id == sku.Id);
        Assert.Equal(750m, updated.UnitCost);
        // Same semantics as EfSkuAdminService.UpdateAsync: the M-15 turnover report needs a
        // zero-delta CostChange marker whenever a balance-carrying SKU's cost moves.
        var movement = await verify.InventoryMovements.SingleAsync(candidate => candidate.SkuId == sku.Id);
        Assert.Equal("CostChange", movement.MovementType);
        Assert.Equal(750m, movement.UnitCostSnapshot);
        Assert.Equal(0, movement.OnHandDelta);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheBatchWasAlreadyCommitted_RejectsTheResend()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);
        var committed = await service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, committed.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportAlreadyCommitted, exception.Code);
    }

    [Fact]
    public async Task ConfirmAsync_WhenThePreviewExpired_RejectsWithGoneAndMarksTheBatchExpired()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);
        // Age the batch past its 24-hour window without waiting for it.
        await context.Database.ExecuteSqlAsync(
            $"UPDATE ImportBatches SET ExpiresAtUtc = DATEADD(HOUR, -1, SYSUTCDATETIME()) WHERE PublicId = {preview.PublicId}");
        // The raw UPDATE bypassed the change tracker (and bumped the rowversion): drop the stale
        // tracked instance so ConfirmAsync reloads the row as production would on a fresh request.
        context.ChangeTracker.Clear();
        var aged = await context.ImportBatches.AsNoTracking().SingleAsync(candidate => candidate.PublicId == preview.PublicId);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, aged.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportBatchExpired, exception.Code);
        await using var verify = ImportServiceFixture.CreateContext();
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Expired, batch.Status);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheRowVersionIsStale_RejectsWithAConcurrencyConflict()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, new byte[8], TestAuditContext, CancellationToken.None));

        Assert.Equal("concurrency_conflict", exception.Code);
        await using var verify = ImportServiceFixture.CreateContext();
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        // The rejected confirm must not have half-applied anything or advanced the state machine.
        Assert.Equal(ImportBatchStatus.Ready, batch.Status);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheCatalogDriftedSincePreview_RejectsAndAppliesNothing()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{productCode},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);

        // Someone creates the same product code between Preview and Confirm — the previewed
        // Insert would silently become an Update, so the confirm must refuse and demand a fresh
        // Preview (the product-import analogue of the inventory confirm's RowVersion recheck).
        context.Products.Add(new Product(Guid.CreateVersion7(), productCode, brand.Id, category.Id, "搶先建立", DateTime.UtcNow));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportValidationFailed, exception.Code);
        await using var verify = ImportServiceFixture.CreateContext();
        Assert.Equal(1, await verify.Products.CountAsync(candidate => candidate.ProductCode == productCode));
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Ready, batch.Status);
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheBatchIsInvalid_RejectsWithValidationFailed()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (_, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        // Unknown brand code → the row errors and the batch lands Invalid, never Ready.
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,NO-SUCH-BRAND,{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Invalid.ToString(), preview.Status);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportValidationFailed, exception.Code);
    }

    private static EfProductImportService CreateService(DoSelectDbContext context) =>
        new(context, new EfAuditWriter(context, TimeProvider.System));

    /// <summary>Mirrors CompatibilityRuleAdminServiceFixture.SeedAdminUserIdAsync: ConfirmAsync's
    /// audit actor resolution requires a real Admin account holding CatalogManager or SuperAdmin.</summary>
    private static async Task<string> SeedCatalogManagerAsync(DoSelectDbContext context)
    {
        var admin = ApplicationUser.CreateAdmin(Guid.CreateVersion7(), $"{Guid.NewGuid():N}@doselect.test", DateTime.UtcNow);
        var role = new IdentityRole(AuditRoleNames.CatalogManager);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private static PreviewProductImportRequest MakeRequest(string productsCsv, string skusCsv, string specificationsCsv) =>
        new(ToFile(productsCsv), ToFile(skusCsv), ToFile(specificationsCsv), TemplateVersion: 1);

    private static IncomingImportFile ToFile(string csv)
    {
        var bytes = ImportServiceFixture.Utf8(csv);
        return new IncomingImportFile("upload.csv", "text/csv", bytes.Length, true, () => new MemoryStream(bytes));
    }
}
