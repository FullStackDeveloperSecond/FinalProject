using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Members;

public sealed class AdminMemberWriter : IAdminMemberWriter
{
    private readonly DoSelectDbContext _dbContext;
    private readonly UserManager<Persistence.Identity.ApplicationUser> _userManager;

    public AdminMemberWriter(
        DoSelectDbContext dbContext, UserManager<Persistence.Identity.ApplicationUser> userManager)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userManager);

        _dbContext = dbContext;
        _userManager = userManager;
    }

    public async Task<AdminMemberWriteResult> UpdateProfileAsync(
        Guid publicId,
        string displayName,
        DateOnly? birthDate,
        byte[] rowVersion,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.PublicId == publicId && u.AccountType == AccountType.Member, cancellationToken);
        if (user is null)
        {
            return AdminMemberWriteResult.Failure(AdminMemberErrorCodes.MemberNotFound);
        }

        var profile = await _dbContext.MemberProfiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (profile is null)
        {
            return AdminMemberWriteResult.Failure(AdminMemberErrorCodes.MemberNotFound);
        }

        _dbContext.Entry(profile).Property(p => p.RowVersion).OriginalValue = rowVersion;
        profile.UpdateProfile(displayName, birthDate, updatedAtUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminMemberWriteResult.Failure(AdminMemberErrorCodes.ConcurrencyConflict);
        }

        return AdminMemberWriteResult.Success();
    }

    public async Task<AdminMemberWriteResult> SetAccountStatusAsync(
        Guid publicId,
        bool suspend,
        byte[] rowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.PublicId == publicId && u.AccountType == AccountType.Member, cancellationToken);
        if (user is null)
        {
            return AdminMemberWriteResult.Failure(AdminMemberErrorCodes.MemberNotFound);
        }

        _dbContext.Entry(user).Property(u => u.RowVersion).OriginalValue = rowVersion;

        if (suspend)
        {
            user.Suspend(occurredAtUtc);
        }
        else
        {
            user.Reactivate(occurredAtUtc);
        }

        try
        {
            // 停用會員時撤銷其既有 Session——bump SecurityStamp。UserManager 的 EF Store
            // 會就地呼叫 SaveChangesAsync，因此連同上面的 Suspend／Reactivate 一起存，
            // RowVersion 衝突會在這裡就丟出，用同一個 try/catch 接住。
            // Member Cookie 目前還沒接 SecurityStamp 驗證（Admin Cookie 才有），
            // 這裡先種欄位一致性，本次不補 Member 端驗證。
            await _userManager.UpdateSecurityStampAsync(user);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AdminMemberWriteResult.Failure(AdminMemberErrorCodes.ConcurrencyConflict);
        }

        return AdminMemberWriteResult.Success();
    }
}
