using DoSelect.Application.Common;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

/// <summary>
/// ⚠ 新範圍：後台會員管理沒有既有 API/DTO 規格，此檔案是依 AdminOrder 系列
/// 清單+詳細頁模式新設計的契約，PR／日誌中標註待 alex 覆核。
/// </summary>
public sealed record AdminMemberQuery(
    string? Search,
    AccountStatus? Status,
    DateOnly? RegisteredFrom,
    DateOnly? RegisteredTo,
    int PageNumber,
    int PageSize);

public sealed record AdminMemberRow(
    Guid PublicId,
    string DisplayName,
    string Email,
    DateTime RegisteredAtUtc,
    AccountStatus AccountStatus);

public sealed record AdminMemberListStats(int TotalMembers, int NewTodayCount, int ActiveCount);

public sealed record AdminMemberListResult(PageResult<AdminMemberRow> Members, AdminMemberListStats Stats);

public sealed record AdminMemberOrderRow(
    Guid OrderPublicId, string OrderNumber, DateTime PlacedAtUtc, string OrderStatus, decimal GrandTotal);

public sealed record AdminMemberActivityEvent(DateTime OccurredAtUtc, string EventType, string Description);

/// <summary>
/// ⚠ 即時運算，沒有任何資料庫欄位儲存這些數字（不新增資料表）。口徑：
/// TotalSpend 只計已完成訂單；TotalOrderCount 排除已取消訂單；
/// ReturnRatePercent = 已完成退貨的相異訂單數 ÷ TotalOrderCount。
/// </summary>
public sealed record AdminMemberStats(decimal TotalSpend, int TotalOrderCount, decimal ReturnRatePercent);

/// <summary>
/// ⚠ 資料庫沒有性別、會員等級欄位，也沒有業務規則可換算——刻意不放進這個 Snapshot。
/// 電話取自預設收件地址（MemberAddress），沒有就是 null。
/// </summary>
public sealed record AdminMemberDetailSnapshot(
    Guid PublicId,
    string DisplayName,
    string Email,
    string? Phone,
    DateOnly? BirthDate,
    DateTime RegisteredAtUtc,
    AccountStatus AccountStatus,
    byte[] RowVersion,
    AdminMemberStats Stats,
    IReadOnlyList<AdminMemberOrderRow> RecentOrders,
    IReadOnlyList<AdminMemberActivityEvent> ActivityLog);

public interface IAdminMemberQueryReader
{
    Task<AdminMemberListResult> ListAsync(
        AdminMemberQuery query, CancellationToken cancellationToken = default);

    Task<AdminMemberDetailSnapshot?> FindDetailAsync(
        Guid publicId, CancellationToken cancellationToken = default);
}

public sealed record AdminMemberWriteResult(bool Succeeded, string? ErrorCode)
{
    public static AdminMemberWriteResult Success() => new(true, null);

    public static AdminMemberWriteResult Failure(string errorCode) => new(false, errorCode);
}

public interface IAdminMemberWriter
{
    Task<AdminMemberWriteResult> UpdateProfileAsync(
        Guid publicId,
        string displayName,
        DateOnly? birthDate,
        byte[] rowVersion,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// suspend=true 對應 ApplicationUser.Suspend；false 對應 Reactivate。停用時一併
    /// bump SecurityStamp——目前 Member Cookie 尚未接 SecurityStamp 驗證（Admin Cookie
    /// 才有，見 SecurityServiceCollectionExtensions），這裡先種欄位一致性，
    /// 不在本次範圍內補 Member Cookie 驗證。
    /// </summary>
    Task<AdminMemberWriteResult> SetAccountStatusAsync(
        Guid publicId,
        bool suspend,
        byte[] rowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 密碼重設一律走既有的忘記密碼 Email 流程（UserManager 產生 Token + 既有 IEmailSender），
/// 不由管理員直接設定明文密碼。
/// </summary>
public interface IAdminMemberPasswordResetInitiator
{
    Task<bool> SendResetPasswordEmailAsync(
        Guid publicId, CancellationToken cancellationToken = default);
}
