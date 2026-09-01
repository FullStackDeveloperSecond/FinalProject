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

        // 組長 PR #74 round-6 review (裁定 A1)：兩個 business key 之間的碰撞也要用資料庫的比較規則
        // 判斷。`PK1` 與全形 `ＰＫ１` 在 Ordinal 下是兩個 key，SQL Server 的 width-insensitive
        // collation 卻視為同一個，兩列都用原值當 ImportKey 就會撞
        // UX_ImportRows_ImportBatchId_Dataset_ImportKey，Preview 變成 500。canonical key 的計數同時
        // 解決兩件事：衝突集合裡的「每一列」都要標錯（錯誤 CSV 才給得出完整集合），而第二列起改用
        // 合成儲存鍵。
        var keyConflictCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var canonicalKeysInUse = new HashSet<string>(StringComparer.Ordinal);

        // 組長 PR #74 round-4 review (P2／P3)：先掃一遍再判。
        //  - 儲存鍵配置器要先知道整個資料集實際用到哪些 key，合成鍵才不會用資料庫的比較規則撞上
        //    使用者自己的 key（例如 __DUP4）。
        //  - 重複的 product_code 必須「每一列」都標錯，不能只標第二筆以後：錯誤 CSV 要能指出完整
        //    的衝突集合，管理員才知道兩列都要改。
        var keys = new ImportStorageKeyAllocator();
        var productCodeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
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
                // 組長 PR #74 round-5 review (P2)：缺 key 與超長 key 都不能直接當儲存鍵——
                // 後者會死在 ImportKey 的 64 字元欄位限制上，讓整批 Preview 變成 500。
                ImportKey = ImportStorageKeyAllocator.CanStore(productKey)
                    ? productKey!
                    : keys.Allocate("row", sourceRowNumber),
                OriginalKey = productKey,
                Payload = payload,
                RawFields = fields,
            };

            if (!ImportStorageKeyAllocator.CanStore(productKey))
            {
                row.AddError(DomainErrorCodes.ImportValidationFailed);
            }
            else if (keyConflictCounts.GetValueOrDefault(ImportStorageKeyAllocator.Canonicalize(productKey!)) > 1)
            {
                // 組長 PR #74 round-3 item 1／round-6 裁定 A1：衝突集合中的每一列都是列級錯誤，
                // 第一列可以保留原本的儲存鍵，第二列起改用不會衝突的合成鍵——否則之後以 ImportKey
                // 建 Dictionary 會丟 ArgumentException、寫 ImportRows 會撞唯一索引，整包直接 500，
                // 管理員連錯誤檔都下載不到。原始 offending key 仍留在 payload 與 OriginalKey 裡，
                // 錯誤 CSV 由那裡取值輸出（見 EfProductImportService.GetErrorsCsvAsync）。
                if (!canonicalKeysInUse.Add(ImportStorageKeyAllocator.Canonicalize(productKey!)))
                {
                    row.ImportKey = keys.Allocate("dup", sourceRowNumber);
                }

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
