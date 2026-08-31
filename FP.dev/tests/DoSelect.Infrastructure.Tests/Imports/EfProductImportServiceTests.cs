using System.Text;
using DoSelect.Application.Imports;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Auditing;
using DoSelect.Infrastructure.Imports;
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
    {
        var bytes = ImportServiceFixture.Utf8(csv);
        return new IncomingImportFile("upload.csv", "text/csv", bytes.Length, true, () => new MemoryStream(bytes));
    }
}
