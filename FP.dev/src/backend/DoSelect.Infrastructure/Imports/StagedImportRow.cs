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
    public required string ImportKey { get; init; }
    public required TPayload Payload { get; init; }
    public required string[] RawFields { get; init; }
    public List<string> Errors { get; } = [];
    public ImportRowAction Action { get; set; } = ImportRowAction.Error;

    public void AddError(string code) => Errors.Add(code);
}
