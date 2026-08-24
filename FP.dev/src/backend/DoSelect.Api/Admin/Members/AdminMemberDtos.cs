using System.ComponentModel.DataAnnotations;
using DoSelect.Application.Common;

namespace DoSelect.Api.Admin.Members;

/// <summary>⚠ 新範圍：後台會員管理沒有既有 DTO 規格，命名比照既有 AdminXxxQuery 慣例。</summary>
public sealed record AdminMemberListRequestDto(
    string? Search,
    string? Status,
    DateOnly? RegisteredFrom,
    DateOnly? RegisteredTo,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record AdminMemberSummaryDto(
    Guid PublicId, string DisplayName, string Email, DateTime RegisteredAtUtc, string AccountStatus);

public sealed record AdminMemberListStatsDto(int TotalMembers, int NewTodayCount, int ActiveCount);

public sealed record AdminMemberListResponseDto(
    PageResult<AdminMemberSummaryDto> Members, AdminMemberListStatsDto Stats);

/// <summary>⚠ 即時運算，非資料庫儲存欄位——參見 AdminMemberStats 的口徑註解。</summary>
public sealed record AdminMemberStatsDto(decimal TotalSpend, int TotalOrderCount, decimal ReturnRatePercent);

public sealed record AdminMemberOrderSummaryDto(
    Guid OrderPublicId, string OrderNumber, DateTime PlacedAtUtc, string OrderStatus, decimal GrandTotal);

public sealed record AdminMemberActivityEventDto(DateTime OccurredAtUtc, string EventType, string Description);

/// <summary>
/// ⚠ 刻意沒有 Gender／MembershipTier 欄位：資料庫沒有對應欄位，也沒有業務規則可換算，
/// 不是漏做。Phone 沒有預設收件地址時為 null，前端顯示「未提供」。
/// </summary>
public sealed record AdminMemberDetailDto(
    Guid PublicId,
    string DisplayName,
    string Email,
    string? Phone,
    DateOnly? BirthDate,
    DateTime RegisteredAtUtc,
    string AccountStatus,
    byte[] RowVersion,
    AdminMemberStatsDto Stats,
    IReadOnlyList<AdminMemberOrderSummaryDto> RecentOrders,
    IReadOnlyList<AdminMemberActivityEventDto> ActivityLog);

public sealed record UpdateAdminMemberProfileRequest(
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    DateOnly? BirthDate,
    byte[] RowVersion);

/// <summary>⚠ PENDING ALEX POLICY REVIEW：Member.ManageSensitive。</summary>
public sealed record SetMemberAccountStatusRequest(bool Suspend, byte[] RowVersion);
