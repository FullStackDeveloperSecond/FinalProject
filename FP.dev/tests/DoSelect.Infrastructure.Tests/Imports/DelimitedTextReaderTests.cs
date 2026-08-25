using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class DelimitedTextReaderTests
{
    [Fact]
    public void Parse_SplitsSimpleCommaDelimitedRows()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b,c\n1,2,3\n"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void Parse_StripsUtf8Bom()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("a,b\n1,2\n")).ToArray();

        var rows = DelimitedTextReader.Parse(withBom);

        Assert.Equal(["a", "b"], rows[0]);
    }

    [Fact]
    public void Parse_ToleratesMissingBom()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b\n1,2\n"));

        Assert.Equal(["a", "b"], rows[0]);
    }

    [Fact]
    public void Parse_UnescapesDoubledQuotesInsideAQuotedField()
    {
        var rows = DelimitedTextReader.Parse(Utf8("name\n\"Say \"\"hi\"\"\"\n"));

        Assert.Equal(["Say \"hi\""], rows[1]);
    }

    [Fact]
    public void Parse_AllowsEmbeddedCommasAndNewlinesInsideAQuotedField()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b\n\"x,y\",\"line1\nline2\"\n"));

        Assert.Equal(["x,y", "line1\nline2"], rows[1]);
    }

    [Fact]
    public void Parse_HandlesTheLastRowWithoutATrailingNewline()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b\n1,2"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void Parse_ThrowsForAnUnterminatedQuotedField()
    {
        Assert.Throws<FormatException>(() => DelimitedTextReader.Parse(Utf8("a,b\n\"unterminated,2\n")));
    }

    [Fact]
    public void Parse_PreservesAnEmptyFieldDistinctFromAMissingRow()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b,c\n1,,3\n"));

        Assert.Equal(["1", "", "3"], rows[1]);
    }

    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
