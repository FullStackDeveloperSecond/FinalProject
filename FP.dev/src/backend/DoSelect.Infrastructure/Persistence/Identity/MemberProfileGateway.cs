using DoSelect.Application.Members;
using DoSelect.Domain.Members;
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
        dbContext.Entry(profile).Property(candidate => candidate.RowVersion).OriginalValue = command.RowVersion;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        profile.UpdateProfile(command.DisplayName, profile.BirthDate, nowUtc);
        user.ChangePreferredLocale(command.Locale, nowUtc);
        user.PhoneNumber = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();

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

    public async Task<MemberAddressDto> CreateAddressAsync(
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(address);
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

        return new MemberAddressWriteOutcome.Success(ToDto(address));
    }

    public async Task<MemberAddressWriteOutcome> DeleteAddressAsync(
        string memberUserId,
        Guid addressPublicId,
        CancellationToken cancellationToken = default)
    {
        var address = await dbContext.MemberAddresses.FirstOrDefaultAsync(
            candidate => candidate.PublicId == addressPublicId && candidate.MemberUserId == memberUserId,
            cancellationToken);
        if (address is null)
        {
            return new MemberAddressWriteOutcome.NotFound();
        }

        if (address.DeletedAtUtc is null)
        {
            address.Delete(timeProvider.GetUtcNow().UtcDateTime);
            await dbContext.SaveChangesAsync(cancellationToken);
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
        profile.RowVersion);

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
