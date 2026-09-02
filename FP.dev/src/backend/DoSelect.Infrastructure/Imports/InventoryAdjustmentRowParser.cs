using DoSelect.Application.Common;
using DoSelect.Domain.Imports;
using DoSelect.Domain.Inventory;

namespace DoSelect.Infrastructure.Imports;

/// <summary>庫存調整模板一列的正規化結果（ImportRow.NormalizedPayloadJson 的形狀）。</summary>
internal sealed record InventoryAdjustmentPayload(
    string SkuCode,
    int? TargetOnHand,
    string? ReasonCode,
    string? Note);

/// <summary>
/// 匯入暫存與庫存調整設計.md「Inventory Adjustments」欄位契約。標題列固定為
/// sku_code、target_on_hand、reason_code、note，順序與大小寫都不容偏差（ImportHeaderValidator）。
/// </summary>
internal static class InventoryAdjustmentRowParser
{
    public static readonly IReadOnlyList<string> Header =
        ["sku_code", "target_on_hand", "reason_code", "note"];

    private const int MaxSkuCodeLength = 64;
    private const int MaxNoteLength = 500;

    public static IReadOnlyList<StagedImportRow<InventoryAdjustmentPayload>> Parse(
        IReadOnlyList<string[]> rows)
    {
        var dataRows = ImportHeaderValidator.ValidateAndGetDataRows(rows, Header, "InventoryAdjustments");
        var staged = new List<StagedImportRow<InventoryAdjustmentPayload>>(dataRows.Count);

        // 同一個 SKU 在一個批次裡出現兩次是列級錯誤：兩列各自算出來的 Delta 都是對著同一個
        // Before 算的，一起套用會得到誰也沒要求的結果。比對用資料庫的比較規則（NFKC＋大寫），
        // 與 ImportStorageKeyAllocator 一致——否則 `SK1` 與全形 `ＳＫ１` 在這裡看似不同，
        // 到了資料庫的 CI_AS 定序卻是同一個。
        var canonicalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fields in dataRows)
        {
            var code = ImportFieldNormalization.NormalizeCode(FieldAt(fields, 0));
            if (code is not null)
            {
                var canonical = ImportStorageKeyAllocator.Canonicalize(code);
                canonicalCounts[canonical] = canonicalCounts.GetValueOrDefault(canonical) + 1;
            }
        }

        // 配置器先登記整個資料集實際用到的鍵，合成鍵才不可能遮蔽到某一列真正的 SKU Code
        // （組長 PR #74 round-4／5／6 在商品匯入那邊踩過的同一組問題，這裡沿用同一個機制而不是
        // 自己維護一個 HashSet）。
        var keys = new ImportStorageKeyAllocator();
        foreach (var fields in dataRows)
        {
            keys.Reserve(ImportFieldNormalization.NormalizeCode(FieldAt(fields, 0)));
        }

        var canonicalKeysInUse = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dataRows.Count; index++)
        {
            var fields = dataRows[index];
            var sourceRowNumber = index + 2; // 標題列是第 1 列。
            staged.Add(ParseRow(fields, sourceRowNumber, canonicalCounts, keys, canonicalKeysInUse));
        }

        return staged;
    }

    private static StagedImportRow<InventoryAdjustmentPayload> ParseRow(
        string[] fields,
        int sourceRowNumber,
        IReadOnlyDictionary<string, int> canonicalCounts,
        ImportStorageKeyAllocator keys,
        HashSet<string> canonicalKeysInUse)
    {
        var errors = new List<string>();

        var skuCode = ImportFieldNormalization.NormalizeCode(FieldAt(fields, 0));
        if (skuCode is null)
        {
            errors.Add(DomainErrorCodes.ImportValidationFailed);
        }
        else if (skuCode.Length > MaxSkuCodeLength)
        {
            errors.Add(DomainErrorCodes.ImportValidationFailed);
            skuCode = skuCode[..MaxSkuCodeLength];
        }

        int? targetOnHand = null;
        var rawTarget = FieldAt(fields, 1);
        if (ImportFieldNormalization.TryParseInt32(rawTarget, out var parsedTarget) && parsedTarget >= 0)
        {
            targetOnHand = parsedTarget;
        }
        else
        {
            // 契約是 0～2,147,483,647：負數與非整數都是列級錯誤，不是「當成 0」。
            errors.Add(DomainErrorCodes.ImportValidationFailed);
        }

        var reasonCode = ImportFieldNormalization.RawOrNull(FieldAt(fields, 2))?.Trim();
        if (reasonCode is null ||
            !InventoryAdjustmentReasonCodes.All.Contains(reasonCode, StringComparer.Ordinal))
        {
            errors.Add(DomainErrorCodes.ImportValidationFailed);
            reasonCode = null;
        }

        var note = ImportFieldNormalization.NormalizeText(FieldAt(fields, 3));
        if (note is { Length: > MaxNoteLength })
        {
            errors.Add(DomainErrorCodes.ImportValidationFailed);
            note = note[..MaxNoteLength];
        }

        // Other 必填說明——沒有說明的 Other 等於沒有原因。
        if (reasonCode is not null &&
            InventoryAdjustmentReasonCodes.RequiresNote(reasonCode) &&
            string.IsNullOrWhiteSpace(note))
        {
            errors.Add(DomainErrorCodes.ImportValidationFailed);
        }

        if (skuCode is not null &&
            canonicalCounts.GetValueOrDefault(ImportStorageKeyAllocator.Canonicalize(skuCode)) > 1)
        {
            errors.Add(DomainErrorCodes.ImportSkuCodeDuplicate);
        }

        var storageKey = AllocateStorageKey(skuCode, sourceRowNumber, keys, canonicalKeysInUse);
        var staged = new StagedImportRow<InventoryAdjustmentPayload>
        {
            SourceRowNumber = sourceRowNumber,
            ImportKey = storageKey,
            OriginalKey = skuCode,
            Payload = new InventoryAdjustmentPayload(skuCode ?? string.Empty, targetOnHand, reasonCode, note),
            RawFields = fields,
        };
        foreach (var error in errors)
        {
            staged.AddError(error);
        }

        return staged;
    }

    /// <summary>
    /// ImportRow 的儲存鍵在 batch+dataset 內唯一，所以重複的 SKU Code（或空的）要換成一個以列號
    /// 為基礎的合成鍵，整批才存得進去讓管理員下載錯誤檔。原始鍵永遠留在 OriginalKey 與 Payload。
    /// 形狀與 ProductRowParser／SkuRowParser 的處理一致。
    /// </summary>
    private static string AllocateStorageKey(
        string? skuCode,
        int sourceRowNumber,
        ImportStorageKeyAllocator keys,
        HashSet<string> canonicalKeysInUse)
    {
        // 第一次出現的合法鍵照原樣存；重複的、缺的、超長的都換成配置器給的合成鍵。
        if (ImportStorageKeyAllocator.CanStore(skuCode) &&
            canonicalKeysInUse.Add(ImportStorageKeyAllocator.Canonicalize(skuCode!)))
        {
            return skuCode!;
        }

        return keys.Allocate("ROW", sourceRowNumber);
    }

    private static string FieldAt(string[] fields, int index) =>
        index < fields.Length ? fields[index] : string.Empty;
}
