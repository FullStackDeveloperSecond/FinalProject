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

    /// <summary>組長 PR #74 round-3 裁定 A1：上傳同時接受有 BOM 與無 BOM 的 UTF-8，內容一致。</summary>
    [Fact]
    public void Parse_AcceptsBothBomAndBomlessUtf8WithIdenticalResults()
    {
        var bomless = Utf8("a,b\n匯入,2\n");
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bomless).ToArray();

        var fromBomless = DelimitedTextReader.Parse(bomless);
        var fromBom = DelimitedTextReader.Parse(withBom);

        Assert.Equal(fromBomless[1], fromBom[1]);
        Assert.Equal(["匯入", "2"], fromBom[1]);
    }

    /// <summary>組長 PR #74 round-3, item 5：非法 UTF-8 位元組先前被 replacement fallback 靜默換成
    /// U+FFFD，壞檔案會「成功」匯入一堆問號。必須明確拒絕。</summary>
    [Fact]
    public void Parse_RejectsInvalidUtf8InsteadOfEmittingReplacementCharacters()
    {
        // 0xFF is not a legal UTF-8 byte in any position.
        var invalid = Utf8("a,b\n").Concat(new byte[] { 0xFF, 0xFE }).Concat(Utf8(",2\n")).ToArray();

        var exception = Assert.Throws<FormatException>(() => DelimitedTextReader.Parse(invalid));
        Assert.Contains("UTF-8", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>組長 PR #74 round-3, item 5：RFC 4180 的引號只能包住整個欄位。欄位中途的引號與
    /// 收尾引號後的殘字先前都被靜默接受。</summary>
    [Theory]
    [InlineData("a,b\nab\"cd\",2\n")]
    [InlineData("a,b\n\"ab\"cd,2\n")]
    public void Parse_RejectsMisplacedQuotes(string content)
    {
        Assert.Throws<FormatException>(() => DelimitedTextReader.Parse(Utf8(content)));
    }

    /// <summary>正常的 RFC 4180 引號用法必須維持可用（含跳脫的雙引號與內嵌逗號）。</summary>
    [Fact]
    public void Parse_StillAcceptsProperlyQuotedFields()
    {
        var rows = DelimitedTextReader.Parse(Utf8("a,b\n\"x,y\",\"say \"\"hi\"\"\"\n"));

        Assert.Equal(["x,y", "say \"hi\""], rows[1]);
    }
}
