using DoSelect.Application.Common;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Pure (no database access) parsing/field-validation for the Skus dataset of a product import —
/// 匯入暫存與庫存調整設計.md's "商品模板 v1 欄位契約 / Skus" table. product_key
/// cross-reference resolution and Insert-vs-Update diffing happen later in
/// EfProductImportService, since both need the database (and, for product_key, the sibling
/// Products dataset already parsed in the same batch).
/// </summary>
internal static class SkuRowParser
{
    public static readonly IReadOnlyList<string> Header =
    [
        "sku_key", "sku_code", "product_key", "name_zh_tw", "list_price", "unit_cost",
        "weight_kg", "length_cm", "width_cm", "height_cm", "requires_prepayment", "status",
    ];

    public static IReadOnlyList<StagedImportRow<SkuPayload>> Parse(IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "Skus");
        var staged = new List<StagedImportRow<SkuPayload>>(dataRows.Count);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        // 組長 PR #74 round-4 review (P2／P3)：同 ProductRowParser——合成儲存鍵要以資料庫的比較
        // 規則避開使用者實際用到的 key，重複的 sku_code 也要每一列都標錯。
        var keys = new ImportStorageKeyAllocator();
        var skuCodeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in dataRows.Where(row => row.Length == Header.Count))
        {
            keys.Reserve(ImportFieldNormalization.NormalizeKey(candidate[0]));
            var candidateCode = ImportFieldNormalization.NormalizeCode(candidate[1]);
            if (candidateCode is not null)
            {
                skuCodeCounts[candidateCode] = skuCodeCounts.GetValueOrDefault(candidateCode) + 1;
            }
        }

        for (var i = 0; i < dataRows.Count; i++)
        {
            var sourceRowNumber = i + 2;
            var fields = dataRows[i];
            if (fields.Length != Header.Count)
            {
                staged.Add(BuildMalformedRow(sourceRowNumber, fields, keys));
                continue;
            }

            var skuKey = ImportFieldNormalization.NormalizeKey(fields[0]);
            var skuCode = ImportFieldNormalization.NormalizeCode(fields[1]);
            var productKey = ImportFieldNormalization.NormalizeKey(fields[2]);
            var nameZhTw = ImportFieldNormalization.NormalizeText(fields[3]);
            var hasListPrice = ImportFieldNormalization.TryParseDecimal(fields[4], out var listPrice);
            var hasUnitCost = ImportFieldNormalization.TryParseDecimal(fields[5], out var unitCost);
            var hasWeight = ImportFieldNormalization.TryParseDecimal(fields[6], out var weightKg);
            var hasLength = ImportFieldNormalization.TryParseDecimal(fields[7], out var lengthCm);
            var hasWidth = ImportFieldNormalization.TryParseDecimal(fields[8], out var widthCm);
            var hasHeight = ImportFieldNormalization.TryParseDecimal(fields[9], out var heightCm);
            var hasPrepayment = ImportFieldNormalization.TryParseBoolean(fields[10], out var requiresPrepayment);
            var status = ImportFieldNormalization.NormalizeKey(fields[11]);

            var payload = new SkuPayload(
                skuKey ?? $"__row{sourceRowNumber}",
                skuCode,
                productKey ?? string.Empty,
                nameZhTw,
                hasListPrice ? listPrice : null,
                hasUnitCost ? unitCost : null,
                hasWeight ? weightKg : null,
                hasLength ? lengthCm : null,
                hasWidth ? widthCm : null,
                hasHeight ? heightCm : null,
                hasPrepayment ? requiresPrepayment : null,
                status);

            var row = new StagedImportRow<SkuPayload>
            {
                SourceRowNumber = sourceRowNumber,
                // 組長 PR #74 round-5 review (P2)：同 ProductRowParser——超長 key 也要走合成鍵。
                ImportKey = ImportStorageKeyAllocator.CanStore(skuKey)
                    ? skuKey!
                    : keys.Allocate("row", sourceRowNumber),
                OriginalKey = skuKey,
                Payload = payload,
                RawFields = fields,
            };

            if (skuKey is null || skuKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (!seenKeys.Add(skuKey))
            {
                // 組長 PR #74 round-3, item 1：同 ProductRowParser——重複列改用不衝突的儲存鍵，
                // 原始 sku_key 保留在 payload 供錯誤下載。
                row.ImportKey = keys.Allocate("dup", sourceRowNumber);
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            // Empty sku_code means "insert, system-assigns a code" — a legitimate value distinct
            // from an invalid/missing field, unlike every other required column here.
            var skuCodeRaw = ImportFieldNormalization.RawOrNull(fields[1]);
            if (skuCodeRaw is not null && skuCodeRaw.Length > 0 && (skuCode is null || skuCode.Length > 64))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (skuCode is not null && skuCodeCounts.GetValueOrDefault(skuCode) > 1)
            {
                row.AddError(DomainErrorCodes.ImportSkuCodeDuplicate);
            }

            if (productKey is null || productKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (nameZhTw is null || nameZhTw.Length > 160)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (!hasListPrice || listPrice < 0)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (!hasUnitCost || unitCost < 0)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            ValidateOptionalPositive(fields[6], hasWeight, weightKg, row);
            ValidateOptionalPositive(fields[7], hasLength, lengthCm, row);
            ValidateOptionalPositive(fields[8], hasWidth, widthCm, row);
            ValidateOptionalPositive(fields[9], hasHeight, heightCm, row);

            if (!hasPrepayment)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (status is null || !Enum.TryParse<SkuImportStatus>(status, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            staged.Add(row);
        }

        return staged;
    }

    private static void ValidateOptionalPositive(string raw, bool hasValue, decimal value, StagedImportRow<SkuPayload> row)
    {
        var isNull = ImportFieldNormalization.RawOrNull(raw) is null;
        if (isNull)
        {
            return;
        }

        if (!hasValue || value <= 0)
        {
            row.AddError(DomainErrorCodes.ImportValidationFailed);
        }
    }

    private static StagedImportRow<SkuPayload> BuildMalformedRow(
        int sourceRowNumber, string[] fields, ImportStorageKeyAllocator keys)
    {
        var row = new StagedImportRow<SkuPayload>
        {
            SourceRowNumber = sourceRowNumber,
            ImportKey = keys.Allocate("row", sourceRowNumber),
            OriginalKey = null,
            Payload = new SkuPayload(
                $"__row{sourceRowNumber}", null, string.Empty, null, null, null, null, null, null, null, null, null),
            RawFields = fields,
        };
        row.AddError(DomainErrorCodes.ImportValidationFailed);
        return row;
    }
}

internal enum SkuImportStatus
{
    Draft,
    Published,
    Unpublished,
}
