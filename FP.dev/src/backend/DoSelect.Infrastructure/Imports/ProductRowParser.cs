using DoSelect.Application.Common;
using DoSelect.Domain.Imports;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// Pure (no database access) parsing/field-validation for the Products dataset of a product
/// import — 匯入暫存與庫存調整設計.md's "商品模板 v1 欄位契約 / Products" table. Lookup
/// resolution (brand_code/category_code existence, Insert-vs-Update against an existing
/// Product) happens later, in EfProductImportService, since it needs the database.
/// </summary>
internal static class ProductRowParser
{
    public static readonly IReadOnlyList<string> Header =
    [
        "product_key", "product_code", "name_zh_tw", "brand_code",
        "category_code", "description_zh_tw", "warranty_months", "status",
    ];

    public static IReadOnlyList<StagedImportRow<ProductPayload>> Parse(IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "Products");
        var staged = new List<StagedImportRow<ProductPayload>>(dataRows.Count);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        // 組長 PR #74 round-4 review (P2／P3)：先掃一遍再判。
        //  - 儲存鍵配置器要先知道整個資料集實際用到哪些 key，合成鍵才不會用資料庫的比較規則撞上
        //    使用者自己的 key（例如 __DUP4）。
        //  - 重複的 product_code 必須「每一列」都標錯，不能只標第二筆以後：錯誤 CSV 要能指出完整
        //    的衝突集合，管理員才知道兩列都要改。
        var keys = new ImportStorageKeyAllocator();
        var productCodeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in dataRows.Where(row => row.Length == Header.Count))
        {
            keys.Reserve(ImportFieldNormalization.NormalizeKey(candidate[0]));
            var candidateCode = ImportFieldNormalization.NormalizeCode(candidate[1]);
            if (candidateCode is not null)
            {
                productCodeCounts[candidateCode] = productCodeCounts.GetValueOrDefault(candidateCode) + 1;
            }
        }

        for (var i = 0; i < dataRows.Count; i++)
        {
            var sourceRowNumber = i + 2; // +1 for header, +1 for 1-based row numbering
            var fields = dataRows[i];
            if (fields.Length != Header.Count)
            {
                staged.Add(BuildMalformedRow(sourceRowNumber, fields, keys));
                continue;
            }

            var productKey = ImportFieldNormalization.NormalizeKey(fields[0]);
            var productCode = ImportFieldNormalization.NormalizeCode(fields[1]);
            var nameZhTw = ImportFieldNormalization.NormalizeText(fields[2]);
            var brandCode = ImportFieldNormalization.NormalizeCode(fields[3]);
            var categoryCode = ImportFieldNormalization.NormalizeCode(fields[4]);
            var descriptionZhTw = ImportFieldNormalization.NormalizeText(fields[5]);
            var hasWarranty = ImportFieldNormalization.TryParseInt32(fields[6], out var warrantyMonths);
            var status = ImportFieldNormalization.NormalizeKey(fields[7]);

            var payload = new ProductPayload(
                productKey ?? $"__row{sourceRowNumber}",
                productCode,
                nameZhTw,
                brandCode,
                categoryCode,
                descriptionZhTw,
                hasWarranty ? warrantyMonths : null,
                status);

            var row = new StagedImportRow<ProductPayload>
            {
                SourceRowNumber = sourceRowNumber,
                ImportKey = productKey ?? keys.Allocate("row", sourceRowNumber),
                OriginalKey = productKey,
                Payload = payload,
                RawFields = fields,
            };

            if (productKey is null || productKey.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (!seenKeys.Add(productKey))
            {
                // 組長 PR #74 round-3, item 1：重複列雖然標了錯誤，儲存鍵仍是那個重複的 key，於是
                // 之後以 ImportKey 建 Dictionary 會丟 ArgumentException、寫 ImportRows 會撞
                // (BatchId, Dataset, ImportKey) 唯一索引——整包直接 500，管理員連錯誤檔都下載不到。
                // 改用「不會衝突的儲存鍵」保存這一列；原始 offending key 仍留在 payload 裡，錯誤
                // CSV 由 payload 取值輸出（見 EfProductImportService.GetErrorsCsvAsync）。
                row.ImportKey = keys.Allocate("dup", sourceRowNumber);
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (productCode is null || productCode.Length > 64)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (productCodeCounts.GetValueOrDefault(productCode) > 1)
            {
                // 組長 PR #74 round-3, item 2：不同 product_key 指向同一個 product_code——新商品要等
                // Confirm 才撞 UX_Products_ProductCode，既有商品則會被兩列連續更新成 last-row-wins，
                // 而 Preview 還顯示 Ready。同一批內重複的 product_code 一律是列級錯誤。
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (nameZhTw is null || nameZhTw.Length > 160)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (brandCode is null || brandCode.Length > 40)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (categoryCode is null || categoryCode.Length > 40)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (descriptionZhTw is { Length: > 4000 })
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (fields[6] != "\\N" && !hasWarranty)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (hasWarranty && warrantyMonths is < 0 or > 120)
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            if (status is null || !Enum.TryParse<ProductImportStatus>(status, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }

            staged.Add(row);
        }

        return staged;
    }

    private static StagedImportRow<ProductPayload> BuildMalformedRow(
        int sourceRowNumber, string[] fields, ImportStorageKeyAllocator keys)
    {
        var storageKey = keys.Allocate("row", sourceRowNumber);
        var row = new StagedImportRow<ProductPayload>
        {
            SourceRowNumber = sourceRowNumber,
            ImportKey = storageKey,
            OriginalKey = null,
            Payload = new ProductPayload(storageKey, null, null, null, null, null, null, null),
            RawFields = fields,
        };
        row.AddError(DomainErrorCodes.ImportValidationFailed);
        return row;
    }
}

/// <summary>The three status values a product import row may set — a strict subset of ProductStatus (excludes Discontinued, which the import contract does not offer).</summary>
internal enum ProductImportStatus
{
    Draft,
    Published,
    Unpublished,
}
