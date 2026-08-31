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
        // The in-transaction revalidation marks a drifted batch Failed (spec: Failed 必須以修正後
        // 檔案建立新 Batch) — the stale preview is unusable either way, and this matches the
        // write-time preimage rejection path.
        Assert.Equal(ImportBatchStatus.Failed, batch.Status);
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

    /// <summary>組長 PR #74 review item 1: 規格要求「建立者且具 CatalogImport.Execute」— another
    /// CatalogManager must not be able to commit a preview they never created or reviewed.</summary>
    [Fact]
    public async Task ConfirmAsync_WhenADifferentAdminConfirms_RejectsAndChangesNothing()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var creator = await SeedCatalogManagerAsync(context);
        var otherAdmin = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{productCode},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), creator, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.ConfirmAsync(preview.PublicId, otherAdmin, preview.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        await using var verify = ImportServiceFixture.CreateContext();
        Assert.Equal(0, await verify.Products.CountAsync(candidate => candidate.ProductCode == productCode));
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Ready, batch.Status);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(log => log.ResourcePublicId == preview.PublicId));
    }

    /// <summary>組長 PR #74 review item 2: an Update row whose underlying entity was edited after
    /// Preview stays an Update on re-resolve, so action equality alone would silently overwrite the
    /// interim change. Every Update write now carries the Preview-time RowVersion as its EF
    /// concurrency original value, so the write itself fails — closing the TOCTOU window too, since
    /// the enforcement happens at write time inside the confirm transaction.</summary>
    [Fact]
    public async Task ConfirmAsync_WhenAnUpdatedEntityChangedAfterPreview_RejectsInsteadOfOverwriting()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var now = DateTime.UtcNow;
        var product = new Product(Guid.CreateVersion7(), ImportServiceFixture.UniqueCode("PROD"), brand.Id, category.Id, "原始名稱", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        // Preview stages an Update: 原始名稱 → 匯入名稱.
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{product.ProductCode},匯入名稱,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);
        Assert.Equal(ImportBatchStatus.Ready.ToString(), preview.Status);

        // A colleague edits the same product between Preview and Confirm — still an Update on
        // re-resolve, which is exactly the gap action-equality could not see.
        await using (var interim = ImportServiceFixture.CreateContext())
        {
            var same = await interim.Products.SingleAsync(candidate => candidate.Id == product.Id);
            same.UpdateDetails(brand.Id, category.Id, "同事改的名稱", null, null, same.IsFeatured, DateTime.UtcNow);
            await interim.SaveChangesAsync();
        }

        await using var confirmContext = ImportServiceFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            CreateService(confirmContext).ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportValidationFailed, exception.Code);
        await using var verify = ImportServiceFixture.CreateContext();
        var after = await verify.Products.SingleAsync(candidate => candidate.Id == product.Id);
        // The colleague's change survives; the import applied nothing.
        Assert.Equal("同事改的名稱", after.NameZhTw);
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.Equal(ImportBatchStatus.Failed, batch.Status);
    }

    /// <summary>組長 PR #74 review item 3: only the current template version is accepted; the 0 a
    /// missing multipart field produces, and any unknown version, reject whole-batch with the
    /// current template information.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PreviewAsync_WithAMissingOrUnsupportedTemplateVersion_RejectsTheWholeBatch(int templateVersion)
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);

        var request = new PreviewProductImportRequest(
            ToFile(ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n"),
            ToFile(SkusHeader),
            ToFile(SpecificationsHeader),
            templateVersion);

        var exception = await Assert.ThrowsAsync<DomainProblemException>(() =>
            service.PreviewAsync(request, adminId, CancellationToken.None));

        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.Code);
        Assert.Contains("version 1", exception.Message);
        await using var verify = ImportServiceFixture.CreateContext();
        Assert.Equal(0, await verify.ImportBatches.CountAsync(candidate => candidate.CreatedByAdminUserId == adminId));
    }

    /// <summary>組長 PR #74 review item 4: an expired Ready batch used to keep tripping the
    /// one-in-progress unique index until someone happened to call its Confirm.</summary>
    [Fact]
    public async Task PreviewAsync_WhenThePreviousReadyBatchExpired_ExpiresItAndStagesTheNewOne()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var first = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);
        await context.Database.ExecuteSqlAsync(
            $"UPDATE ImportBatches SET ExpiresAtUtc = DATEADD(HOUR, -1, SYSUTCDATETIME()) WHERE PublicId = {first.PublicId}");
        context.ChangeTracker.Clear();

        var second = await CreateService(context).PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);

        Assert.Equal(ImportBatchStatus.Ready.ToString(), second.Status);
        await using var verify = ImportServiceFixture.CreateContext();
        var firstAfter = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == first.PublicId);
        Assert.Equal(ImportBatchStatus.Expired, firstAfter.Status);
    }

    /// <summary>組長 PR #74 review item 5: invalid rows-query inputs must reject, not silently
    /// widen the result.</summary>
    [Fact]
    public async Task GetRowsAsync_WithInvalidInputs_RejectsInsteadOfSilentlyWidening()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await SeedCatalogManagerAsync(context);
        var service = CreateService(context);
        var preview = await service.PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);

        await Assert.ThrowsAsync<DomainProblemException>(() => service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery("NotADataset", false, null, 50), CancellationToken.None));
        await Assert.ThrowsAsync<DomainProblemException>(() => service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery(null, false, "not-a-cursor", 50), CancellationToken.None));
        await Assert.ThrowsAsync<DomainProblemException>(() => service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery(null, false, null, 0), CancellationToken.None));
        await Assert.ThrowsAsync<DomainProblemException>(() => service.GetRowsAsync(
            preview.PublicId, new ImportRowsQuery(null, false, null, 201), CancellationToken.None));
    }

    /// <summary>An audit write failure rolls the entire confirm back — no catalog change, no
    /// Committed batch, no partial state (組長 PR #74 review, closing note).</summary>
    [Fact]
    public async Task ConfirmAsync_WhenTheAuditWriteFails_RollsEverythingBack()
    {
        await using var seedContext = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(seedContext);
        var adminId = await SeedCatalogManagerAsync(seedContext);
        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var preview = await CreateService(seedContext).PreviewAsync(MakeRequest(
            ProductsHeader + $"PK1,{productCode},商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n",
            SkusHeader,
            SpecificationsHeader), adminId, CancellationToken.None);

        await using var failingContext = ImportServiceFixture.CreateContext(new FailAuditLogWrites());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(failingContext).ConfirmAsync(preview.PublicId, adminId, preview.RowVersion, TestAuditContext, CancellationToken.None));

        await using var verify = ImportServiceFixture.CreateContext();
        Assert.Equal(0, await verify.Products.CountAsync(candidate => candidate.ProductCode == productCode));
        var batch = await verify.ImportBatches.SingleAsync(candidate => candidate.PublicId == preview.PublicId);
        Assert.NotEqual(ImportBatchStatus.Committed, batch.Status);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(log => log.ResourcePublicId == preview.PublicId));
    }

    /// <summary>Fails any SaveChanges that tries to insert an AuditLog row, simulating an audit
    /// subsystem failure at the exact point the confirm writes its entry.</summary>
    private sealed class FailAuditLogWrites : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null &&
                eventData.Context.ChangeTracker.Entries<DoSelect.Domain.Auditing.AuditLog>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("Simulated audit write failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
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
