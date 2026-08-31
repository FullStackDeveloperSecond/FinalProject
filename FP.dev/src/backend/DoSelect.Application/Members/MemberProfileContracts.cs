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
/// 欄位）——這裡只讀寫既有欄位，沒有新增 Migration。RowVersion 涵蓋整個可修改聚合：對外的單一
/// rowVersion 欄位是 MemberProfile.RowVersion 與 ApplicationUser.ConcurrencyStamp 的複合值
/// （見 MemberProfileGateway 的 ComposeRowVersion／TryDecomposeRowVersion），確保 Phone／
/// Locale（存在 ApplicationUser）被別的流程改過時，也能被偵測為併發衝突，不會被舊畫面覆寫
/// （Alex review，2026-08-28）。
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

    /// <summary>回傳 ConcurrencyConflict（而非丟未處理例外）——併發建立兩筆各自不同的
    /// 「設為預設」地址會撞 UX_MemberAddresses_MemberUserId_Default 這個過濾唯一索引，跟一般
    /// RowVersion 樂觀併發衝突是同一類使用者可重試的情境（Alex review，2026-08-28）。</summary>
    Task<MemberAddressWriteOutcome> CreateAddressAsync(
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
    /// 視同成功，不視為 NotFound。rowVersion 用於偵測刪除跟另一個併發更新互相衝突的情況
    /// （Alex review，2026-08-28）。</summary>
    Task<MemberAddressWriteOutcome> DeleteAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        byte[] rowVersion,
        CancellationToken cancellationToken = default);
}
