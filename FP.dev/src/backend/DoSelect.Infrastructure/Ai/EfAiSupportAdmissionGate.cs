using System.Data;
using System.Data.Common;
using DoSelect.Application.Ai;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiSupportAdmissionGate(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<OpenAiResponsesOptions>? options = null) : IAiSupportAdmissionGate
{
    public const int DailySupportLimit = 20;

    private static readonly TimeZoneInfo TaipeiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public async Task<AiSupportAccessState> ReadAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var window = ResolveWindow(timeProvider.GetUtcNow());
        try
        {
            var memberUserId = ToMemberUserId(memberId);
            var consentState = await ReadConsentStateAsync(
                memberUserId,
                lockForReservation: false,
                cancellationToken);
            var used = consentState == AiConsentState.Granted
                ? await CountUsedAsync(memberUserId, window, cancellationToken)
                : 0;
            return await CreateStateAsync(
                memberUserId,
                consentState,
                used,
                window.ResetAtUtc,
                cancellationToken);
        }
        catch (DbException)
        {
            return Unavailable(window.ResetAtUtc);
        }
        catch (InvalidOperationException exception) when (IsDatabaseFailure(exception))
        {
            return Unavailable(window.ResetAtUtc);
        }
    }

    public async Task<AiSupportReservationResult> TryReserveAsync(
        Guid memberId,
        Guid requestPublicId,
        CancellationToken cancellationToken)
    {
        if (requestPublicId == Guid.Empty)
        {
            throw new ArgumentException("RequestPublicId is required.", nameof(requestPublicId));
        }

        var now = timeProvider.GetUtcNow();
        var window = ResolveWindow(now);
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var memberUserId = ToMemberUserId(memberId);

            // Lock the member's consent-key range first. Every reservation for the same member
            // takes this lock in the same order, serializing the quota count + append operation.
            var consentState = await ReadConsentStateAsync(
                memberUserId,
                lockForReservation: true,
                cancellationToken);
            if (consentState != AiConsentState.Granted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(
                    IsReserved: false,
                    new AiSupportAccessState(
                        consentState,
                        DailySupportLimit,
                        window.ResetAtUtc));
            }

            var existing = await dbContext.AiUsageLedger
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.RequestPublicId == requestPublicId,
                    cancellationToken);
            var used = await CountUsedAsync(memberUserId, window, cancellationToken);
            var accessState = await CreateStateAsync(
                memberUserId,
                consentState,
                used,
                window.ResetAtUtc,
                cancellationToken);
            if (accessState.ConsentState != AiConsentState.Granted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(IsReserved: false, accessState);
            }

            if (accessState.BudgetProtectionActive && !accessState.IsDemoAllowlisted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(IsReserved: false, accessState);
            }

            if (existing is not null)
            {
                var isSameReservation =
                    existing.MemberUserId == memberUserId &&
                    existing.Feature == AiUsageFeature.Support &&
                    existing.Succeeded;
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(
                    isSameReservation,
                    accessState);
            }

            if (used >= DailySupportLimit)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(
                    IsReserved: false,
                    accessState);
            }

            dbContext.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                memberUserId,
                requestPublicId,
                now.UtcDateTime));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AiSupportReservationResult(
                IsReserved: true,
                accessState with
                {
                    RemainingDailyMessages = Math.Max(0, DailySupportLimit - used - 1),
                });
        }
        catch (DbException)
        {
            dbContext.ChangeTracker.Clear();
            return new AiSupportReservationResult(
                IsReserved: false,
                Unavailable(window.ResetAtUtc));
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return new AiSupportReservationResult(
                IsReserved: false,
                Unavailable(window.ResetAtUtc));
        }
        catch (InvalidOperationException exception) when (IsDatabaseFailure(exception))
        {
            dbContext.ChangeTracker.Clear();
            return new AiSupportReservationResult(
                IsReserved: false,
                Unavailable(window.ResetAtUtc));
        }
    }

    private async Task<AiConsentState> ReadConsentStateAsync(
        string memberUserId,
        bool lockForReservation,
        CancellationToken cancellationToken)
    {
        IQueryable<AiConsentRecord> query = dbContext.AiConsentRecords.AsNoTracking();
        if (lockForReservation)
        {
            query = dbContext.AiConsentRecords
                .FromSqlInterpolated(
                    $"SELECT * FROM [AiConsentRecords] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberUserId] = {memberUserId}")
                .AsNoTracking();
        }

        var latest = await query
            .Where(record =>
                record.MemberUserId == memberUserId &&
                record.Purpose == AiConsentPurpose.Support &&
                record.PolicyVersion == AiConsentPolicy.CurrentVersion)
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return latest?.Status switch
        {
            AiConsentRecordStatus.Granted when latest.WithdrawnAtUtc is null =>
                AiConsentState.Granted,
            AiConsentRecordStatus.Withdrawn => AiConsentState.Denied,
            _ => AiConsentState.Missing,
        };
    }

    private Task<int> CountUsedAsync(
        string memberUserId,
        QuotaWindow window,
        CancellationToken cancellationToken) =>
        dbContext.AiUsageLedger
            .AsNoTracking()
            .CountAsync(
                entry => entry.MemberUserId == memberUserId &&
                    entry.Feature == AiUsageFeature.Support &&
                    entry.Succeeded &&
                    entry.OccurredAtUtc >= window.StartsAtUtc &&
                    entry.OccurredAtUtc < window.ResetAtUtc.UtcDateTime,
                cancellationToken);

    private async Task<AiSupportAccessState> CreateStateAsync(
        string memberUserId,
        AiConsentState consentState,
        int used,
        DateTimeOffset resetAtUtc,
        CancellationToken cancellationToken)
    {
        if (options is not null &&
            !await HasValidBudgetAlertRecipientAsync(
                options.Value.BudgetAlertRecipientAdminPublicId,
                cancellationToken))
        {
            return Unavailable(resetAtUtc);
        }

        var cumulativeCost = await dbContext.AiInteractions
            .AsNoTracking()
            .SumAsync(
                interaction => (decimal?)interaction.EstimatedCostUsd,
                cancellationToken) ?? 0m;
        var demoIds = options?.Value.DemoMemberPublicIds ?? [];
        var isDemoAllowlisted = demoIds.Length > 0 && await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == memberUserId && demoIds.Contains(user.PublicId),
                cancellationToken);
        return new AiSupportAccessState(
            consentState,
            Math.Max(0, DailySupportLimit - used),
            resetAtUtc,
            cumulativeCost >= 90m,
            isDemoAllowlisted);
    }

    private async Task<bool> HasValidBudgetAlertRecipientAsync(
        Guid? recipientPublicId,
        CancellationToken cancellationToken)
    {
        if (!recipientPublicId.HasValue || recipientPublicId.Value == Guid.Empty)
        {
            return false;
        }

        return await (
            from user in dbContext.Users.AsNoTracking()
            join profile in dbContext.AdminProfiles.AsNoTracking()
                on user.Id equals profile.UserId
            join userRole in dbContext.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where user.PublicId == recipientPublicId.Value &&
                user.AccountType == AccountType.Admin &&
                user.AccountStatus == AccountStatus.Active &&
                profile.IsActive &&
                role.Name == AuditRoleNames.SuperAdmin
            select user.Id)
            .AnyAsync(cancellationToken);
    }

    private static AiSupportAccessState Unavailable(DateTimeOffset resetAtUtc) =>
        new(AiConsentState.Unavailable, RemainingDailyMessages: 0, resetAtUtc);

    private static bool IsDatabaseFailure(InvalidOperationException exception) =>
        exception.InnerException is DbException;

    private static string ToMemberUserId(Guid memberId) =>
        memberId != Guid.Empty
            ? memberId.ToString("D")
            : throw new ArgumentException("A member identifier is required.", nameof(memberId));

    private static QuotaWindow ResolveWindow(DateTimeOffset now)
    {
        var taipeiNow = TimeZoneInfo.ConvertTime(now, TaipeiTimeZone);
        var startsAtTaipei = DateTime.SpecifyKind(
            taipeiNow.Date,
            DateTimeKind.Unspecified);
        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(startsAtTaipei, TaipeiTimeZone);
        var resetAtUtc = TimeZoneInfo.ConvertTimeToUtc(
            startsAtTaipei.AddDays(1),
            TaipeiTimeZone);
        return new QuotaWindow(
            startsAtUtc,
            new DateTimeOffset(resetAtUtc, TimeSpan.Zero));
    }

    private sealed record QuotaWindow(DateTime StartsAtUtc, DateTimeOffset ResetAtUtc);
}
