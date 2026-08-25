using System.Globalization;
using System.Text;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Field-level normalization shared by every import dataset (匯入暫存與庫存調整設計.md's
/// "CSV 格式" section): the fixed null token is <c>\N</c> — distinct from an empty string,
/// which means "empty string", not "null". System/relational codes are Trim+Upper (matching
/// domain code normalization, e.g. Catalog's internal CatalogCode.Normalize) plus Unicode NFKC
/// so a full-width or otherwise NFKC-equivalent code still matches the same DB row; general
/// display strings are Trim+NFKC only (case preserved).
/// </summary>
internal static class ImportFieldNormalization
{
    private const string NullToken = "\\N";

    /// <summary>
    /// Returns the raw field with <c>\N</c> collapsed to null; any other value (including an
    /// empty string) passes through untouched — the two are not interchangeable per spec.
    /// </summary>
    public static string? RawOrNull(string raw) =>
        raw == NullToken ? null : raw;

    /// <summary>
    /// For domain codes that must match a persisted Catalog code (product_code, sku_code,
    /// brand_code, category_code) — mirrors Catalog's own internal CatalogCode.Normalize
    /// (Trim + Unicode NFKC + Upper) exactly, so a full-width or otherwise NFKC-equivalent code
    /// in the upload still resolves to the same DB row.
    /// </summary>
    public static string? NormalizeCode(string raw)
    {
        var value = RawOrNull(raw);
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0
            ? null
            : trimmed.Normalize(NormalizationForm.FormKC).ToUpper(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// For batch-scoped relational keys only (product_key, sku_key) — the spec documents these
    /// as "Trim 後大寫" specifically, without NFKC (unlike the domain codes above). These never
    /// touch the database directly, only other rows in the same upload.
    /// </summary>
    public static string? NormalizeKey(string raw)
    {
        var value = RawOrNull(raw);
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed.ToUpper(CultureInfo.InvariantCulture);
    }

    public static string? NormalizeText(string raw)
    {
        var value = RawOrNull(raw);
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed.Normalize(NormalizationForm.FormKC);
    }

    public static bool TryParseInt32(string raw, out int value)
    {
        value = 0;
        var text = RawOrNull(raw);
        return text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDecimal(string raw, out decimal value)
    {
        value = 0m;
        var text = RawOrNull(raw);
        return text is not null &&
            decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// The spec's Boolean columns use the fixed English tokens "true"/"false" only — not "1"/"0"
    /// or any other alias, and not case-insensitively (avoids a stray "True"/"TRUE" silently
    /// working while every other enum-like column in this format is documented as fixed-case).
    /// </summary>
    public static bool TryParseBoolean(string raw, out bool value)
    {
        value = false;
        var text = RawOrNull(raw);
        switch (text)
        {
            case "true":
                value = true;
                return true;
            case "false":
                value = false;
                return true;
            default:
                return false;
        }
    }
}
