using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DoSelect.Api.Contracts.Auth;
using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;

namespace DoSelect.Api.Contracts.Members;

public sealed record MemberProfileResponse(
    Guid PublicId,
    string DisplayName,
    string EmailMasked,
    bool EmailVerified,
    string? Phone,
    string Locale,
    DateTime CreatedAtUtc,
    byte[] RowVersion)
{
    public static MemberProfileResponse From(MemberProfileDto dto) => new(
        dto.PublicId,
        dto.DisplayName,
        dto.EmailMasked,
        dto.EmailVerified,
        dto.Phone,
        LocaleTokens.ToToken(dto.Locale),
        dto.CreatedAtUtc,
        dto.RowVersion);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateMemberProfileRequest
{
    private string _displayName = string.Empty;
    private string? _phone;
    private string _locale = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName
    {
        get => _displayName;
        init => _displayName = InputNormalization.Canonicalize(value);
    }

    [StringLength(32, MinimumLength = 6)]
    public string? Phone
    {
        get => _phone;
        init => _phone = value is null ? null : InputNormalization.Canonicalize(value);
    }

    [Required]
    public string Locale
    {
        get => _locale;
        init => _locale = InputNormalization.Canonicalize(value);
    }

    [Required]
    public byte[] RowVersion { get; init; } = [];

    public UpdateMemberProfileCommand ToCommand()
    {
        if (!LocaleTokens.TryFromToken(Locale, out var locale))
        {
            throw new ValidationException($"'{Locale}' is not a supported locale.");
        }

        return new UpdateMemberProfileCommand(DisplayName, Phone, locale, RowVersion);
    }
}

public sealed record MemberAddressResponse(
    Guid PublicId,
    string Label,
    string RecipientName,
    string Phone,
    string PostalCode,
    string City,
    string District,
    string AddressLine1,
    string? AddressLine2,
    bool IsDefault,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion)
{
    public static MemberAddressResponse From(MemberAddressDto dto) => new(
        dto.PublicId,
        dto.Label,
        dto.RecipientName,
        dto.Phone,
        dto.PostalCode,
        dto.City,
        dto.District,
        dto.AddressLine1,
        dto.AddressLine2,
        dto.IsDefault,
        dto.CreatedAtUtc,
        dto.UpdatedAtUtc,
        dto.RowVersion);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateMemberAddressRequest
{
    private string _label = string.Empty;
    private string _recipientName = string.Empty;
    private string _phone = string.Empty;
    private string _postalCode = string.Empty;
    private string _city = string.Empty;
    private string _district = string.Empty;
    private string _addressLine1 = string.Empty;
    private string? _addressLine2;

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Label
    {
        get => _label;
        init => _label = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string RecipientName
    {
        get => _recipientName;
        init => _recipientName = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(32, MinimumLength = 6)]
    public string Phone
    {
        get => _phone;
        init => _phone = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(16, MinimumLength = 1)]
    public string PostalCode
    {
        get => _postalCode;
        init => _postalCode = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string City
    {
        get => _city;
        init => _city = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string District
    {
        get => _district;
        init => _district = InputNormalization.Canonicalize(value);
    }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string AddressLine1
    {
        get => _addressLine1;
        init => _addressLine1 = InputNormalization.Canonicalize(value);
    }

    [StringLength(300)]
    public string? AddressLine2
    {
        get => _addressLine2;
        init => _addressLine2 = value is null ? null : InputNormalization.Canonicalize(value);
    }

    public bool IsDefault { get; init; }

    public MemberAddressInput ToInput() => new(
        Label, RecipientName, Phone, PostalCode, City, District, AddressLine1, AddressLine2, IsDefault);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateMemberAddressRequest : CreateMemberAddressRequest
{
    [Required]
    public byte[] RowVersion { get; init; } = [];

    public UpdateMemberAddressCommand ToCommand() => new(ToInput(), RowVersion);
}

/// <summary>比照 CartController.RemoveItem 的既有慣例——DELETE 帶 RowVersion 於 Body（Alex
/// review，2026-08-28：刪除地址前也要驗證併發，不能無條件覆蓋）。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DeleteMemberAddressRequest
{
    [Required]
    public byte[] RowVersion { get; init; } = [];
}
