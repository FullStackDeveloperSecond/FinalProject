using System.IO.Compression;
using System.Text;
using DoSelect.Infrastructure.Imports;

namespace DoSelect.Infrastructure.Tests.Imports;

/// <summary>
/// UC-IMPORT-01 模板下載。這裡最重要的一支不是「檔名對不對」，而是「下載到的模板真的能通過上傳
/// 端的 Header 驗證」——那正是抄第二份標題列時會壞掉、而且只會壞在管理員身上的地方。
/// </summary>
public sealed class ImportTemplateServiceTests
{
    [Fact]
    public void ProductTemplate_ContainsTheThreeDatasetsAsCsvEntries()
    {
        var template = new ImportTemplateService().GetCurrentProductTemplate();

        Assert.Equal("application/zip", template.ContentType);
        Assert.EndsWith(".zip", template.FileName, StringComparison.Ordinal);

        var entries = ReadEntries(template.Content);
        Assert.Equal(
            new[] { "products.csv", "skus.csv", "specifications.csv" },
            entries.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 模板的標題列必須原封不動通過 Parser 的驗證。ImportHeaderValidator 對順序、大小寫、多餘或
    /// 缺少的欄位都是整批拒絕，所以這支只要綠，就代表模板與驗證器沒有分岔。
    /// </summary>
    [Fact]
    public void ProductTemplate_HeadersArePreciselyWhatTheParsersAccept()
    {
        var entries = ReadEntries(new ImportTemplateService().GetCurrentProductTemplate().Content);

        AssertHeaderMatches(entries["products.csv"], ProductRowParser.Header);
        AssertHeaderMatches(entries["skus.csv"], SkuRowParser.Header);
        AssertHeaderMatches(entries["specifications.csv"], SpecificationRowParser.Header);
    }

    /// <summary>
    /// 只有標題列，沒有範例資料：範例值一旦被原樣送出就是髒資料，而且會引用到遲早失效的品牌／
    /// 分類代碼。
    /// </summary>
    [Fact]
    public void ProductTemplate_ShipsHeadersOnlyWithNoSampleRows()
    {
        var entries = ReadEntries(new ImportTemplateService().GetCurrentProductTemplate().Content);

        foreach (var (name, content) in entries)
        {
            var lines = Decode(content).Split(
                ["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length == 1, $"{name} should carry only its header, found {lines.Length} lines");
        }
    }

    /// <summary>Excel 只有看到 BOM 才會把 UTF-8 的中文正確解讀。</summary>
    [Fact]
    public void ProductTemplate_CsvEntriesCarryTheUtf8Bom()
    {
        var entries = ReadEntries(new ImportTemplateService().GetCurrentProductTemplate().Content);

        foreach (var (name, content) in entries)
        {
            Assert.True(
                content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF,
                $"{name} is missing the UTF-8 BOM");
        }
    }

    private static void AssertHeaderMatches(byte[] content, IReadOnlyList<string> expected)
    {
        var rows = DelimitedTextReader.Parse(content);
        // 通不過就會丟 ImportBatchParseException——直接讓例外冒出來，訊息比自己組的斷言清楚。
        ImportHeaderValidator.ValidateAndGetDataRows(rows, expected, "Template");
        Assert.Equal(expected, rows[0].Select(column => column.Trim()).ToArray());
    }

    private static Dictionary<string, byte[]> ReadEntries(byte[] zip)
    {
        using var buffer = new MemoryStream(zip);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            entries[entry.FullName] = copy.ToArray();
        }

        return entries;
    }

    private static string Decode(byte[] content) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetString(content).TrimStart('﻿');
}
