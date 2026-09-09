using System.Data;
using DoSelect.Application.Checkout;
using DoSelect.Application.Common;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Checkout;

public sealed class EfGuestCheckoutEmailVerificationGateway(
    DoSelectDbContext context,
    IOutboxWriter outboxWriter) : IGuestCheckoutEmailVerificationGateway
{
    private const int DeadlockVictimErrorNumber = 1205;

    public async Task<bool> TryCreateAsync(
        GuestCheckoutVerificationRateLimitWindow window,
        GuestCheckoutEmailVerification verification,
        OutboxWriteRequest notification,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var ipCount = await context.GuestCheckoutEmailVerifications.CountAsync(
                item => item.RequesterIpHash == window.IpHash &&
                    item.CreatedAtUtc > window.WindowStartUtc,
                cancellationToken);
            var emailCount = await context.GuestCheckoutEmailVerifications.CountAsync(
                item => item.EmailHash == window.EmailHash &&
                    item.CreatedAtUtc > window.WindowStartUtc,
                cancellationToken);
            var cartCount = await context.GuestCheckoutEmailVerifications.CountAsync(
                item => item.GuestCartKeyHash == window.GuestCartKeyHash &&
                    item.CreatedAtUtc > window.WindowStartUtc,
                cancellationToken);
            if (ipCount >= window.IpPermitLimit || emailCount >= window.EmailPermitLimit ||
                cartCount >= window.GuestCartPermitLimit)
            {
                return false;
            }

            context.GuestCheckoutEmailVerifications.Add(verification);
            outboxWriter.Add(notification);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsRetryableConflict(exception))
        {
            context.ChangeTracker.Clear();
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The guest Checkout email verification was interrupted by a concurrent request.")
                .WithInnerException(exception);
        }
    }

    public Task<GuestCheckoutEmailVerification?> FindActiveAsync(
        Guid requestPublicId,
        byte[] guestCartKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        context.GuestCheckoutEmailVerifications.SingleOrDefaultAsync(
            item => item.PublicId == requestPublicId &&
                item.GuestCartKeyHash == guestCartKeyHash &&
                item.ExpiresAtUtc > nowUtc &&
                item.VerifiedAtUtc == null &&
                item.ConsumedAtUtc == null &&
                item.LockedAtUtc == null &&
                item.RevokedAtUtc == null &&
                item.AttemptCount < GuestCheckoutEmailVerification.MaximumAttempts,
            cancellationToken);

    public Task<GuestCheckoutEmailVerification?> FindByProofAsync(
        byte[] proofTokenHash,
        byte[] guestCartKeyHash,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        context.GuestCheckoutEmailVerifications.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProofTokenHash == proofTokenHash &&
                item.GuestCartKeyHash == guestCartKeyHash &&
                item.ExpiresAtUtc > nowUtc &&
                item.VerifiedAtUtc != null &&
                item.ConsumedAtUtc == null &&
                item.LockedAtUtc == null &&
                item.RevokedAtUtc == null,
            cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw DomainProblemException.Conflict(
                DomainErrorCodes.ConcurrencyConflict,
                "The guest Checkout email verification changed concurrently.")
                .WithInnerException(exception);
        }
    }

    private static bool IsRetryableConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException ||
        exception is SqlException { Number: DeadlockVictimErrorNumber } ||
        exception.InnerException is SqlException { Number: DeadlockVictimErrorNumber };
}
