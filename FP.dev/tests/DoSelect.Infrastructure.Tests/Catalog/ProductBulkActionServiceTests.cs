using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DoSelect.Application.Auditing;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Inventory;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Tests.Catalog;

/// <summary>
/// UC-ADM-PROD-02 批次上架／下架／調價與 A-04 匯出。整批單一交易、稽核、RowVersion 與價格界線都
/// 靠真實 SQL Server 驗證——CHECK 條件約束與樂觀鎖在 InMemory Provider 上根本不存在。
/// </summary>
[Collection(nameof(CatalogAdminCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class ProductBulkActionServiceTests
{
    [Fact]
    public async Task PublishAsync_ChangesOnlyTheSelectedProducts()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var selected = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var untouched = await CatalogAdminFixture.CreateProductAsync(context, brand, category);

        var service = CatalogAdminFixture.CreateProductService(context);
        var result = await service.ApplyBulkActionAsync(
            BulkProductActions.Publish,
            Selection(selected),
            CatalogAdminFixture.TestAuditContext,
            actorId,
            CancellationToken.None);

        Assert.Equal(1, result.AffectedProductCount);
        await using var verify = CatalogAdminFixture.CreateContext();
        Assert.Equal(ProductStatus.Published, await StatusOf(verify, selected.PublicId));
        Assert.Equal(ProductStatus.Draft, await StatusOf(verify, untouched.PublicId));
    }

    /// <summary>
    /// 已經是目標狀態的商品完全跳過。若照樣呼叫 ChangeStatus，UpdatedAtUtc 會變、EF 會發出一筆
    /// 什麼都沒改的 UPDATE 並推進 RowVersion——別人手上剛讀到的 RowVersion 就無故失效了。
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenAlreadyPublished_DoesNotBumpTheRowVersion()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        product.ChangeStatus(ProductStatus.Published, DateTime.UtcNow);
        await context.SaveChangesAsync();
        var rowVersionBefore = product.RowVersion.ToArray();

        var result = await CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
            BulkProductActions.Publish,
            Selection(product),
            CatalogAdminFixture.TestAuditContext,
            actorId,
            CancellationToken.None);

        Assert.Equal(0, result.AffectedProductCount);
        await using var verify = CatalogAdminFixture.CreateContext();
        var reloaded = await verify.Products.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == product.PublicId);
        Assert.Equal(rowVersionBefore, reloaded.RowVersion);
    }

    [Fact]
    public async Task BulkActionAsync_WhenAnyProductIsDiscontinued_RejectsTheWholeBatch()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var healthy = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var discontinued = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        discontinued.ChangeStatus(ProductStatus.Discontinued, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                BulkProductActions.Publish,
                Selection(healthy, discontinued),
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ProductUnavailable, exception.ErrorCode);

        // 整批拒絕的意思是「沒有一筆生效」，不是「不能改的那筆沒生效」。
        await using var verify = CatalogAdminFixture.CreateContext();
        Assert.Equal(ProductStatus.Draft, await StatusOf(verify, healthy.PublicId));
    }

    [Fact]
    public async Task BulkActionAsync_WhenARowVersionIsStale_RejectsWithConcurrencyConflict()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var staleRowVersion = product.RowVersion.ToArray();

        await using (var other = CatalogAdminFixture.CreateContext())
        {
            var concurrent = await other.Products.SingleAsync(candidate => candidate.PublicId == product.PublicId);
            concurrent.ChangeStatus(ProductStatus.Unpublished, DateTime.UtcNow);
            await other.SaveChangesAsync();
        }

        await using var fresh = CatalogAdminFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(fresh).ApplyBulkActionAsync(
                BulkProductActions.Publish,
                new BulkProductActionRequest(
                    [product.PublicId],
                    [new BulkProductActionItem(product.PublicId, staleRowVersion)],
                    null),
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public async Task AdjustPriceAsync_AppliesThePercentageToEverySkuAndAuditsTheReason()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 1000m);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 333.33m);

        var result = await CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
            BulkProductActions.AdjustPrice,
            Selection(product) with
            {
                PriceAdjustment = new BulkPriceAdjustment(
                    BulkPriceAdjustmentModes.Percentage, -10m, "季末促銷"),
            },
            CatalogAdminFixture.TestAuditContext,
            actorId,
            CancellationToken.None);

        Assert.Equal(1, result.AffectedProductCount);
        Assert.Equal(2, result.AffectedSkuCount);

        await using var verify = CatalogAdminFixture.CreateContext();
        var prices = await verify.Skus.AsNoTracking()
            .Where(sku => sku.ProductId == product.Id)
            .Select(sku => sku.ListPrice)
            .OrderBy(price => price)
            .ToListAsync();
        // 333.33 * 0.9 = 299.997 → AwayFromZero 進位到 300.00。
        Assert.Equal([300.00m, 900.00m], prices);

        var audit = await verify.AuditLogs.AsNoTracking()
            .Where(log => log.ResourcePublicId == product.PublicId)
            .SingleAsync();
        Assert.Equal(AuditActions.ProductBulkAdjustPrice, audit.Action);
        // 原因（規格要求的「原因」）由 EfAuditWriter 序列化進 ChangedFieldsJson 的信封，
        // AuditLog 沒有獨立的 Note 欄位——這支測試就是為了證明它真的有落地而不是被丟掉。
        Assert.Contains("季末促銷", audit.ChangedFieldsJson, StringComparison.Ordinal);
        Assert.Contains("percentage", audit.ChangedFieldsJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// 子 SKU 改了價、父商品的 RowVersion 卻不動的話，另一個管理員手上那份「看起來還新鮮」的
    /// RowVersion 就能覆蓋掉這次調價。Product.Touch 就是為此存在。
    /// </summary>
    [Fact]
    public async Task AdjustPriceAsync_AdvancesTheProductRowVersion()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 100m);
        var rowVersionBefore = product.RowVersion.ToArray();

        await CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
            BulkProductActions.AdjustPrice,
            Selection(product) with
            {
                PriceAdjustment = new BulkPriceAdjustment(BulkPriceAdjustmentModes.Amount, 50m, "調整"),
            },
            CatalogAdminFixture.TestAuditContext,
            actorId,
            CancellationToken.None);

        await using var verify = CatalogAdminFixture.CreateContext();
        var reloaded = await verify.Products.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == product.PublicId);
        Assert.NotEqual(rowVersionBefore, reloaded.RowVersion);
    }

    /// <summary>
    /// CK_Skus_Prices 要求 ListPrice >= 0。若讓負數一路寫下去會是 DbUpdateException（500），所以
    /// 必須在服務邊界擋成 validation_failed，而且整批都不能生效。
    /// </summary>
    [Fact]
    public async Task AdjustPriceAsync_WhenTheResultWouldGoNegative_RejectsTheWholeBatch()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 500m);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 30m);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                BulkProductActions.AdjustPrice,
                Selection(product) with
                {
                    PriceAdjustment = new BulkPriceAdjustment(BulkPriceAdjustmentModes.Amount, -100m, "降價"),
                },
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);

        await using var verify = CatalogAdminFixture.CreateContext();
        var prices = await verify.Skus.AsNoTracking()
            .Where(sku => sku.ProductId == product.Id)
            .Select(sku => sku.ListPrice)
            .OrderBy(price => price)
            .ToListAsync();
        Assert.Equal([30m, 500m], prices);
    }

    /// <summary>
    /// AuditWriteRequest.RequireSafeNote 對引號等字元直接丟 ArgumentException，那會冒成 500。
    /// 原因是管理員自己打的自由文字，必須在服務邊界先擋成 400。
    /// </summary>
    [Theory]
    [InlineData("客戶說 \"太貴了\"")]
    [InlineData("與 vendor@example.com 談定")]
    [InlineData("依 token 規則調整")]
    public async Task AdjustPriceAsync_WhenTheReasonCannotBeAudited_FailsValidationInsteadOf500(string reason)
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        await CatalogAdminFixture.AddSkuAsync(context, product, listPrice: 100m);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                BulkProductActions.AdjustPrice,
                Selection(product) with
                {
                    PriceAdjustment = new BulkPriceAdjustment(BulkPriceAdjustmentModes.Percentage, -5m, reason),
                },
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("")]
    public async Task BulkActionAsync_WhenTheActionIsNotWhitelisted_FailsValidation(string action)
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                action,
                Selection(product),
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>
    /// 契約同時要求 productPublicIds 與 rowVersions，而且兩者必須指向同一組商品。
    ///
    /// 這裡刻意讓兩份清單「筆數相同、內容不同」：反向驗證發現，若只送較少的 RowVersion，會先被
    /// 「有商品不存在」那道檢查擋下，測試就算拿掉這個守衛也照樣是綠的——等於在測別的東西。筆數
    /// 相同時才輪得到這個守衛，而少了它，後面 rowVersionsByProduct[...] 會直接 KeyNotFound 變成
    /// 500。
    /// </summary>
    [Fact]
    public async Task BulkActionAsync_WhenRowVersionsPointAtDifferentProducts_FailsValidation()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var first = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var second = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var stranger = await CatalogAdminFixture.CreateProductAsync(context, brand, category);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                BulkProductActions.Publish,
                new BulkProductActionRequest(
                    [first.PublicId, second.PublicId],
                    [
                        new BulkProductActionItem(first.PublicId, first.RowVersion.ToArray()),
                        new BulkProductActionItem(stranger.PublicId, stranger.RowVersion.ToArray()),
                    ],
                    null),
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    /// <summary>
    /// 契約上限是 100 筆。這裡建的是 101 個「真的存在」的商品：反向驗證發現，若用不存在的 GUID
    /// 湊數，會先被「有商品不存在」那道檢查擋下，拿掉上限守衛測試照樣是綠的。
    /// </summary>
    [Fact]
    public async Task BulkActionAsync_WhenTheSelectionExceedsOneHundred_FailsValidation()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var actorId = await CatalogAdminFixture.SeedCatalogAdminAsync(context);
        var products = await CatalogAdminFixture.CreateProductsAsync(context, brand, category, count: 101);

        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ApplyBulkActionAsync(
                BulkProductActions.Publish,
                Selection([.. products]),
                CatalogAdminFixture.TestAuditContext,
                actorId,
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);

        // 上限守衛在讀資料庫之前就該擋下來——沒有任何一筆可以生效。
        await using var verify = CatalogAdminFixture.CreateContext();
        Assert.Equal(ProductStatus.Draft, await StatusOf(verify, products[0].PublicId));
    }

    [Fact]
    public async Task ExportAsync_UsesTheSameFilterAsTheListAndOmitsCost()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var matching = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        var excluded = await CatalogAdminFixture.CreateProductAsync(context, brand, category);
        excluded.ChangeStatus(ProductStatus.Published, DateTime.UtcNow);
        await context.SaveChangesAsync();
        await CatalogAdminFixture.AddSkuAsync(context, matching, listPrice: 1234.50m, unitCost: 999.99m);

        var service = CatalogAdminFixture.CreateProductService(context);
        var query = new AdminProductQuery(
            null, [brand.Code], null, ["Draft"], null, null, PageNumber: 1, PageSize: 1);

        var export = await service.ExportAsync(query, AdminProductExportFormats.Csv, CancellationToken.None);
        var csv = new UTF8Encoding(true).GetString(export.Content);

        Assert.Contains(matching.ProductCode, csv, StringComparison.Ordinal);
        Assert.DoesNotContain(excluded.ProductCode, csv, StringComparison.Ordinal);
        // 列表看不到成本，匯出當然也不能把它漏出去。
        Assert.DoesNotContain("999.99", csv, StringComparison.Ordinal);
        Assert.Contains("1234.50", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// PageSize 只影響列表分頁，匯出必須帶出整組符合條件的資料——否則管理員按下匯出只會拿到
    /// 目前這一頁。
    /// </summary>
    [Fact]
    public async Task ExportAsync_IgnoresPagingAndReturnsEveryMatchingProduct()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var products = new List<Product>();
        for (var index = 0; index < 3; index++)
        {
            products.Add(await CatalogAdminFixture.CreateProductAsync(context, brand, category));
        }

        var export = await CatalogAdminFixture.CreateProductService(context).ExportAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, null, PageNumber: 1, PageSize: 1),
            AdminProductExportFormats.Csv,
            CancellationToken.None);
        var csv = new UTF8Encoding(true).GetString(export.Content);

        foreach (var product in products)
        {
            Assert.Contains(product.ProductCode, csv, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 匯出的 CSV 會被 Excel 直接開啟：以 = + - @ 開頭的欄位會被當成公式執行，是典型的 CSV
    /// 注入。名稱是管理員可控的自由文字，所以必須中和。
    /// </summary>
    [Fact]
    public async Task ExportAsync_NeutralisesFormulaLikeValues()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var now = DateTime.UtcNow;
        var product = new Product(
            Guid.CreateVersion7(), CatalogAdminFixture.UniqueCode("PROD"), brand.Id, category.Id,
            "=cmd|'/c calc'!A1", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var export = await CatalogAdminFixture.CreateProductService(context).ExportAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, null, PageNumber: 1, PageSize: 20),
            AdminProductExportFormats.Csv,
            CancellationToken.None);
        var csv = new UTF8Encoding(true).GetString(export.Content);

        Assert.DoesNotContain(",=cmd", csv, StringComparison.Ordinal);
        Assert.Contains("\t=cmd", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_ProducesAReadableXlsxWorkbook()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var (brand, category, _) = await CatalogAdminFixture.SeedCatalogAsync(context);
        var product = await CatalogAdminFixture.CreateProductAsync(context, brand, category);

        var export = await CatalogAdminFixture.CreateProductService(context).ExportAsync(
            new AdminProductQuery(null, [brand.Code], null, null, null, null, PageNumber: 1, PageSize: 20),
            AdminProductExportFormats.Xlsx,
            CancellationToken.None);

        Assert.EndsWith(".xlsx", export.FileName, StringComparison.Ordinal);
        using var stream = new MemoryStream(export.Content);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Products");
        Assert.Equal("商品代碼", sheet.Cell(1, 1).GetString());
        Assert.Equal(product.ProductCode, sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExportAsync_WhenTheFormatIsUnsupported_FailsValidation()
    {
        await using var context = CatalogAdminFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<CatalogWriteException>(() =>
            CatalogAdminFixture.CreateProductService(context).ExportAsync(
                new AdminProductQuery(null, null, null, null, null, null, 1, 20),
                "pdf",
                CancellationToken.None));

        Assert.Equal(CatalogWriteException.ErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    private static BulkProductActionRequest Selection(params Product[] products) =>
        new(
            products.Select(product => product.PublicId).ToArray(),
            products.Select(product => new BulkProductActionItem(product.PublicId, product.RowVersion.ToArray())).ToArray(),
            null);

    private static async Task<ProductStatus> StatusOf(DoSelectDbContext context, Guid publicId) =>
        await context.Products.AsNoTracking()
            .Where(product => product.PublicId == publicId)
            .Select(product => product.Status)
            .SingleAsync();
}
