using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class ImportFieldNormalizationTests
{
    [Fact]
    public void RawOrNull_TreatsTheNullTokenAsNull()
    {
        Assert.Null(ImportFieldNormalization.RawOrNull("\\N"));
    }

    [Fact]
    public void RawOrNull_TreatsAnEmptyStringAsEmptyNotNull()
    {
        Assert.Equal(string.Empty, ImportFieldNormalization.RawOrNull(""));
    }

    [Fact]
    public void NormalizeCode_TrimsUppercasesAndAppliesNfkc()
    {
        Assert.Equal("ABC", ImportFieldNormalization.NormalizeCode("  abc  "));
        // U+FF21 FULLWIDTH LATIN CAPITAL LETTER A normalizes (NFKC) to U+0041 'A'.
        Assert.Equal("A", ImportFieldNormalization.NormalizeCode("Ａ"));
    }

    [Fact]
    public void NormalizeCode_ReturnsNullForTheNullTokenOrBlank()
    {
        Assert.Null(ImportFieldNormalization.NormalizeCode("\\N"));
        Assert.Null(ImportFieldNormalization.NormalizeCode("   "));
    }

    [Fact]
    public void NormalizeKey_TrimsAndUppercasesWithoutNfkc()
    {
        Assert.Equal("ABC", ImportFieldNormalization.NormalizeKey("  abc  "));
        // Full-width 'A' must NOT collapse for batch-scoped keys (spec: Trim + Upper only).
        Assert.NotEqual("A", ImportFieldNormalization.NormalizeKey("Ａ"));
    }

    [Fact]
    public void NormalizeText_TrimsAndAppliesNfkcButPreservesCase()
    {
        Assert.Equal("Hello", ImportFieldNormalization.NormalizeText("  Hello  "));
    }

    [Theory]
    [InlineData("true", true, true)]
    [InlineData("false", true, false)]
    [InlineData("True", false, false)]
    [InlineData("1", false, false)]
    [InlineData("\\N", false, false)]
    public void TryParseBoolean_OnlyAcceptsTheFixedLowercaseTokens(string raw, bool expectedSuccess, bool expectedValue)
    {
        var success = ImportFieldNormalization.TryParseBoolean(raw, out var value);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, value);
        }
    }

    [Fact]
    public void TryParseInt32_ReturnsFalseForTheNullToken()
    {
        Assert.False(ImportFieldNormalization.TryParseInt32("\\N", out _));
    }

    [Fact]
    public void TryParseDecimal_AcceptsADotDecimalPoint()
    {
        Assert.True(ImportFieldNormalization.TryParseDecimal("12.50", out var value));
        Assert.Equal(12.50m, value);
    }
}
