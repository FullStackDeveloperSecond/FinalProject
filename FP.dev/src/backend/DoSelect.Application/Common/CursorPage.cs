namespace DoSelect.Application.Common;

/// <summary>
/// The response shape for the small set of endpoints that use cursor pagination instead of
/// pageNumber/pageSize (API共通規範's Cursor 例外 list, e.g. 後台訂單與庫存保留列表). The cursor is
/// bound to the caller's filter, sort, and authorization scope; NextCursor is null once HasMore is
/// false, and callers must not infer a total count from this shape. First used by
/// EfAdminOrderService; later Cursor 分頁 consumers (terry/kafen 的後台訂單、庫存保留、SLA
/// 佇列等模組) should reuse this type rather than defining their own.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
