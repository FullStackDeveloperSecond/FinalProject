using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Common;
using DoSelect.Application.Common.Cursors;
using DoSelect.Application.Imports;
using DoSelect.Domain.Imports;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// 商品匯入與庫存匯入之間真正共通的機制。兩種匯入的資料集、解析與套用規則完全不同，但「批次」這
/// 一層是一樣的：讀檔上限、CSV 解析的錯誤映射、暫存列的統計與 32 KB 信封、逾期批次收尾、錯誤 CSV
/// 與 Cursor 分頁。
///
/// 抽出來是因為 A-13 庫存匯入若各寫一份，兩邊會慢慢分岔——而分岔的那一份不會有人發現，直到某個
/// 管理員遇到只在其中一種匯入才會出現的行為。組長在 #74 為商品匯入逐輪修正過的那些邊界
/// （超長鍵、32 KB 列、逾期批次擋住重傳）沒有理由在庫存匯入重踩一次。
/// </summary>
internal static class ImportBatchStaging
{
    public const int MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>ImportRow 的兩個 JSON 欄位各有 32 KB 上限。</summary>
    public const int MaxRowJsonLength = 32 * 1024;

    /// <summary>暫存列存進 NormalizedPayloadJson 的信封：payload 加上 Preview 當時的 RowVersion。</summary>
    public sealed record RowEnvelope<TPayload>(TPayload Payload, byte[]? PreimageRowVersion);

    private sealed record OversizedRowEnvelope(object? Payload, byte[]? PreimageRowVersion, string? OriginalKey);

    public static async Task<byte[]> ReadFileAsync(
        IncomingImportFile file,
        string datasetLabel,
        string importLabel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.HasFile)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportDatasetMissing,
                $"The {datasetLabel} file is required for {importLabel}.");
        }

        if (file.DeclaredLength is > MaxFileSizeBytes)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file exceeds the 10 MB limit.");
        }

        await using var stream = file.OpenRead();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        // 宣告長度可以說謊，所以讀完之後照實再量一次。
        if (buffer.Length > MaxFileSizeBytes)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file exceeds the 10 MB limit.");
        }

        return buffer.ToArray();
    }

    public static IReadOnlyList<StagedImportRow<TPayload>> ParseCsv<TPayload>(
        byte[] content,
        Func<IReadOnlyList<string[]>, IReadOnlyList<StagedImportRow<TPayload>>> parse,
        string datasetLabel)
    {
        IReadOnlyList<string[]> rows;
        try
        {
            rows = DelimitedTextReader.Parse(content);
        }
        catch (FormatException exception)
        {
            throw DomainProblemException.BadRequest(
                DomainErrorCodes.ImportFormatUnsupported,
                $"The {datasetLabel} file is not valid CSV: {exception.Message}");
        }

        return ParseRows(rows, parse);
    }

    /// <summary>
    /// CSV 與 XLSX 在這裡會合：不管來源是逗號分隔文字還是工作表，進了 Parser 都是同一種
    /// <c>string[]</c> 列。兩種格式的對等就是靠這一個入口保證的。
    /// </summary>
    public static IReadOnlyList<StagedImportRow<TPayload>> ParseRows<TPayload>(
        IReadOnlyList<string[]> rows,
        Func<IReadOnlyList<string[]>, IReadOnlyList<StagedImportRow<TPayload>>> parse)
    {
        try
        {
            return parse(rows);
        }
        catch (ImportBatchParseException exception)
        {
            throw DomainProblemException.BadRequest(exception.ErrorCode, exception.Message);
        }
    }

    /// <summary>XLSX：讀出固定名稱的工作表，錯誤映射與 CSV 一致（穩定錯誤碼，不是 500）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string[]>> ReadWorkbookSheets(
        byte[] content,
        IReadOnlyList<string> sheetNames)
    {
        try
        {
            return XlsxWorkbookReader.ReadSheets(content, sheetNames);
        }
        catch (ImportBatchParseException exception)
        {
            throw DomainProblemException.BadRequest(exception.ErrorCode, exception.Message);
        }
    }

    /// <summary>
    /// 組長 PR #74 review item 4：唯一索引只擋「進行中」的批次，但過期的 Ready 批次以前只有在有人
    /// 呼叫它自己的 Confirm 時才會翻成 Expired——於是管理員重傳一直撞 import_batch_in_progress。
    /// 暫存新批次之前先把這位管理員同型別、已過 24 小時的批次收掉。
    /// </summary>
    public static async Task ExpireStaleBatchesAsync(
        DoSelectDbContext dbContext,
        string createdByAdminUserId,
        ImportType importType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stale = await dbContext.ImportBatches
            .Where(candidate => candidate.CreatedByAdminUserId == createdByAdminUserId &&
                candidate.ImportType == importType &&
                candidate.ExpiresAtUtc <= now &&
                (candidate.Status == ImportBatchStatus.Uploaded ||
                 candidate.Status == ImportBatchStatus.Validating ||
                 candidate.Status == ImportBatchStatus.Ready ||
                 candidate.Status == ImportBatchStatus.Committing))
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var batch in stale)
        {
            batch.ChangeStatus(ImportBatchStatus.Expired, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 把暫存列寫進 ImportRows 並累加統計。
    ///
    /// 組長 PR #74 round-3 item 4：兩個 JSON 欄位各有 32 KB 上限，超過會從建構子丟例外——一個 40 KB
    /// 的無效欄位讓整批直接 500，管理員連錯誤檔都拿不到。所以在建立實體「之前」量測序列化後的大小：
    /// 超限的列不保存巨量內容（RawJson 省略，payload 換成不含資料的最小信封），但仍然是一列帶錯誤碼
    /// 的資料，批次照常成為 Invalid 讓管理員修檔重傳。
    /// </summary>
    public static ImportRowCounts AddRows<TPayload>(
        DoSelectDbContext dbContext,
        long batchId,
        ImportDataset dataset,
        IReadOnlyList<StagedImportRow<TPayload>> rows,
        ImportRowCounts counts,
        DateTime now)
    {
        foreach (var row in rows)
        {
            var action = row.Errors.Count > 0 ? ImportRowAction.Error : row.Action;
            counts = counts.Add(action);

            var normalizedPayloadJson = JsonSerializer.Serialize(
                new RowEnvelope<TPayload>(row.Payload, row.PreimageRowVersion));
            var rawJson = JsonSerializer.Serialize(row.RawFields);
            var errorCodes = row.Errors.Count > 0 ? string.Join(",", row.Errors.Distinct()) : null;

            if (normalizedPayloadJson.Length > MaxRowJsonLength)
            {
                normalizedPayloadJson = BuildOversizedPayloadJson(row.OriginalKey);
                if (errorCodes is null)
                {
                    errorCodes = DomainErrorCodes.ImportValidationFailed;
                    counts = counts.Remove(action).Add(ImportRowAction.Error);
                    action = ImportRowAction.Error;
                }
            }

            if (rawJson.Length > MaxRowJsonLength)
            {
                rawJson = null;
            }

            dbContext.ImportRows.Add(new ImportRow(
                batchId,
                dataset,
                row.SourceRowNumber,
                row.ImportKey,
                action,
                normalizedPayloadJson,
                errorCodes,
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPayloadJson)),
                rawJson,
                now));
        }

        return counts;
    }

    /// <summary>
    /// 組長 PR #74 round-4 review (P3)：超過 32 KB 的列會丟掉整個 payload，若那列同時是 duplicate，
    /// 錯誤 CSV 就只剩合成鍵可顯示——與「顯示管理員原始鍵」的契約不符。最小信封仍保留原始鍵。
    /// </summary>
    public static string BuildOversizedPayloadJson(string? originalKey)
    {
        var boundedKey = originalKey is { Length: > ImportStorageKeyAllocator.MaxKeyLength }
            ? originalKey[..ImportStorageKeyAllocator.MaxKeyLength] + "...(truncated)"
            : originalKey;
        var json = JsonSerializer.Serialize(new OversizedRowEnvelope(null, null, boundedKey));

        // 保留的鍵本身也可能長到讓最小信封再次超限；那就連鍵都放棄，至少這一列存得進去。
        return json.Length <= MaxRowJsonLength
            ? json
            : JsonSerializer.Serialize(new OversizedRowEnvelope(null, null, null));
    }

    private sealed record RowCursorPayload(ImportDataset Dataset, int SourceRowNumber);

    /// <summary>
    /// 預覽列的 Cursor 分頁。兩種匯入的差別只有「哪些 dataset 名稱算合法」，其餘（游標指紋、
    /// 只看錯誤列、排序鍵、多取一筆判斷還有沒有下一頁）完全相同。
    ///
    /// 游標帶指紋：換了篩選條件卻沿用舊游標，會拿到一份對不上自己條件的資料——直接拒絕，要求
    /// 從第一頁重新開始。
    /// </summary>
    public static Task<CursorPage<ImportRowDto>> GetRowsAsync(
        DoSelectDbContext dbContext,
        long batchId,
        Guid batchPublicId,
        ImportRowsQuery query,
        IReadOnlyList<ImportDataset> allowedDatasets,
        CancellationToken cancellationToken) =>
        GetRowsAsync(dbContext, batchId, batchPublicId, query, allowedDatasets, ToImportRowDto, cancellationToken);

    /// <summary>
    /// 同一段分頁，換一個投影：庫存匯入要回明確型別的預覽列（Before／Delta／After），商品匯入回
    /// 通用的 <see cref="ImportRowDto"/>。游標、篩選與排序不因 DTO 不同而分岔。
    /// </summary>
    public static async Task<CursorPage<TDto>> GetRowsAsync<TDto>(
        DoSelectDbContext dbContext,
        long batchId,
        Guid batchPublicId,
        ImportRowsQuery query,
        IReadOnlyList<ImportDataset> allowedDatasets,
        Func<ImportRow, TDto> project,
        CancellationToken cancellationToken)
    {
        var pageSize = query.PageSize;
        var fingerprint = OpaqueCursorCodec.ComputeFingerprint(
            batchPublicId.ToString(), query.Dataset, query.ErrorsOnly.ToString());

        var rowsQuery = dbContext.ImportRows.AsNoTracking()
            .Where(row => row.ImportBatchId == batchId);

        if (!string.IsNullOrWhiteSpace(query.Dataset))
        {
            if (!Enum.TryParse<ImportDataset>(query.Dataset, ignoreCase: true, out var dataset) ||
                !allowedDatasets.Contains(dataset))
            {
                throw DomainProblemException.Validation(
                    $"Unknown dataset '{query.Dataset}'. Valid values: {string.Join(", ", allowedDatasets)}.");
            }

            rowsQuery = rowsQuery.Where(row => row.Dataset == dataset);
        }

        if (query.ErrorsOnly)
        {
            rowsQuery = rowsQuery.Where(row => row.ErrorCodes != null);
        }

        if (!string.IsNullOrWhiteSpace(query.Cursor) &&
            !OpaqueCursorCodec.TryDecode<RowCursorPayload>(query.Cursor, fingerprint, out _))
        {
            throw DomainProblemException.Validation(
                "The cursor is invalid or was issued under different filters. Restart from the first page.");
        }

        if (OpaqueCursorCodec.TryDecode<RowCursorPayload>(query.Cursor, fingerprint, out var after) && after is not null)
        {
            var afterDataset = after.Dataset;
            var afterSourceRowNumber = after.SourceRowNumber;
            rowsQuery = rowsQuery.Where(row =>
                row.Dataset > afterDataset ||
                (row.Dataset == afterDataset && row.SourceRowNumber > afterSourceRowNumber));
        }

        var page = await rowsQuery
            .OrderBy(row => row.Dataset).ThenBy(row => row.SourceRowNumber)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).Select(project).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = page[pageSize - 1];
            nextCursor = OpaqueCursorCodec.Encode(new RowCursorPayload(last.Dataset, last.SourceRowNumber), fingerprint);
        }

        return new CursorPage<TDto>(items, nextCursor, hasMore);
    }

    public static IReadOnlyList<string> SplitErrorCodes(string? errorCodes) =>
        string.IsNullOrEmpty(errorCodes) ? [] : errorCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static ImportRowDto ToImportRowDto(ImportRow row) => new(
        row.Dataset.ToString(),
        row.SourceRowNumber,
        row.ImportKey,
        row.Action.ToString(),
        SplitErrorCodes(row.ErrorCodes),
        UnwrapPayloadJson(row.NormalizedPayloadJson));

    public static string UnwrapPayloadJson(string normalizedPayloadJson)
    {
        using var document = JsonDocument.Parse(normalizedPayloadJson);
        return document.RootElement.TryGetProperty("Payload", out var payload)
            ? payload.GetRawText()
            : normalizedPayloadJson;
    }

    /// <summary>
    /// 錯誤 CSV。被判為重複的列是用合成鍵儲存的，所以顯示的是管理員在自己檔案裡看得懂的原始鍵
    /// （組長 PR #74 round-3 item 1）。
    /// </summary>
    public static byte[] BuildErrorsCsv(IReadOnlyList<ImportRow> errorRows)
    {
        var header = new[] { "dataset", "source_row_number", "import_key", "error_codes" };
        var rows = errorRows.Select(row => (IReadOnlyList<string>)new[]
        {
            row.Dataset.ToString(),
            row.SourceRowNumber.ToString(CultureInfo.InvariantCulture),
            OriginalKeyOf(row),
            row.ErrorCodes ?? string.Empty,
        });

        return DelimitedTextWriter.Write(header, rows);
    }

    /// <summary>
    /// 屬性名是 PascalCase：序列化用的是 JsonSerializer 的預設設定，所以信封的鍵就是 C# 的屬性
    /// 名稱。這一段是從 EfProductImportService 原樣搬過來的，只多加庫存那一組——改寫成別的大小寫
    /// 會讓錯誤 CSV 安靜地退回合成鍵，而那正是組長 round-3 item 1 要求修掉的行為。
    /// </summary>
    public static string OriginalKeyOf(ImportRow row)
    {
        try
        {
            using var document = JsonDocument.Parse(row.NormalizedPayloadJson);

            // 超限的列只留下了鍵（round-4, P3）。
            if (ReadString(document.RootElement, "OriginalKey") is { } preservedKey)
            {
                return preservedKey;
            }

            if (!document.RootElement.TryGetProperty("Payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return row.ImportKey;
            }

            return row.Dataset switch
            {
                ImportDataset.Products => ReadString(payload, "ProductKey") ?? row.ImportKey,
                ImportDataset.Skus => ReadString(payload, "SkuKey") ?? row.ImportKey,
                ImportDataset.Specifications =>
                    ReadString(payload, "SkuKey") is { } skuKey
                        ? $"{skuKey}/{ReadString(payload, "SemanticKey") ?? string.Empty}"
                        : row.ImportKey,
                // 庫存調整的業務鍵就是 SKU Code。
                ImportDataset.InventoryAdjustments => ReadString(payload, "SkuCode") ?? row.ImportKey,
                _ => row.ImportKey,
            };
        }
        catch (JsonException)
        {
            return row.ImportKey;
        }
    }

    private static string? ReadString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Preview 的四個統計數字。用具名型別而不是四個 int 參數傳來傳去。</summary>
internal readonly record struct ImportRowCounts(int New, int Updated, int Unchanged, int Errors)
{
    public ImportRowCounts Add(ImportRowAction action) => action switch
    {
        ImportRowAction.Insert => this with { New = New + 1 },
        ImportRowAction.Update => this with { Updated = Updated + 1 },
        ImportRowAction.NoChange => this with { Unchanged = Unchanged + 1 },
        _ => this with { Errors = Errors + 1 },
    };

    public ImportRowCounts Remove(ImportRowAction action) => action switch
    {
        ImportRowAction.Insert => this with { New = New - 1 },
        ImportRowAction.Update => this with { Updated = Updated - 1 },
        ImportRowAction.NoChange => this with { Unchanged = Unchanged - 1 },
        _ => this with { Errors = Errors - 1 },
    };
}
