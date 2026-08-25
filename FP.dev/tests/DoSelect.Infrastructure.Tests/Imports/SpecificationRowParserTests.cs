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
}
