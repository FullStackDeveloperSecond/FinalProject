using System.Text;
using DoSelect.Application.Imports;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Imports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

[Collection(nameof(ImportServiceCollection))]
[Trait("Category", "RequiresSqlServer")]
public sealed class EfProductImportServiceTests
{
    private const string ProductsHeader = "product_key,product_code,name_zh_tw,brand_code,category_code,description_zh_tw,warranty_months,status\r\n";
    private const string SkusHeader = "sku_key,sku_code,product_key,name_zh_tw,list_price,unit_cost,weight_kg,length_cm,width_cm,height_cm,requires_prepayment,status\r\n";
    private const string SpecificationsHeader = "sku_key,semantic_key,value_type,string_value,decimal_value,boolean_value,option_code\r\n";

    [Fact]
    public async Task PreviewAsync_WhenEverythingIsNew_StagesInsertsAndMarksTheBatchReady()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productKey = "PK1";
        var skuKey = "SK1";
        var productsCsv = ProductsHeader +
            $"{productKey},{ImportServiceFixture.UniqueCode("PROD")},Test Product,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var skusCsv = SkusHeader +
            $"{skuKey},{ImportServiceFixture.UniqueCode("SKU")},{productKey},Test Sku,1000,700,\\N,\\N,\\N,\\N,false,Draft\r\n";
        var specificationsCsv = SpecificationsHeader;

        var request = new PreviewProductImportRequest(
            ToFile(productsCsv),
            ToFile(skusCsv),
            ToFile(specificationsCsv),
            TemplateVersion: 1);

        var result = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.Equal("Ready", result.Status);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.NewCount);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public async Task PreviewAsync_WhenABrandCodeDoesNotResolve_MarksTheRowAsAnErrorAndTheBatchInvalid()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (_, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Test Product,NO-SUCH-BRAND,{category.Code},\\N,\\N,Draft\r\n";
        var skusCsv = SkusHeader;
        var specificationsCsv = SpecificationsHeader;

        var request = new PreviewProductImportRequest(ToFile(productsCsv), ToFile(skusCsv), ToFile(specificationsCsv), 1);

        var result = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.Equal("Invalid", result.Status);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public async Task PreviewAsync_WhenTheProductAlreadyExistsWithIdenticalValues_MarksTheRowUnchanged()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var now = DateTime.UtcNow;
        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var product = new Product(Guid.CreateVersion7(), productCode, brand.Id, category.Id, "Existing Product", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);

        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));
        var productsCsv = ProductsHeader +
            $"PK1,{productCode},Existing Product,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var result = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(0, result.NewCount);
        Assert.Equal(0, result.UpdatedCount);
    }

    [Fact]
    public async Task PreviewAsync_WhenTheProductAlreadyExistsWithADifferentName_MarksTheRowUpdated()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var now = DateTime.UtcNow;
        var productCode = ImportServiceFixture.UniqueCode("PROD");
        var product = new Product(Guid.CreateVersion7(), productCode, brand.Id, category.Id, "Old Name", now);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);

        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));
        var productsCsv = ProductsHeader +
            $"PK1,{productCode},New Name,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var result = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.Equal(1, result.UpdatedCount);
    }

    [Fact]
    public async Task PreviewAsync_WhenBothProductsAndSkusAndSpecificationsAreEmpty_ThrowsDatasetMissing()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var request = new PreviewProductImportRequest(
            ToFile(ProductsHeader), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        // No admin seeding needed: this throws before any database write (the dataset-missing
        // check runs before ImportBatch is even constructed), so the FK to AspNetUsers never
        // gets exercised.
        await Assert.ThrowsAsync<DoSelect.Application.Common.DomainProblemException>(
            () => service.PreviewAsync(request, "admin-1", CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_WhenTheSameAdminAlreadyHasAnInProgressBatch_RejectsTheSecondOne()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Test Product,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        await service.PreviewAsync(request, adminId, CancellationToken.None);

        var secondRequest = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var exception = await Assert.ThrowsAsync<DoSelect.Application.Common.DomainProblemException>(
            () => service.PreviewAsync(secondRequest, adminId, CancellationToken.None));
        Assert.Equal("import_batch_in_progress", exception.Code);
    }

    [Fact]
    public async Task GetRowsAsync_FiltersByDatasetAndPaginatesWithACursor()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 1,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK2,{ImportServiceFixture.UniqueCode("PROD")},Product 2,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);
        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        var firstPage = await service.GetRowsAsync(
            batch.PublicId, new ImportRowsQuery("Products", false, null, 1), CancellationToken.None);

        Assert.Single(firstPage.Items);
        Assert.True(firstPage.HasMore);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await service.GetRowsAsync(
            batch.PublicId, new ImportRowsQuery("Products", false, firstPage.NextCursor, 1), CancellationToken.None);

        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);
        Assert.NotEqual(firstPage.Items[0].ImportKey, secondPage.Items[0].ImportKey);
    }

    [Fact]
    public async Task GetErrorsCsvAsync_ReturnsOnlyTheErroredRows()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 1,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK2,{ImportServiceFixture.UniqueCode("PROD")},Product 2,NO-SUCH-BRAND,{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);
        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);

        Assert.NotNull(csvBytes);
        var text = Encoding.UTF8.GetString(csvBytes!.Skip(3).ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // header + exactly one errored row
        Assert.Contains("PK2", lines[1], StringComparison.Ordinal);
    }

    private static IncomingImportFile ToFile(string csv)
    /// <summary>組長 PR #74 round-3, item 1：SQL Server 重現——重複的 product_key 先前在 staging 階段
    /// 就以 ArgumentException／唯一索引違反炸成 500，管理員連錯誤檔都下載不到。現在必須安全落成一個
    /// Invalid batch，而且錯誤 CSV 要顯示「原始」的 offending key（儲存鍵是合成的）。</summary>
    [Fact]
    public async Task PreviewAsync_WhenAProductKeyIsDuplicated_StoresAnInvalidBatchWithADownloadableErrorFile()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 1,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 2,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        Assert.True(batch.ErrorCount >= 1);

        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);
        Assert.NotNull(csvBytes);
        var text = Encoding.UTF8.GetString(csvBytes!.Skip(3).ToArray());
        // The download names the key the admin actually wrote, not the synthetic storage key.
        Assert.Contains("PK1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__dup", text, StringComparison.Ordinal);
    }

    /// <summary>組長 PR #74 round-3, item 1（Skus／Specifications 兩組）。</summary>
    [Fact]
    public async Task PreviewAsync_WhenSkuAndSpecificationKeysAreDuplicated_StillStoresAnInvalidBatch()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 1,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var skusCsv = SkusHeader +
            $"SK1,{ImportServiceFixture.UniqueCode("SKU")},PK1,SKU 1,1000,600,\\N,\\N,\\N,\\N,false,Draft\r\n" +
            $"SK1,{ImportServiceFixture.UniqueCode("SKU")},PK1,SKU 2,1000,600,\\N,\\N,\\N,\\N,false,Draft\r\n";
        var specificationsCsv = SpecificationsHeader +
            "SK1,capacity_gb,Decimal,\\N,512,\\N,\\N\r\n" +
            "SK1,capacity_gb,Decimal,\\N,1024,\\N,\\N\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(skusCsv), ToFile(specificationsCsv), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);
        Assert.NotNull(csvBytes);
        var text = Encoding.UTF8.GetString(csvBytes!.Skip(3).ToArray());
        Assert.Contains("SK1", text, StringComparison.Ordinal);
    }

    /// <summary>組長 PR #74 round-3, item 4：單列序列化後超過 32 KB 時，ImportRow 建構子會丟
    /// ArgumentOutOfRangeException——整批 500。超限的列必須改存最小資訊但仍形成 Invalid batch。</summary>
    [Fact]
    public async Task PreviewAsync_WhenASingleRowExceedsTheJsonLimit_StillProducesAnInvalidBatch()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        // ~40 KB in one field: far under the 10 MB per-file cap, far over the 32 KB per-row cap.
        var huge = new string('X', 40 * 1024);
        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},{huge},{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        Assert.True(batch.ErrorCount >= 1);
        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);
        Assert.NotNull(csvBytes);
    }

    /// <summary>組長 PR #74 round-3 裁定 A1：系統產生的錯誤 CSV 固定輸出 UTF-8 BOM。</summary>
    [Fact]
    public async Task GetErrorsCsvAsync_AlwaysEmitsAUtf8Bom()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},Product 1,NO-SUCH-BRAND,{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);
        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);

        Assert.NotNull(csvBytes);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csvBytes!.Take(3).ToArray());
    }

    /// <summary>組長 PR #74 round-3 裁定 A1：上傳的 UTF-8 有沒有 BOM 都要照常受理。</summary>
    [Fact]
    public async Task PreviewAsync_AcceptsUploadsWithAndWithoutAUtf8Bom()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        // One in-progress batch per admin is a real rule, so the two uploads use different admins.
        async Task<string> RunPreviewAsync(bool withBom, string uploaderId)
        {
            var productsCsv = ProductsHeader +
                $"PK1,{ImportServiceFixture.UniqueCode("PROD")},含中文的商品,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
            var request = new PreviewProductImportRequest(
                ToFile(productsCsv, withBom), ToFile(SkusHeader, withBom), ToFile(SpecificationsHeader, withBom), 1);
            var batch = await service.PreviewAsync(request, uploaderId, CancellationToken.None);
            return batch.Status;
        }

        var secondAdminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        Assert.Equal("Ready", await RunPreviewAsync(withBom: false, adminId));
        Assert.Equal("Ready", await RunPreviewAsync(withBom: true, secondAdminId));
    }

    /// <summary>組長 PR #74 round-3, item 5：非法 UTF-8 必須穩定拒絕，不可靜默換成 U+FFFD。</summary>
    [Fact]
    public async Task PreviewAsync_WhenTheUploadIsNotValidUtf8_RejectsWithFormatUnsupported()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var invalidBytes = ImportServiceFixture.Utf8(ProductsHeader)
            .Concat(new byte[] { 0xFF, 0xFE })
            .Concat(ImportServiceFixture.Utf8($",CODE,Name,{brand.Code},{category.Code},\\N,\\N,Draft\r\n"))
            .ToArray();
        var request = new PreviewProductImportRequest(
            new IncomingImportFile("upload.csv", "text/csv", invalidBytes.Length, true, () => new MemoryStream(invalidBytes)),
            ToFile(SkusHeader),
            ToFile(SpecificationsHeader),
            1);

        var exception = await Assert.ThrowsAsync<DoSelect.Application.Common.DomainProblemException>(
            () => service.PreviewAsync(request, adminId, CancellationToken.None));
        Assert.Equal(DoSelect.Application.Common.DomainErrorCodes.ImportFormatUnsupported, exception.Code);
    }

    /// <summary>組長 PR #74 round-4 review (P2)：真正跑進 SQL Server 的證明，而不是記憶體裡的
    /// 大小寫敏感比較。CI 的 container 沒有設定 MSSQL_COLLATION，預設 SQL_Latin1_General_CP1_CI_AS
    /// 不分大小寫——使用者自己的 `__DUP3` 與合成鍵 `__dup3` 在資料庫眼中是同一個 ImportKey，會撞
    /// UX_ImportRows_ImportBatchId_Dataset_ImportKey。整批必須安全落成 Invalid batch。</summary>
    [Fact]
    public async Task PreviewAsync_WhenAUserKeyLooksLikeASyntheticKey_StagesWithoutHittingTheUniqueIndex()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        // Rows 2 and 3 share PK1, so row 3 needs a synthetic key; row 4 is a legitimate upload key
        // that occupies exactly the name that synthetic key would otherwise take.
        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品一,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品二,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"__DUP3,{ImportServiceFixture.UniqueCode("PROD")},商品三,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);

        // Every row landed: SQL Server accepted three distinct ImportKeys under its own collation.
        await using var verify = ImportServiceFixture.CreateContext();
        var stored = await verify.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId ==
                verify.ImportBatches.Where(b => b.PublicId == batch.PublicId).Select(b => b.Id).First())
            .ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.Equal(
            3,
            stored.Select(row => row.ImportKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);
        Assert.NotNull(csvBytes);
    }

    /// <summary>組長 PR #74 round-4 review (P3)：duplicate ＋ oversized 的組合。超長列的 payload 會被
    /// 丟棄，但錯誤 CSV 仍必須顯示管理員自己的 key，不能只剩合成鍵。</summary>
    [Fact]
    public async Task PreviewAsync_WhenARowIsBothDuplicateAndOversized_TheErrorFileStillNamesTheOriginalKey()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var huge = new string('X', 40 * 1024);
        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品一,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK1,{huge},商品二,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        var csvBytes = await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None);
        Assert.NotNull(csvBytes);
        var text = Encoding.UTF8.GetString(csvBytes!.Skip(3).ToArray());
        Assert.Contains("PK1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__dup", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>組長 PR #74 round-5 review (P2)：65 字元的 product_key 先前只加了列級錯誤卻仍被當成
    /// ImportKey，於是死在該欄位的 64 字元限制上，整批 Preview 變成 500。必須改用有界的合成鍵，
    /// 正常形成 Invalid batch。</summary>
    [Fact]
    public async Task PreviewAsync_WhenAProductKeyExceedsTheColumnLength_StillProducesAnInvalidBatch()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var tooLongKey = new string('K', 65);
        var productsCsv = ProductsHeader +
            $"{tooLongKey},{ImportServiceFixture.UniqueCode("PROD")},商品一,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        await using var verify = ImportServiceFixture.CreateContext();
        var stored = await verify.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId ==
                verify.ImportBatches.Where(b => b.PublicId == batch.PublicId).Select(b => b.Id).First())
            .ToListAsync();
        Assert.All(stored, row => Assert.True(row.ImportKey.Length <= 64));
        Assert.NotNull(await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None));
    }

    /// <summary>同一條規則的極端版：key 欄位本身約 40 KB。除了儲存鍵，最小信封也不能把整個 key 放
    /// 回去——那只是換個地方超過 32 KB。</summary>
    [Fact]
    public async Task PreviewAsync_WhenTheKeyFieldItselfIsHuge_StillProducesAnInvalidBatch()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        var hugeKey = new string('K', 40 * 1024);
        var productsCsv = ProductsHeader +
            $"{hugeKey},{ImportServiceFixture.UniqueCode("PROD")},商品一,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        await using var verify = ImportServiceFixture.CreateContext();
        var stored = await verify.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId ==
                verify.ImportBatches.Where(b => b.PublicId == batch.PublicId).Select(b => b.Id).First())
            .ToListAsync();
        Assert.All(stored, row => Assert.True(row.ImportKey.Length <= 64));
        Assert.All(stored, row => Assert.True(row.NormalizedPayloadJson.Length <= 32 * 1024));
        Assert.NotNull(await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None));
    }

    /// <summary>組長 PR #74 round-5 review (P2)：SQL_Latin1_General_CP1_CI_AS 是 width-insensitive，
    /// 全形的 ＿＿ＤＵＰ３ 與合成鍵 __dup3 在資料庫眼中可能是同一個 ImportKey。配置器改用 NFKC ＋
    /// 大寫的 canonical 形式比較後，整批仍必須安全落成 Invalid batch。</summary>
    [Fact]
    public async Task PreviewAsync_WhenAFullWidthKeyShadowsASyntheticKey_StagesWithoutHittingTheUniqueIndex()
    {
        await using var context = ImportServiceFixture.CreateContext();
        var (brand, category) = await ImportServiceFixture.SeedBrandAndCategoryAsync(context);
        var adminId = await ImportServiceFixture.SeedAdminUserIdAsync(context);
        var service = new EfProductImportService(context, new EfAuditWriter(context, TimeProvider.System));

        // Rows 2 and 3 share PK1 (row 3 needs the synthetic __dup3); row 4's key is the full-width
        // spelling of that same synthetic name.
        var productsCsv = ProductsHeader +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品一,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"PK1,{ImportServiceFixture.UniqueCode("PROD")},商品二,{brand.Code},{category.Code},\\N,\\N,Draft\r\n" +
            $"＿＿ＤＵＰ３,{ImportServiceFixture.UniqueCode("PROD")},商品三,{brand.Code},{category.Code},\\N,\\N,Draft\r\n";
        var request = new PreviewProductImportRequest(
            ToFile(productsCsv), ToFile(SkusHeader), ToFile(SpecificationsHeader), 1);

        var batch = await service.PreviewAsync(request, adminId, CancellationToken.None);

        Assert.NotEqual("Ready", batch.Status);
        await using var verify = ImportServiceFixture.CreateContext();
        var stored = await verify.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId ==
                verify.ImportBatches.Where(b => b.PublicId == batch.PublicId).Select(b => b.Id).First())
            .ToListAsync();
        Assert.Equal(3, stored.Count);
        Assert.NotNull(await service.GetErrorsCsvAsync(batch.PublicId, CancellationToken.None));
    }

    private static IncomingImportFile ToFile(string csv, bool withBom = false)
    {
        var bytes = ImportServiceFixture.Utf8(csv);
        return new IncomingImportFile("upload.csv", "text/csv", bytes.Length, true, () => new MemoryStream(bytes));
    }
}
