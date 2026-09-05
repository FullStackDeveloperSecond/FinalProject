using DoSelect.Domain.Imports;

namespace DoSelect.Infrastructure.Imports;

/// <summary>
/// One parsed-and-validated (but not yet DB-resolved) row awaiting cross-reference/diff
/// resolution against the database. <see cref="Errors"/> accumulates every problem found for
/// this row rather than stopping at the first — 匯入暫存與庫存調整設計.md's Preview is meant
/// to let an admin fix a whole file in one round trip, not one error at a time.
/// </summary>
internal sealed class StagedImportRow<TPayload>
{
    public required int SourceRowNumber { get; init; }

    /// <summary>
    /// The key this row is STORED under (unique within batch+dataset). Normally the row's own
    /// business key; for a row rejected as a duplicate it is swapped for a row-scoped synthetic
    /// key so the invalid batch can still be persisted and downloaded (組長 PR #74 round-3,
    /// item 1). The original offending key always remains in <see cref="Payload"/>.
    /// </summary>
    public required string ImportKey { get; set; }

    /// <summary>
    /// The business key exactly as the admin wrote it (already normalized), independent of the
    /// storage key. 組長 PR #74 round-4 review (P3)：超過 32 KB 的列會丟掉整個 payload，若那列同時
    /// 是 duplicate，錯誤 CSV 就只剩合成鍵可顯示——與「顯示管理員原始鍵」的契約不符。這個欄位讓
    /// 最小化的信封仍能保留（本身有長度上限的）原始鍵。Null 代表該列根本沒有可用的鍵。
    /// </summary>
    public required string? OriginalKey { get; init; }
    public required TPayload Payload { get; init; }
    public required string[] RawFields { get; init; }
    public List<string> Errors { get; } = [];
    public ImportRowAction Action { get; set; } = ImportRowAction.Error;

    /// <summary>
    /// The referenced existing entity's RowVersion as of Preview, for Update／NoChange rows
    /// (組長 PR #74 review item 2). Confirm feeds this to EF as the concurrency original value on
    /// every Update write, so any third-party modification between Preview and Confirm — even one
    /// racing the confirm transaction itself — fails the write at the database rather than being
    /// silently overwritten. Null for Insert rows and rows that resolved no existing entity.
    /// </summary>
    public byte[]? PreimageRowVersion { get; set; }

    public void AddError(string code) => Errors.Add(code);
}
