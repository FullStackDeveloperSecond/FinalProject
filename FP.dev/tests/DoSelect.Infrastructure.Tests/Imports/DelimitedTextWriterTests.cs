using System.Text;
using DoSelect.Infrastructure.Imports;
using Xunit;

namespace DoSelect.Infrastructure.Tests.Imports;

public sealed class DelimitedTextWriterTests
{
    [Fact]
    public void Write_EmitsAUtf8Bom()
    {
        var bytes = DelimitedTextWriter.Write(["a", "b"], []);

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void Write_QuotesAFieldContainingACommaOrQuote()
    {
        var bytes = DelimitedTextWriter.Write(["h"], [["a,b"], ["say \"hi\""]]);

        var text = Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        Assert.Contains("\"a,b\"", text, StringComparison.Ordinal);
        Assert.Contains("\"say \"\"hi\"\"\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@cmd")]
    public void Write_DefusesAFieldThatWouldBeInterpretedAsAFormula(string dangerous)
    {
        var bytes = DelimitedTextWriter.Write(["h"], [[dangerous]]);

        var text = Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        Assert.StartsWith($"'{dangerous}", text.Split("\r\n")[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_LeavesAnOrdinaryFieldUntouched()
    {
        var bytes = DelimitedTextWriter.Write(["h"], [["import_lookup_not_found"]]);

        var text = Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        Assert.Equal("h\r\nimport_lookup_not_found\r\n", text);
    }
}
