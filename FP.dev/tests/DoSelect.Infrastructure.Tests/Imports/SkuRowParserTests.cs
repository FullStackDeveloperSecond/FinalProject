using DoSelect.Application.Common;
using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class SkuRowParserTests
{
    private static readonly string[] ValidHeader =
    [
        "sku_key", "sku_code", "product_key", "name_zh_tw", "list_price", "unit_cost",
        "weight_kg", "length_cm", "width_cm", "height_cm", "requires_prepayment", "status",
    ];

    [Fact]
    public void Parse_ProducesACleanRowForFullyValidInputWithAnExplicitCode()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU 1", "1000", "700", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        var row = Assert.Single(result);
        Assert.Empty(row.Errors);
        Assert.Equal("SKU-1", row.Payload.SkuCode);
        Assert.Equal(1000m, row.Payload.ListPrice);
    }

    [Fact]
    public void Parse_AllowsAnEmptySkuCodeAsANewSystemGeneratedSku()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "", "PK1", "SKU 1", "1000", "700", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        var row = Assert.Single(result);
        Assert.Empty(row.Errors);
        Assert.Null(row.Payload.SkuCode);
    }

    [Fact]
    public void Parse_FlagsADuplicateSkuCodeWithinTheSameBatch()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU 1", "1000", "700", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
            ["SK2", "SKU-1", "PK1", "SKU 2", "2000", "1400", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportSkuCodeDuplicate, result[1].Errors);
    }

    [Fact]
    public void Parse_FlagsANegativeListPrice()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU 1", "-1", "700", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_FlagsAZeroWeightAsInvalid_OnlyPositiveOrNullIsAllowed()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU 1", "1000", "700", "0", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_AcceptsTheSmallestLegalWeight()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU 1", "1000", "700", "0.001", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Empty(result[0].Errors);
    }
}
