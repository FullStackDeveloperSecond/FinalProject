namespace DoSelect.Application.Common;

/// <summary>
/// Opaque-cursor pagination for fast-moving or large row-by-row data (庫存保留列表 and the other
/// documented exceptions in API共通規範.md). The cursor is bound to the caller's filter, sort, and
/// authorization scope; it never promises TotalCount/TotalPages.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
