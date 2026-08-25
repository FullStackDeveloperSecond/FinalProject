using System.Text;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Writes RFC 4180 CSV with UTF-8 BOM (匯入暫存與庫存調整設計.md's export format) and
/// defuses formula injection: a cell whose first character is one Excel/Sheets would interpret
/// as a formula prefix (=, +, -, @, tab, CR) is prefixed with a leading apostrophe so it lands
/// in the cell as inert text instead of executing when the downloaded error file is opened.
/// </summary>
public static class DelimitedTextWriter
{
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@', '\t', '\r'];

    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        // Encoding.GetBytes(string) never emits a BOM regardless of encoderShouldEmitUTF8Identifier
        // — that flag only affects GetPreamble(), which must be prepended explicitly.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = utf8.GetPreamble();
        var content = utf8.GetBytes(builder.ToString());
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(EscapeField(DefuseFormula(fields[i])));
        }

        builder.Append("\r\n");
    }

    private static string DefuseFormula(string value) =>
        value.Length > 0 && FormulaTriggerChars.Contains(value[0]) ? $"'{value}" : value;

    private static string EscapeField(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
