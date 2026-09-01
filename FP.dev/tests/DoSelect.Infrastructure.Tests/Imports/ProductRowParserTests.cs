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

    /// <summary>組長 PR #74 round-3, item 1：重複的 product_key 必須能安全保存——儲存鍵改成不衝突的
    /// 合成鍵（否則之後 ToDictionary／唯一索引直接炸成 500），原始 key 仍留在 payload。</summary>
    [Fact]
    public void Parse_WhenAProductKeyRepeats_StoresTheDuplicateUnderANonCollidingKey()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "CODE-1", "商品一", "BRAND", "CAT", "\\N", "\\N", "Draft"],
            ["PK1", "CODE-2", "商品二", "BRAND", "CAT", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        Assert.Equal("PK1", result[0].ImportKey);
        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
        // The offending original key survives for the error download.
        Assert.Equal("PK1", result[1].Payload.ProductKey);
    }

    /// <summary>組長 PR #74 round-3, item 2：同一批不同 product_key 指向同一個 product_code——新商品
    /// 會等到 Confirm 才撞唯一索引，既有商品則兩列連續更新成 last-row-wins，而 Preview 還是 Ready。
    /// 重複的 product_code 必須在 Preview 就是列級錯誤。</summary>
    [Fact]
    public void Parse_WhenTwoRowsShareAProductCode_MarksTheSecondRowInvalid()
    {
        string[][] rows =
        [
            ValidHeader,
            ["PK1", "SAME-CODE", "商品一", "BRAND", "CAT", "\\N", "\\N", "Draft"],
            ["PK2", "SAME-CODE", "商品二", "BRAND", "CAT", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        // 組長 PR #74 round-4 review (P3)：兩列都要有穩定錯誤結果，錯誤 CSV 才指得出完整衝突集合。
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
    }

    /// <summary>組長 PR #74 round-4 review (P2)：合成儲存鍵必須以資料庫的比較規則配置。CI 的 SQL
    /// Server 用預設的 CI collation，應用層用 Ordinal 比會覺得 `__DUP3`（使用者自己的 key）與
    /// `__dup3`（合成鍵）不同，資料庫卻認為相同，唯一索引因此再次炸成 500。</summary>
    [Fact]
    public void Parse_WhenAUserKeyLooksLikeASyntheticKey_TheSyntheticKeyStillDoesNotCollide()
    {
        string[][] rows =
        [
            ValidHeader,
            // Row 2 and 3 share a key, so row 3 needs a synthetic one; row 4 already occupies the
            // name that synthetic key would otherwise take.
            ["PK1", "CODE-1", "商品一", "BRAND", "CAT", "\\N", "\\N", "Draft"],
            ["PK1", "CODE-2", "商品二", "BRAND", "CAT", "\\N", "\\N", "Draft"],
            ["__dup3", "CODE-3", "商品三", "BRAND", "CAT", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        var storageKeys = result.Select(row => row.ImportKey).ToList();
        Assert.Equal(
            storageKeys.Count,
            storageKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>缺少 key 的列也用合成鍵，同樣不能被使用者的 `__ROW2` 撞到。</summary>
    [Fact]
    public void Parse_WhenAUserKeyLooksLikeAMissingKeyPlaceholder_TheSyntheticKeyStillDoesNotCollide()
    {
        string[][] rows =
        [
            ValidHeader,
            ["\\N", "CODE-1", "商品一", "BRAND", "CAT", "\\N", "\\N", "Draft"],
            ["__ROW2", "CODE-2", "商品二", "BRAND", "CAT", "\\N", "\\N", "Draft"],
        ];

        var result = ProductRowParser.Parse(rows);

        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey, StringComparer.OrdinalIgnoreCase);
    }
}
