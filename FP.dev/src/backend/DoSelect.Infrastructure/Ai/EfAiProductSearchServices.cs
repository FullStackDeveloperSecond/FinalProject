using System.Data;
using System.Data.Common;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Application.Auditing;
using DoSelect.Application.Notifications;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiProductSearchAdmissionGate(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<OpenAiResponsesOptions> options) : IAiProductSearchAdmissionGate
{
    public const int AnonymousDailyLimit = 10;
    public const int MemberDailyLimit = 30;
    private static readonly TimeZoneInfo TaipeiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public async Task<AiProductSearchAccessState> ReadAsync(
        AiProductSearchActor actor,
        CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        var window = ResolveWindow(timeProvider.GetUtcNow());
        try
        {
            var used = await CountUsedAsync(actor, window, cancellationToken);
            return await CreateStateAsync(actor, used, window.ResetAtUtc, cancellationToken);
        }
        catch (DbException)
        {
            return Unavailable(window.ResetAtUtc);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return Unavailable(window.ResetAtUtc);
        }
    }

    public async Task<AiProductSearchReservationResult> TryReserveAsync(
        AiProductSearchActor actor,
        Guid requestPublicId,
        CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        if (requestPublicId == Guid.Empty)
        {
            throw new ArgumentException("RequestPublicId is required.", nameof(requestPublicId));
        }

        var now = timeProvider.GetUtcNow();
        var window = ResolveWindow(now);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await TryReserveOnceAsync(
                    actor,
                    requestPublicId,
                    now,
                    window,
                    cancellationToken);
            }
            catch (Exception exception) when (attempt == 0 && IsSqlDeadlock(exception))
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbException)
            {
                dbContext.ChangeTracker.Clear();
                return new AiProductSearchReservationResult(false, Unavailable(window.ResetAtUtc));
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear();
                return new AiProductSearchReservationResult(false, Unavailable(window.ResetAtUtc));
            }
            catch (InvalidOperationException exception) when (ContainsDbException(exception))
            {
                dbContext.ChangeTracker.Clear();
                return new AiProductSearchReservationResult(false, Unavailable(window.ResetAtUtc));
            }
        }

        return new AiProductSearchReservationResult(false, Unavailable(window.ResetAtUtc));
    }

    private async Task<AiProductSearchReservationResult> TryReserveOnceAsync(
        AiProductSearchActor actor,
        Guid requestPublicId,
        DateTimeOffset now,
        QuotaWindow window,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await dbContext.AiUsageLedger.AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.RequestPublicId == requestPublicId,
                cancellationToken);
        var used = await CountUsedAsync(actor, window, cancellationToken);
        var state = await CreateStateAsync(actor, used, window.ResetAtUtc, cancellationToken);
        if (state.BudgetProtectionActive && !state.IsDemoAllowlisted)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AiProductSearchReservationResult(false, state);
        }

        if (existing is not null)
        {
            var sameOwner = existing.Feature == AiUsageFeature.ProductSearch &&
                existing.Succeeded &&
                (actor.IsMember
                    ? existing.MemberUserId == actor.MemberUserId
                    : existing.AnonymousSessionKeyHash is not null &&
                      actor.AnonymousSessionKeyHash is not null &&
                      existing.AnonymousSessionKeyHash.SequenceEqual(actor.AnonymousSessionKeyHash));
            await transaction.CommitAsync(cancellationToken);
            return new AiProductSearchReservationResult(sameOwner, state);
        }

        var limit = actor.IsMember ? MemberDailyLimit : AnonymousDailyLimit;
        if (used >= limit)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AiProductSearchReservationResult(false, state);
        }

        dbContext.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveProductSearch(
            actor.MemberUserId,
            actor.AnonymousSessionKeyHash,
            requestPublicId,
            now.UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AiProductSearchReservationResult(
            true,
            state with { RemainingDailyRequests = Math.Max(0, limit - used - 1) });
    }

    private static bool IsSqlDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDbException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is DbException)
            {
                return true;
            }
        }

        return false;
    }

    private Task<int> CountUsedAsync(
        AiProductSearchActor actor,
        QuotaWindow window,
        CancellationToken cancellationToken) =>
        dbContext.AiUsageLedger.AsNoTracking().CountAsync(
            entry => entry.Feature == AiUsageFeature.ProductSearch &&
                entry.Succeeded &&
                entry.OccurredAtUtc >= window.StartsAtUtc &&
                entry.OccurredAtUtc < window.ResetAtUtc.UtcDateTime &&
                (actor.IsMember
                    ? entry.MemberUserId == actor.MemberUserId
                    : entry.AnonymousSessionKeyHash == actor.AnonymousSessionKeyHash),
            cancellationToken);

    private async Task<AiProductSearchAccessState> CreateStateAsync(
        AiProductSearchActor actor,
        int used,
        DateTimeOffset resetAtUtc,
        CancellationToken cancellationToken)
    {
        if (!await HasValidBudgetAlertRecipientAsync(cancellationToken))
        {
            return Unavailable(resetAtUtc);
        }

        var cumulativeCost = await dbContext.AiInteractions.AsNoTracking()
            .SumAsync(interaction => (decimal?)interaction.EstimatedCostUsd, cancellationToken) ?? 0m;
        var isDemoAllowlisted = actor.IsDemoAllowlisted;
        if (actor.IsMember && options.Value.DemoMemberPublicIds.Length > 0)
        {
            isDemoAllowlisted = await dbContext.Users.AsNoTracking().AnyAsync(
                user => user.Id == actor.MemberUserId &&
                    options.Value.DemoMemberPublicIds.Contains(user.PublicId),
                cancellationToken);
        }

        var limit = actor.IsMember ? MemberDailyLimit : AnonymousDailyLimit;
        return new AiProductSearchAccessState(
            Math.Max(0, limit - used),
            resetAtUtc,
            cumulativeCost >= 90m,
            isDemoAllowlisted);
    }

    private Task<bool> HasValidBudgetAlertRecipientAsync(CancellationToken cancellationToken)
    {
        var recipient = options.Value.BudgetAlertRecipientAdminPublicId;
        if (!recipient.HasValue || recipient.Value == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return (
            from user in dbContext.Users.AsNoTracking()
            join profile in dbContext.AdminProfiles.AsNoTracking() on user.Id equals profile.UserId
            join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.PublicId == recipient.Value &&
                user.AccountType == AccountType.Admin &&
                user.AccountStatus == AccountStatus.Active &&
                profile.IsActive &&
                role.Name == AuditRoleNames.SuperAdmin
            select user.Id).AnyAsync(cancellationToken);
    }

    private static void ValidateActor(AiProductSearchActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var hasMember = !string.IsNullOrWhiteSpace(actor.MemberUserId);
        var hasAnonymous = actor.AnonymousSessionKeyHash is { Length: 32 };
        if (hasMember == hasAnonymous)
        {
            throw new ArgumentException("Exactly one AI search actor is required.", nameof(actor));
        }
    }

    private static AiProductSearchAccessState Unavailable(DateTimeOffset resetAtUtc) =>
        new(0, resetAtUtc, BudgetProtectionActive: true, IsDemoAllowlisted: false);

    private static QuotaWindow ResolveWindow(DateTimeOffset now)
    {
        var taipeiNow = TimeZoneInfo.ConvertTime(now, TaipeiTimeZone);
        var localStart = DateTime.SpecifyKind(taipeiNow.Date, DateTimeKind.Unspecified);
        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, TaipeiTimeZone);
        var resetAtUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), TaipeiTimeZone);
        return new QuotaWindow(startsAtUtc, new DateTimeOffset(resetAtUtc, TimeSpan.Zero));
    }

    private sealed record QuotaWindow(DateTime StartsAtUtc, DateTimeOffset ResetAtUtc);
}

public sealed class EfAiProductSearchInteractionStore(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<OpenAiResponsesOptions> options,
    IOutboxWriter outboxWriter) : IAiProductSearchInteractionStore
{
    private const decimal BudgetWarningThresholdUsd = 70m;
    private const string SchemaVersion = "search-intent-v1";

    public async Task<bool> SaveAsync(
        AiProductSearchInteractionWrite interaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            if (await dbContext.AiInteractions.AsNoTracking().AnyAsync(
                    item => item.SearchPublicId == interaction.SearchPublicId,
                    cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var usage = interaction.Usage;
            var inputTokens = usage?.InputTokens ?? 0;
            var outputTokens = usage?.OutputTokens ?? 0;
            var estimatedCost = CalculateCost(inputTokens, outputTokens, options.Value);
            var cumulativeCostBefore = await dbContext.AiInteractions.AsNoTracking()
                .SumAsync(item => (decimal?)item.EstimatedCostUsd, cancellationToken) ?? 0m;
            var entity = AiInteraction.RecordProductSearch(
                Guid.NewGuid(),
                interaction.SearchPublicId,
                interaction.UserMessage,
                interaction.AssistantContent,
                interaction.Intent is null ? null : JsonSerializer.Serialize(interaction.Intent),
                usage?.Model ?? "unavailable",
                OpenAiProductSearchClient.PromptVersion,
                SchemaVersion,
                inputTokens,
                outputTokens,
                estimatedCost,
                interaction.IsDegraded ? AiInteractionStatus.Degraded : AiInteractionStatus.Answered,
                interaction.FallbackReason,
                interaction.LatencyMs,
                now);
            dbContext.AiInteractions.Add(entity);

            if (cumulativeCostBefore < BudgetWarningThresholdUsd &&
                cumulativeCostBefore + estimatedCost >= BudgetWarningThresholdUsd)
            {
                var recipient = options.Value.BudgetAlertRecipientAdminPublicId ?? throw new InvalidOperationException(
                    "The AI budget alert recipient is not configured.");
                AddBudgetAlertNotifications(recipient, interaction.SearchPublicId, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private static decimal CalculateCost(
        int inputTokens,
        int outputTokens,
        OpenAiResponsesOptions cost) =>
        decimal.Round(
            inputTokens / 1_000_000m * cost.ProductSearchInputCostPerMillionTokens +
            outputTokens / 1_000_000m * cost.ProductSearchOutputCostPerMillionTokens,
            6,
            MidpointRounding.AwayFromZero);

    private void AddBudgetAlertNotifications(
        Guid recipientPublicId,
        Guid searchPublicId,
        DateTime occurredAtUtc)
    {
        outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.NewGuid(),
            "AiBudget",
            searchPublicId,
            new EmailNotificationRequestedV1(
                Guid.NewGuid(),
                AiBudgetAlertNotificationContract.TemplateKey,
                AiBudgetAlertNotificationContract.RecipientPurpose,
                AiBudgetAlertNotificationContract.ResourceType,
                recipientPublicId,
                AiBudgetAlertNotificationContract.Locale,
                AiBudgetAlertNotificationContract.ParameterSetVersion),
            occurredAtUtc,
            occurredAtUtc,
            searchPublicId.ToString("D")));
        outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.NewGuid(),
            "AiBudget",
            searchPublicId,
            new InAppNotificationRequestedV1(
                Guid.NewGuid(),
                recipientPublicId,
                AiBudgetAlertNotificationContract.TemplateKey,
                AiBudgetAlertNotificationContract.ResourceType,
                recipientPublicId,
                AiBudgetAlertNotificationContract.Locale,
                AiBudgetAlertNotificationContract.ParameterSetVersion),
            occurredAtUtc,
            occurredAtUtc,
            searchPublicId.ToString("D")));
    }
}
