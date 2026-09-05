using DoSelect.Application.Common;
using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class SpecificationRowParserTests
{
    private static readonly string[] ValidHeader =
        ["sku_key", "semantic_key", "value_type", "string_value", "decimal_value", "boolean_value", "option_code"];

    [Fact]
    public void Parse_ProducesACleanRowForAValidStringSpecification()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "COLOR", "String", "Red", "\\N", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        var row = Assert.Single(result);
        Assert.Empty(row.Errors);
        Assert.Equal("Red", row.Payload.StringValue);
    }

    [Fact]
    public void Parse_FlagsAStringSpecificationThatAlsoPopulatesADifferentValueColumn()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "COLOR", "String", "Red", "1.5", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_FlagsAnOptionSpecificationWithNoOptionCode()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "SIZE", "Option", "\\N", "\\N", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_FlagsADuplicateSkuKeySemanticKeyPairWithinTheSameBatch()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "COLOR", "String", "Red", "\\N", "\\N", "\\N"],
            ["SK1", "COLOR", "String", "Blue", "\\N", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
    }

    [Fact]
    public void Parse_GivesDifferentSkuKeySemanticKeyPairsDifferentImportKeys()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "COLOR", "String", "Red", "\\N", "\\N", "\\N"],
            ["SK2", "COLOR", "String", "Blue", "\\N", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
        Assert.True(result[0].ImportKey.Length <= 64);
    }

    [Fact]
    public void Parse_FlagsAnUndefinedValueType()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "COLOR", "Enum", "Red", "\\N", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    /// <summary>組長 PR #74 round-3, item 1：重複的 (sku_key, semantic_key) 會算出相同的 composite
    /// ImportKey，寫入時撞唯一索引成 500。</summary>
    [Fact]
    public void Parse_WhenAPairRepeats_StoresTheDuplicateUnderANonCollidingKey()
    {
        string[][] rows =
        [
            ValidHeader,
            ["SK1", "capacity_gb", "Decimal", "\\N", "512", "\\N", "\\N"],
            ["SK1", "capacity_gb", "Decimal", "\\N", "1024", "\\N", "\\N"],
        ];

        var result = SpecificationRowParser.Parse(rows);

        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[1].Errors);
        Assert.Equal("SK1", result[1].Payload.SkuKey);
        // semantic_key is normalized to upper case like every other batch-scoped key.
        Assert.Equal("CAPACITY_GB", result[1].Payload.SemanticKey);
    }

    /// <summary>組長 PR #74 round-3, item 3：規格 decimal 契約是 18,4。</summary>
    [Theory]
    [InlineData("512.00001")]
    [InlineData("100000000000000.0000")]
    public void Parse_WhenADecimalValueBreaksItsContract_MarksTheRowInvalid(string value)
    {
        string[][] rows = [ValidHeader, ["SK1", "capacity_gb", "Decimal", "\\N", value, "\\N", "\\N"]];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Contains(DomainErrorCodes.ImportValidationFailed, result[0].Errors);
    }

    [Fact]
    public void Parse_WhenADecimalValueSitsOnTheContractBoundary_StaysValid()
    {
        string[][] rows = [ValidHeader, ["SK1", "capacity_gb", "Decimal", "\\N", "99999999999999.9999", "\\N", "\\N"]];

        var result = SpecificationRowParser.Parse(rows);

        Assert.Empty(result[0].Errors);
    }
}
