using DoSelect.Domain.Members;

namespace DoSelect.Application.Members;

public sealed record MemberProfileDto(
    Guid PublicId,
    string DisplayName,
    string EmailMasked,
    bool EmailVerified,
    string? Phone,
    SupportedLocale Locale,
    DateTime CreatedAtUtc,
    byte[] RowVersion);

public sealed record UpdateMemberProfileCommand(
    string DisplayName,
    string? Phone,
    SupportedLocale Locale,
    byte[] RowVersion);

public sealed record MemberAddressDto(
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
    byte[] RowVersion);

public sealed record MemberAddressInput(
    string Label,
    string RecipientName,
    string Phone,
    string PostalCode,
    string City,
    string District,
    string AddressLine1,
    string? AddressLine2,
    bool IsDefault);

public sealed record UpdateMemberAddressCommand(MemberAddressInput Input, byte[] RowVersion);

public abstract record UpdateMemberProfileOutcome
{
    public sealed record Success(MemberProfileDto Dto) : UpdateMemberProfileOutcome;

    public sealed record ConcurrencyConflict : UpdateMemberProfileOutcome;
}

public abstract record MemberAddressWriteOutcome
{
    public sealed record Success(MemberAddressDto Dto) : MemberAddressWriteOutcome;

    public sealed record NotFound : MemberAddressWriteOutcome;

    public sealed record ConcurrencyConflict : MemberAddressWriteOutcome;
}

/// <summary>
/// M 會員資料／收件地址支撐（API Endpoint目錄.md）。Profile 的 Email／EmailVerified 來自
/// ApplicationUser（Identity），DisplayName／BirthDate 來自 MemberProfile；Phone／Locale
/// 也存在 ApplicationUser 上（既有的 PhoneNumber／PreferredLocale 欄位，非 MemberProfile 新增
/// 欄位）——這裡只讀寫既有欄位，沒有新增 Migration。RowVersion 一律用 MemberProfile 自己的
/// （UpdateMemberProfileRequest 只有單一 rowVersion 欄位，ApplicationUser 這邊的併發改用
/// Identity 內建 ConcurrencyStamp，不對外暴露第二個 RowVersion）；這是本次判斷，待 alex 確認。
/// </summary>
public interface IMemberProfileGateway
{
    Task<MemberProfileDto?> GetProfileAsync(string memberUserId, CancellationToken cancellationToken = default);

    Task<UpdateMemberProfileOutcome> UpdateProfileAsync(
        string memberUserId,
        UpdateMemberProfileCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberAddressDto>> ListAddressesAsync(
        string memberUserId,
        CancellationToken cancellationToken = default);

    Task<MemberAddressDto> CreateAddressAsync(
        string memberUserId,
        MemberAddressInput input,
        CancellationToken cancellationToken = default);

    Task<MemberAddressWriteOutcome> UpdateAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        UpdateMemberAddressCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>軟刪除（MemberAddress.Delete）——歷史訂單快照已經各自保存收件資訊，不依賴這筆
    /// 地址簿列存活（API Endpoint目錄.md：「刪除不改歷史訂單快照」）。冪等：刪除已刪除的地址
    /// 視同成功，不視為 NotFound。</summary>
    Task<MemberAddressWriteOutcome> DeleteAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        CancellationToken cancellationToken = default);
}
