namespace DoSelect.Application.Common;

/// <summary>
/// The response shape for the small set of endpoints that use cursor pagination instead of
/// pageNumber/pageSize (API共通規範's Cursor 例外 list). NextCursor is null once HasMore is
/// false; callers must not infer a total count from this shape.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
