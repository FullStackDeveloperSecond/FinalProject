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
    // 模板欄位契約的 decimal precision/scale，與 CatalogConfigurations 的資料庫欄位定義一致
    // (組長 PR #74 round-3, item 3)：list_price/unit_cost 18,2、weight_kg 10,3、長寬高 10,2。
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;
    private const int WeightPrecision = 10;
    private const int WeightScale = 3;
    private const int DimensionPrecision = 10;
    private const int DimensionScale = 2;

    public static readonly IReadOnlyList<string> Header =
    [
        "sku_key", "sku_code", "product_key", "name_zh_tw", "list_price", "unit_cost",
        "weight_kg", "length_cm", "width_cm", "height_cm", "requires_prepayment", "status",
    ];

    public static IReadOnlyList<StagedImportRow<SkuPayload>> Parse(IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "Skus");
        var staged = new List<StagedImportRow<SkuPayload>>(dataRows.Count);

        // 組長 PR #74 round-6 review (裁定 A1)：同 ProductRowParser——`SK1` 與全形 `ＳＫ１` 在資料庫
        // 的 collation 下是同一個 key。
        var keyConflictCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var canonicalKeysInUse = new HashSet<string>(StringComparer.Ordinal);

        // 組長 PR #74 round-4 review (P2／P3)：同 ProductRowParser——合成儲存鍵要以資料庫的比較
        // 規則避開使用者實際用到的 key，重複的 sku_code 也要每一列都標錯。
        var keys = new ImportStorageKeyAllocator();
        var skuCodeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in dataRows.Where(row => row.Length == Header.Count))
        {
            var candidateKey = ImportFieldNormalization.NormalizeKey(candidate[0]);
            keys.Reserve(candidateKey);
            if (ImportStorageKeyAllocator.CanStore(candidateKey))
            {
                var canonical = ImportStorageKeyAllocator.Canonicalize(candidateKey!);
                keyConflictCounts[canonical] = keyConflictCounts.GetValueOrDefault(canonical) + 1;
            }

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

            if (!ImportStorageKeyAllocator.CanStore(skuKey))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (keyConflictCounts.GetValueOrDefault(ImportStorageKeyAllocator.Canonicalize(skuKey!)) > 1)
            {
                // 組長 PR #74 round-3 item 1／round-6 裁定 A1：同 ProductRowParser——衝突集合每一列
                // 都標錯，第一列保留原鍵、其餘改用合成鍵，原始 sku_key 留在 payload 供錯誤下載。
                if (!canonicalKeysInUse.Add(ImportStorageKeyAllocator.Canonicalize(skuKey!)))
                {
                    row.ImportKey = keys.Allocate("dup", sourceRowNumber);
                }

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

            // 組長 PR #74 round-3, item 3：金額與尺寸都要照模板欄位契約驗 precision/scale，否則
            // 超出 scale 的值會在 SQL Server 被靜默捨入、超出 precision 的值會寫入失敗。
            if (!hasListPrice || listPrice < 0 ||
                !ImportFieldNormalization.FitsDecimalContract(listPrice, MoneyPrecision, MoneyScale))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (!hasUnitCost || unitCost < 0 ||
                !ImportFieldNormalization.FitsDecimalContract(unitCost, MoneyPrecision, MoneyScale))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            ValidateOptionalPositive(fields[6], hasWeight, weightKg, row, WeightPrecision, WeightScale);
            ValidateOptionalPositive(fields[7], hasLength, lengthCm, row, DimensionPrecision, DimensionScale);
            ValidateOptionalPositive(fields[8], hasWidth, widthCm, row, DimensionPrecision, DimensionScale);
            ValidateOptionalPositive(fields[9], hasHeight, heightCm, row, DimensionPrecision, DimensionScale);

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

    private static void ValidateOptionalPositive(
        string raw,
        bool hasValue,
        decimal value,
        StagedImportRow<SkuPayload> row,
        int precision,
        int scale)
    {
        var isNull = ImportFieldNormalization.RawOrNull(raw) is null;
        if (isNull)
        {
            return;
        }

        if (!hasValue || value <= 0 || !ImportFieldNormalization.FitsDecimalContract(value, precision, scale))
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
