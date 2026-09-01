using System.Text;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// A minimal RFC 4180 CSV reader for import uploads: comma-delimited, double-quote escaped
/// (a doubled quote inside a quoted field is a literal quote), quoted fields may embed commas
/// and newlines. A UTF-8 BOM is stripped if present but not required — 組長 PR #74 round-3 裁定
/// A1：上傳同時接受 UTF-8 with BOM 與 without BOM，而系統自己產生的模板／錯誤 CSV 固定輸出 BOM
/// (DelimitedTextWriter)。非法 UTF-8 與不符 RFC 4180 的引號位置一律明確拒絕，不做靜默修補。
/// </summary>
public static class DelimitedTextReader
{
    // 組長 PR #74 round-3, item 5：解碼必須嚴格。預設的 UTF8Encoding 用 replacement fallback，非法
    // UTF-8 位元組會被靜默換成 U+FFFD，於是壞掉的檔案「成功」匯入一堆問號字元。throwOnInvalidBytes
    // 讓它丟例外，再映射成穩定的 import_format_unsupported。
    private static readonly UTF8Encoding StrictUtf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<string[]> Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var text = DecodeUtf8(content);
        var rows = new List<string[]>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;
        // 組長 PR #74 round-3, item 5：RFC 4180 只允許引號包住「整個」欄位。舊的讀取器把欄位中間的
        // 引號當成開引號、把收尾引號後面的字元直接續接，等於靜默接受非 RFC4180 的內容。這兩個旗標
        // 讓「引號出現在欄位中途」與「收尾引號後還有字元」都變成明確的格式錯誤。
        var fieldHasContent = false;
        var fieldWasQuoted = false;

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
                        fieldWasQuoted = true;
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
                    if (fieldHasContent || fieldWasQuoted)
                    {
                        throw new FormatException(
                            "A double quote may only open a field in RFC 4180 CSV; found one inside or after a field.");
                    }

                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    fieldHasContent = false;
                    fieldWasQuoted = false;
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

                    fieldHasContent = false;
                    fieldWasQuoted = false;
                    break;
                default:
                    if (fieldWasQuoted)
                    {
                        throw new FormatException(
                            "A quoted CSV field must end at its closing quote; found trailing characters after it.");
                    }

                    field.Append(c);
                    rowHasContent = true;
                    fieldHasContent = true;
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
        try
        {
            return StrictUtf8NoBom.GetString(span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException(
                "The file is not valid UTF-8 text.", exception);
        }
    }
}
