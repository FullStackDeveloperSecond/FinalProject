using System.Data;
using System.Data.Common;
using DoSelect.Application.Ai;
using DoSelect.Domain.Ai;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiSupportAdmissionGate(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider) : IAiSupportAdmissionGate
{
    public const int DailySupportLimit = 20;

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
            return CreateState(consentState, used, window.ResetAtUtc);
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
                    CreateState(consentState, used: 0, window.ResetAtUtc));
            }

            var existing = await dbContext.AiUsageLedger
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.RequestPublicId == requestPublicId,
                    cancellationToken);
            var used = await CountUsedAsync(memberUserId, window, cancellationToken);
            if (existing is not null)
            {
                var isSameReservation =
                    existing.MemberUserId == memberUserId &&
                    existing.Feature == AiUsageFeature.Support &&
                    existing.Succeeded;
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(
                    isSameReservation,
                    CreateState(consentState, used, window.ResetAtUtc));
            }

            if (used >= DailySupportLimit)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AiSupportReservationResult(
                    IsReserved: false,
                    CreateState(consentState, used, window.ResetAtUtc));
            }

            dbContext.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                memberUserId,
                requestPublicId,
                now.UtcDateTime));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AiSupportReservationResult(
                IsReserved: true,
                CreateState(consentState, used + 1, window.ResetAtUtc));
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
            .Where(record => record.MemberUserId == memberUserId)
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

    private static AiSupportAccessState CreateState(
        AiConsentState consentState,
        int used,
        DateTimeOffset resetAtUtc) =>
        new(
            consentState,
            Math.Max(0, DailySupportLimit - used),
            resetAtUtc);

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
        var utc = now.ToUniversalTime();
        var startsAtUtc = new DateTime(
            utc.Year,
            utc.Month,
            utc.Day,
            0,
            0,
            0,
            DateTimeKind.Utc);
        return new QuotaWindow(
            startsAtUtc,
            new DateTimeOffset(startsAtUtc.AddDays(1), TimeSpan.Zero));
    }

    private sealed record QuotaWindow(DateTime StartsAtUtc, DateTimeOffset ResetAtUtc);
}
