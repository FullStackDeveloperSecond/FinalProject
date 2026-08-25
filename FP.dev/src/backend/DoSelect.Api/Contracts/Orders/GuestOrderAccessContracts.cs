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
