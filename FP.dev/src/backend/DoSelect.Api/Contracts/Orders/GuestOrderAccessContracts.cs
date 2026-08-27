using System.ComponentModel.DataAnnotations;

namespace DoSelect.Api.Contracts.Orders;

public sealed class GuestOrderAccessRequestDto
{
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string OrderNumber { get; init; } = string.Empty;

    [Required]
    [StringLength(320, MinimumLength = 3)]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// 有效／無效輸入回相同 Schema——不表示訂單或 Email 是否存在（API DTO與Schema契約.md:64）。
/// <c>actions/resend</c> 原地更新同一張 Challenge 的碼與寄送狀態，因此
/// <see cref="RequestPublicId"/> 維持等於 URL 中的值；持久化限流由同交易的獨立事件 Row 計數。
/// </summary>
public sealed record GuestOrderAccessRequestAcceptedDto(
    Guid RequestPublicId,
    DateTime ExpiresAtUtc,
    DateTime ResendAvailableAtUtc);

public sealed class GuestOrderAccessVerificationDto
{
    [Required]
    public Guid RequestPublicId { get; init; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public sealed record GuestOrderAccessVerifiedDto(Guid OrderPublicId, DateTime ExpiresAtUtc);
