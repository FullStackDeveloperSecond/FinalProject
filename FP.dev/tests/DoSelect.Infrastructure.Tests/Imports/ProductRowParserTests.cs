using DoSelect.Application.Common;
using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class ProductRowParserTests
{
    private static readonly string[] ValidHeader =
        ["product_key", "product_code", "name_zh_tw", "brand_code", "category_code", "description_zh_tw", "warranty_months", "status"];

    [Fact]
    public void Parse_ThrowsWhenTheHeaderIsMissingAColumn()
    {
        string[][] rows = [ValidHeader.Take(7).ToArray()];

        var exception = Assert.Throws<ImportBatchParseException>(() => ProductRowParser.Parse(rows));
        Assert.Equal(DomainErrorCodes.ImportFormatUnsupported, exception.ErrorCode);
    }

    [Fact]
    public void Parse_ThrowsWhenTheHeaderOrderDoesNotMatchTheContract()
    {
        var reordered = ValidHeader.Reverse().ToArray();
        string[][] rows = [reordered];

        Assert.Throws<ImportBatchParseException>(() => ProductRowParser.Parse(rows));
    }

    [Fact]
    public void Parse_ReturnsNoRowsForAHeaderOnlyFile()
    {
        // A single dataset having zero data rows is not itself an error — Specifications in
        // particular may legitimately be empty. EfProductImportService rejects the batch only
        // if ALL THREE datasets combined have zero rows.
        string[][] rows = [ValidHeader];

        var result = ProductRowParser.Parse(rows);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_ThrowsWhenTheFileHasNoHeaderAtAll()
    {
        var exception = Assert.Throws<ImportBatchParseException>(() => ProductRowParser.Parse([]));
        Assert.Equal(DomainErrorCodes.ImportDatasetMissing, exception.ErrorCode);
    }

    [Fact]
    public void Parse_ProducesACleanRowWithNoErrorsForFullyValidInput()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "PROD-1", "Product 1", "ACME", "CAT-A", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        var row = Assert.Single(result);
        Assert.Empty(row.Errors);
        Assert.Equal("PK1", row.ImportKey);
        Assert.Equal("PROD-1", row.Payload.ProductCode);
        Assert.Null(row.Payload.DescriptionZhTw);
        Assert.Null(row.Payload.WarrantyMonths);
    }

    [Fact]
    public void Parse_FlagsADuplicateProductKeyWithinTheSameBatch()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "PROD-1", "Product 1", "ACME", "CAT-A", "\\N", "\\N", "Draft"],
            ["PK1", "PROD-2", "Product 2", "ACME", "CAT-A", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
    }

    [Fact]
    public void Parse_FlagsAnOutOfRangeWarrantyMonths()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "PROD-1", "Product 1", "ACME", "CAT-A", "\\N", "121", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_FlagsAnUndefinedStatusToken()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "PROD-1", "Product 1", "ACME", "CAT-A", "\\N", "\\N", "Discontinued"],
        ];

        var result = ProductRowParser.Parse(rows);

        // Discontinued is a real ProductStatus but not offered by the import contract (only
        // Draft/Published/Unpublished) — must still be rejected, not silently accepted.
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_FlagsARowWithTheWrongColumnCountWithoutThrowing()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "PROD-1"],
        ];

        var result = ProductRowParser.Parse(rows);

        var row = Assert.Single(result);
        Assert.NotEmpty(row.Errors);
    }
}
