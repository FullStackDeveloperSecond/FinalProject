using System.Text;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberProfileGateway(DoSelectDbContext dbContext, TimeProvider timeProvider)
    : IMemberProfileGateway
{
    public async Task<MemberProfileDto?> GetProfileAsync(
        string memberUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(memberUserId, cancellationToken);
        return record is null ? null : ToDto(record.Value.User, record.Value.Profile);
    }

    public async Task<UpdateMemberProfileOutcome> UpdateProfileAsync(
        string memberUserId,
        UpdateMemberProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = await LoadAsync(memberUserId, cancellationToken, forUpdate: true);
        if (record is null)
        {
            // A missing profile for an authenticated member is not a normal concurrency race;
            // treating it the same as a stale RowVersion keeps the caller's error handling to one
            // branch and avoids leaking which specific condition occurred.
            return new UpdateMemberProfileOutcome.ConcurrencyConflict();
        }

        var (user, profile) = record.Value;

        // rowVersion 涵蓋整個可修改聚合，不是只有 MemberProfile 自己那 8 bytes——Phone／
        // Locale 存在 ApplicationUser，該實體的 ConcurrencyStamp 也是 EF Concurrency Token
        // （見 Migration Snapshot 的 IsConcurrencyToken）。只檢查 Profile 自己的 RowVersion，
        // 會讓「畫面讀取之後、送出之前，Phone／Locale 被別的流程改過」這種情境悄悄被目前這次
        // 更新蓋掉（Alex review，2026-08-28）：兩個實體的併發權杖都要用呼叫端帶入的舊值覆蓋
        // OriginalValue，讓 EF 的 SaveChanges 一次性偵測到任一邊過期。
        if (!TryDecomposeRowVersion(command.RowVersion, out var profileRowVersion, out var concurrencyStamp))
        {
            return new UpdateMemberProfileOutcome.ConcurrencyConflict();
        }

        dbContext.Entry(profile).Property(candidate => candidate.RowVersion).OriginalValue = profileRowVersion;
        dbContext.Entry(user).Property(candidate => candidate.ConcurrencyStamp).OriginalValue = concurrencyStamp;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        profile.UpdateProfile(command.DisplayName, profile.BirthDate, nowUtc);
        user.ChangePreferredLocale(command.Locale, nowUtc);
        user.PhoneNumber = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();
        // ChangePreferredLocale / PhoneNumber 的變更本身不會自動輪替 ConcurrencyStamp（不是走
        // UserManager），這裡手動轉一個新值，讓下一次讀取拿到的 rowVersion 反映這次更新。
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new UpdateMemberProfileOutcome.ConcurrencyConflict();
        }

        return new UpdateMemberProfileOutcome.Success(ToDto(user, profile));
    }

    public async Task<IReadOnlyList<MemberAddressDto>> ListAddressesAsync(
        string memberUserId,
        CancellationToken cancellationToken = default)
    {
        var addresses = await dbContext.MemberAddresses.AsNoTracking()
            .Where(address => address.MemberUserId == memberUserId && address.DeletedAtUtc == null)
            .OrderByDescending(address => address.IsDefault)
            .ThenByDescending(address => address.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return addresses.Select(ToDto).ToList();
    }

    public async Task<MemberAddressWriteOutcome> CreateAddressAsync(
        string memberUserId,
        MemberAddressInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var address = new MemberAddress(
            Guid.CreateVersion7(),
            memberUserId,
            input.Label,
            input.RecipientName,
            input.Phone,
            input.PostalCode,
            input.City,
            input.District,
            input.AddressLine1,
            input.AddressLine2,
            nowUtc);

        if (input.IsDefault)
        {
            await ClearExistingDefaultAsync(memberUserId, excludePublicId: null, nowUtc, cancellationToken);
            address.SetAsDefault(nowUtc);
        }

        dbContext.MemberAddresses.Add(address);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDefaultAddressUniqueIndexViolation(exception))
        {
            // 兩個併發請求各自把不同地址設成預設：都通過了 ClearExistingDefaultAsync（當下都還
            // 沒看到對方尚未提交的變更），但只有一個 INSERT 真的能拿到過濾唯一索引
            // （Alex review，2026-08-28）。輸家回可重試的衝突，不是未處理的 500。
            return new MemberAddressWriteOutcome.ConcurrencyConflict();
        }

        return new MemberAddressWriteOutcome.Success(ToDto(address));
    }

    public async Task<MemberAddressWriteOutcome> UpdateAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        UpdateMemberAddressCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var address = await dbContext.MemberAddresses.FirstOrDefaultAsync(
            candidate => candidate.PublicId == addressPublicId &&
                         candidate.MemberUserId == memberUserId &&
                         candidate.DeletedAtUtc == null,
            cancellationToken);
        if (address is null)
        {
            return new MemberAddressWriteOutcome.NotFound();
        }

        dbContext.Entry(address).Property(candidate => candidate.RowVersion).OriginalValue = command.RowVersion;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var input = command.Input;
        address.Update(
            input.Label,
            input.RecipientName,
            input.Phone,
            input.PostalCode,
            input.City,
            input.District,
            input.AddressLine1,
            input.AddressLine2,
            nowUtc);

        if (input.IsDefault)
        {
            await ClearExistingDefaultAsync(memberUserId, addressPublicId, nowUtc, cancellationToken);
            address.SetAsDefault(nowUtc);
        }
        else
        {
            address.ClearDefault(nowUtc);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new MemberAddressWriteOutcome.ConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (IsDefaultAddressUniqueIndexViolation(exception))
        {
            return new MemberAddressWriteOutcome.ConcurrencyConflict();
        }

        return new MemberAddressWriteOutcome.Success(ToDto(address));
    }

    public async Task<MemberAddressWriteOutcome> DeleteAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        var address = await dbContext.MemberAddresses.FirstOrDefaultAsync(
            candidate => candidate.PublicId == addressPublicId && candidate.MemberUserId == memberUserId,
            cancellationToken);
        if (address is null)
        {
            return new MemberAddressWriteOutcome.NotFound();
        }

        if (address.DeletedAtUtc is null)
        {
            dbContext.Entry(address).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
            address.Delete(timeProvider.GetUtcNow().UtcDateTime);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // 例如刪除當下有另一個請求剛更新過這筆地址——RowVersion 已經跟呼叫端帶入的
                // 不一致，讓呼叫端重新讀取後決定是否仍要刪除（Alex review，2026-08-28）。
                return new MemberAddressWriteOutcome.ConcurrencyConflict();
            }
        }

        return new MemberAddressWriteOutcome.Success(ToDto(address));
    }

    private async Task ClearExistingDefaultAsync(
        string memberUserId,
        Guid? excludePublicId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existingDefaults = await dbContext.MemberAddresses
            .Where(candidate =>
                candidate.MemberUserId == memberUserId &&
                candidate.DeletedAtUtc == null &&
                candidate.IsDefault &&
                candidate.PublicId != excludePublicId)
            .ToListAsync(cancellationToken);

        foreach (var existing in existingDefaults)
        {
            existing.ClearDefault(nowUtc);
        }
    }

    /// <summary>
    /// MemberAddresses 只有一個唯一索引（UX_MemberAddresses_MemberUserId_Default），所以這裡不
    /// 比對索引名稱字串——本機 SQL Server 用繁中定位環境時，錯誤訊息裡索引名稱前後夾雜的中文字
    /// 在某些邊界情況下會讓單純的子字串比對（見 SqlUniqueIndexViolations.Matches）漏判，在高併發
    /// 測試中偶爾讓真正的唯一索引衝突被誤判成未處理例外（Alex review，2026-08-28 後續觀察）。
    /// 只要是這張表的重複索引鍵錯誤（2601／2627），就當作預設地址搶到同一個名額處理。
    /// </summary>
    private static bool IsDefaultAddressUniqueIndexViolation(DbUpdateException exception)
    {
        const int DuplicateKeyOnUniqueIndex = 2601;
        const int DuplicateKeyOnPrimaryOrUniqueConstraint = 2627;

        for (var current = (Exception)exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException &&
                sqlException.Errors.Cast<SqlError>().Any(error =>
                    error.Number is DuplicateKeyOnUniqueIndex or DuplicateKeyOnPrimaryOrUniqueConstraint))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(ApplicationUser User, MemberProfile Profile)?> LoadAsync(
        string memberUserId,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var usersQuery = forUpdate ? dbContext.Users : dbContext.Users.AsNoTracking();
        var profilesQuery = forUpdate ? dbContext.MemberProfiles : dbContext.MemberProfiles.AsNoTracking();

        var user = await usersQuery.SingleOrDefaultAsync(
            candidate => candidate.Id == memberUserId, cancellationToken);
        var profile = await profilesQuery.SingleOrDefaultAsync(
            candidate => candidate.UserId == memberUserId, cancellationToken);

        return user is null || profile is null ? null : (user, profile);
    }

    private static MemberProfileDto ToDto(ApplicationUser user, MemberProfile profile) => new(
        profile.PublicId,
        profile.DisplayName,
        EmailMasking.Mask(user.Email!),
        user.EmailConfirmed,
        user.PhoneNumber,
        user.PreferredLocale,
        profile.CreatedAtUtc,
        ComposeRowVersion(profile.RowVersion, user.ConcurrencyStamp));

    /// <summary>rowVersion 涵蓋 MemberProfile.RowVersion（固定 8 bytes，SQL Server rowversion）
    /// ++ ApplicationUser.ConcurrencyStamp（Identity 的字串併發權杖，Phone／Locale 所在實體）
    /// ——單一 rowVersion 欄位保護整個可修改聚合，不只是 Profile 自己的欄位。</summary>
    private static byte[] ComposeRowVersion(byte[] profileRowVersion, string? concurrencyStamp)
    {
        var stampBytes = Encoding.UTF8.GetBytes(concurrencyStamp ?? string.Empty);
        var combined = new byte[profileRowVersion.Length + stampBytes.Length];
        Buffer.BlockCopy(profileRowVersion, 0, combined, 0, profileRowVersion.Length);
        Buffer.BlockCopy(stampBytes, 0, combined, profileRowVersion.Length, stampBytes.Length);
        return combined;
    }

    private static bool TryDecomposeRowVersion(
        byte[] rowVersion, out byte[] profileRowVersion, out string concurrencyStamp)
    {
        const int ProfileRowVersionLength = 8;
        if (rowVersion.Length < ProfileRowVersionLength)
        {
            profileRowVersion = [];
            concurrencyStamp = string.Empty;
            return false;
        }

        profileRowVersion = rowVersion[..ProfileRowVersionLength];
        concurrencyStamp = Encoding.UTF8.GetString(rowVersion[ProfileRowVersionLength..]);
        return true;
    }

    private static MemberAddressDto ToDto(MemberAddress address) => new(
        address.PublicId,
        address.Label,
        address.RecipientName,
        address.Phone,
        address.PostalCode,
        address.City,
        address.District,
        address.AddressLine1,
        address.AddressLine2,
        address.IsDefault,
        address.CreatedAtUtc,
        address.UpdatedAtUtc,
        address.RowVersion);
}
