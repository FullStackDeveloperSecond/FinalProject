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

    /// <summary>組長 PR #74 round-3, item 1：重複 sku_key 的儲存鍵必須不衝突。</summary>
    [Fact]
    public void Parse_WhenASkuKeyRepeats_StoresTheDuplicateUnderANonCollidingKey()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU一", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
            ["SK1", "SKU-2", "PK1", "SKU二", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
        Assert.Equal("SK1", result[1].Payload.SkuKey);
    }

    /// <summary>組長 PR #74 round-3, item 3：金額 18,2、weight 10,3、尺寸 10,2。多一位小數會在
    /// SQL Server 被靜默捨入，超出 precision 則寫入失敗——兩者都必須是 Preview 的列級錯誤。</summary>
    [Theory]
    [InlineData(4, "1000.005")]              // list_price scale 3 > 2
    [InlineData(5, "600.001")]               // unit_cost scale 3 > 2
    [InlineData(6, "1.0001")]                // weight_kg scale 4 > 3
    [InlineData(7, "10.001")]                // length_cm scale 3 > 2
    [InlineData(4, "10000000000000000.00")]  // list_price precision 18 exceeded
    [InlineData(7, "100000000.00")]          // length_cm precision 10 exceeded
    public void Parse_WhenADecimalBreaksItsPrecisionOrScaleContract_MarksTheRowInvalid(int fieldIndex, string value)
    {
        var row = new[] { "SK1", "SKU-1", "PK1", "SKU一", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft" };
        row[fieldIndex] = value;
        string[][] rows = [ValidHeader, row];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    /// <summary>邊界值必須通過：剛好用滿 scale、剛好在 precision 之內。</summary>
    [Fact]
    public void Parse_WhenDecimalsSitExactlyOnTheContractBoundary_StaysValid()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU一", "9999999999999999.99", "600.55", "1.999", "99999999.99", "10.5", "10.25", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Empty(result[0].Errors);
    }

    /// <summary>組長 PR #74 round-4 review (P3)：重複的 sku_code 也要每一列都標錯。</summary>
    [Fact]
    public void Parse_WhenTwoRowsShareASkuCode_MarksBothRowsInvalid()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SAME-SKU", "PK1", "SKU一", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
            ["SK2", "SAME-SKU", "PK1", "SKU二", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportSkuCodeDuplicate, result[0].Errors);
        Assert.Contains(DomainErrorCodes.ImportSkuCodeDuplicate, result[1].Errors);
    }

    /// <summary>組長 PR #74 round-6 review (裁定 A1)：sku_key 的 width-equivalent 碰撞。</summary>
    [Fact]
    public void Parse_WhenTwoSkuKeysAreWidthEquivalent_MarksBothAndKeepsStorageKeysDistinct()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SKU-1", "PK1", "SKU一", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
            ["ＳＫ１", "SKU-2", "PK1", "SKU二", "1000", "600", "\\N", "\\N", "\\N", "\\N", "false", "Draft"],
        ];

        var result = SkuRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
        Assert.Equal("SK1", result[0].ImportKey);
        Assert.NotEqual(
            ImportStorageKeyAllocator.Canonicalize(result[0].ImportKey),
            ImportStorageKeyAllocator.Canonicalize(result[1].ImportKey));
        Assert.Equal("ＳＫ１", result[1].OriginalKey);
    }
}
