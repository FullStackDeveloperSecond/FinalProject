using System.Text;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// A minimal RFC 4180 CSV reader for import uploads: comma-delimited, double-quote escaped
/// (a doubled quote inside a quoted field is a literal quote), quoted fields may embed commas
/// and newlines. A UTF-8 BOM is stripped if present but not required — real-world exports from
/// spreadsheet tools vary on this, and rejecting a BOM-less file would just be friction with no
/// correctness benefit (匯入暫存與庫存調整設計.md only mandates BOM for the system's own
/// generated templates/error files, not for what an admin may upload).
/// </summary>
public static class DelimitedTextReader
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyList<string[]> Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var text = DecodeUtf8(content);
        var rows = new List<string[]>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (rowHasContent || field.Length > 0 || currentRow.Count > 0)
                    {
                        currentRow.Add(field.ToString());
                        rows.Add([.. currentRow]);
                        currentRow.Clear();
                        field.Clear();
                        rowHasContent = false;
                    }

                    break;
                default:
                    field.Append(c);
                    rowHasContent = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("An unterminated quoted field was found in the CSV content.");
        }

        if (rowHasContent || field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add([.. currentRow]);
        }

        return rows;
    }

    private static string DecodeUtf8(byte[] content)
    {
        var hasBom = content.Length >= 3 &&
            content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
        var span = hasBom ? content.AsSpan(3) : content.AsSpan();
        return Utf8NoBom.GetString(span);
    }
}
