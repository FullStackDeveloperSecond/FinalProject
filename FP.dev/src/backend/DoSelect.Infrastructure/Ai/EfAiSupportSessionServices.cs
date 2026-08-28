using System.Data;
using System.Data.Common;
using DoSelect.Application.Ai;
using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Ai;

public sealed class EfAiConsentManager(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider) : IAiConsentManager
{
    public async Task<AiConsentSnapshot> ReadCurrentAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var latest = await ReadLatestAsync(memberId, cancellationToken);
            return Map(latest);
        }
        catch (DbException)
        {
            return Unavailable();
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return Unavailable();
        }
    }

    public async Task<AiConsentSnapshot> GrantAsync(
        Guid memberId,
        int policyVersion,
        SupportedLocale locale,
        CancellationToken cancellationToken)
    {
        if (policyVersion != AiConsentPolicy.CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }

        try
        {
            var latest = await ReadLatestAsync(memberId, cancellationToken);
            if (latest is { Status: AiConsentRecordStatus.Granted } && latest.Locale == locale)
            {
                return Map(latest);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            dbContext.AiConsentRecords.Add(AiConsentRecord.Grant(
                ToMemberUserId(memberId),
                policyVersion,
                AiConsentPurpose.Support,
                locale,
                "customer-web",
                now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AiConsentSnapshot(
                AiConsentState.Granted,
                policyVersion,
                locale,
                new DateTimeOffset(now, TimeSpan.Zero));
        }
        catch (DbException)
        {
            dbContext.ChangeTracker.Clear();
            return Unavailable();
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return Unavailable();
        }
    }

    public async Task<AiConsentSnapshot> WithdrawAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var latest = await ReadLatestAsync(memberId, cancellationToken);
            if (latest is null || latest.Status == AiConsentRecordStatus.Withdrawn)
            {
                return Map(latest);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            dbContext.AiConsentRecords.Add(AiConsentRecord.Withdraw(
                ToMemberUserId(memberId),
                AiConsentPolicy.CurrentVersion,
                AiConsentPurpose.Support,
                latest.Locale,
                "customer-web",
                DateTime.SpecifyKind(latest.GrantedAtUtc, DateTimeKind.Utc),
                now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AiConsentSnapshot(
                AiConsentState.Denied,
                AiConsentPolicy.CurrentVersion,
                latest.Locale,
                new DateTimeOffset(now, TimeSpan.Zero));
        }
        catch (DbException)
        {
            dbContext.ChangeTracker.Clear();
            return Unavailable();
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return Unavailable();
        }
    }

    private Task<AiConsentRecord?> ReadLatestAsync(Guid memberId, CancellationToken cancellationToken) =>
        dbContext.AiConsentRecords
            .AsNoTracking()
            .Where(record =>
                record.MemberUserId == ToMemberUserId(memberId) &&
                record.Purpose == AiConsentPurpose.Support &&
                record.PolicyVersion == AiConsentPolicy.CurrentVersion)
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static AiConsentSnapshot Map(AiConsentRecord? record) => record?.Status switch
    {
        AiConsentRecordStatus.Granted => new AiConsentSnapshot(
            AiConsentState.Granted,
            record.PolicyVersion,
            record.Locale,
            new DateTimeOffset(record.CreatedAtUtc, TimeSpan.Zero)),
        AiConsentRecordStatus.Withdrawn => new AiConsentSnapshot(
            AiConsentState.Denied,
            record.PolicyVersion,
            record.Locale,
            new DateTimeOffset(record.CreatedAtUtc, TimeSpan.Zero)),
        _ => new AiConsentSnapshot(
            AiConsentState.Missing,
            AiConsentPolicy.CurrentVersion,
            Locale: null,
            DecidedAtUtc: null),
    };

    private static AiConsentSnapshot Unavailable() => new(
        AiConsentState.Unavailable,
        AiConsentPolicy.CurrentVersion,
        Locale: null,
        DecidedAtUtc: null);

    private static string ToMemberUserId(Guid memberId) => memberId != Guid.Empty
        ? memberId.ToString("D")
        : throw new ArgumentException("A member identifier is required.", nameof(memberId));
}

public sealed class EfAiMemberUsageReader(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider) : IAiMemberUsageReader
{
    private static readonly TimeZoneInfo TaipeiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public async Task<AiMemberUsageSnapshot?> ReadSupportUsageAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var window = ResolveWindow(timeProvider.GetUtcNow());
            var memberUserId = memberId.ToString("D");
            var used = await dbContext.AiUsageLedger
                .AsNoTracking()
                .CountAsync(entry =>
                    entry.MemberUserId == memberUserId &&
                    entry.Feature == AiUsageFeature.Support &&
                    entry.Succeeded &&
                    entry.OccurredAtUtc >= window.StartsAtUtc.UtcDateTime &&
                    entry.OccurredAtUtc < window.ResetAtUtc.UtcDateTime,
                    cancellationToken);
            var memberUsage = await (
                from interaction in dbContext.AiInteractions.AsNoTracking()
                join conversation in dbContext.AiConversations.AsNoTracking()
                    on interaction.AiConversationId equals conversation.Id
                where conversation.MemberUserId == memberUserId &&
                    interaction.CreatedAtUtc >= window.StartsAtUtc.UtcDateTime &&
                    interaction.CreatedAtUtc < window.ResetAtUtc.UtcDateTime
                select new
                {
                    interaction.InputTokens,
                    interaction.OutputTokens,
                    interaction.EstimatedCostUsd,
                })
                .ToListAsync(cancellationToken);
            var cumulativeCost = await dbContext.AiInteractions
                .AsNoTracking()
                .SumAsync(interaction => (decimal?)interaction.EstimatedCostUsd, cancellationToken) ?? 0m;
            return new AiMemberUsageSnapshot(
                used,
                EfAiSupportAdmissionGate.DailySupportLimit,
                memberUsage.Sum(item => item.InputTokens),
                memberUsage.Sum(item => item.OutputTokens),
                memberUsage.Sum(item => item.EstimatedCostUsd),
                window.StartsAtUtc,
                window.ResetAtUtc,
                cumulativeCost >= 70m,
                cumulativeCost >= 90m);
        }
        catch (DbException)
        {
            return null;
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return null;
        }
    }

    private static UsageWindow ResolveWindow(DateTimeOffset now)
    {
        var taipeiNow = TimeZoneInfo.ConvertTime(now, TaipeiTimeZone);
        var startsAtTaipei = DateTime.SpecifyKind(taipeiNow.Date, DateTimeKind.Unspecified);
        var startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(startsAtTaipei, TaipeiTimeZone);
        var resetAtUtc = TimeZoneInfo.ConvertTimeToUtc(startsAtTaipei.AddDays(1), TaipeiTimeZone);
        return new UsageWindow(
            new DateTimeOffset(startsAtUtc, TimeSpan.Zero),
            new DateTimeOffset(resetAtUtc, TimeSpan.Zero));
    }

    private sealed record UsageWindow(DateTimeOffset StartsAtUtc, DateTimeOffset ResetAtUtc);
}

public sealed class EfAiAdminUsageReader(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider) : IAiAdminUsageReader
{
    public async Task<AiAdminUsageSnapshot?> ReadAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero ||
            fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(90))
        {
            throw new ArgumentOutOfRangeException(nameof(fromUtc));
        }

        try
        {
            var grouped = await dbContext.AiInteractions
                .AsNoTracking()
                .Where(interaction =>
                    interaction.CreatedAtUtc >= fromUtc.UtcDateTime &&
                    interaction.CreatedAtUtc < toUtc.UtcDateTime)
                .GroupBy(interaction => new
                {
                    IsSupport = interaction.AiConversationId != null,
                    interaction.Model,
                    interaction.Status,
                })
                .Select(group => new
                {
                    group.Key.IsSupport,
                    group.Key.Model,
                    group.Key.Status,
                    InteractionCount = group.Count(),
                    InputTokens = group.Sum(item => item.InputTokens),
                    OutputTokens = group.Sum(item => item.OutputTokens),
                    EstimatedCostUsd = group.Sum(item => item.EstimatedCostUsd),
                })
                .ToListAsync(cancellationToken);
            var cumulativeCost = await dbContext.AiInteractions
                .AsNoTracking()
                .SumAsync(interaction => (decimal?)interaction.EstimatedCostUsd, cancellationToken) ?? 0m;
            var rows = grouped
                .Select(group => new AiAdminUsageRow(
                    group.IsSupport ? "support" : "productSearch",
                    group.Model,
                    group.Status.ToString().ToLowerInvariant(),
                    group.InteractionCount,
                    group.InputTokens,
                    group.OutputTokens,
                    group.EstimatedCostUsd))
                .OrderBy(row => row.Feature, StringComparer.Ordinal)
                .ThenBy(row => row.Model, StringComparer.Ordinal)
                .ThenBy(row => row.Status, StringComparer.Ordinal)
                .ToArray();
            return new AiAdminUsageSnapshot(
                fromUtc,
                toUtc,
                rows,
                cumulativeCost,
                cumulativeCost >= 70m,
                cumulativeCost >= 90m,
                timeProvider.GetUtcNow());
        }
        catch (DbException)
        {
            return null;
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbException)
        {
            return null;
        }
    }
}

public sealed class EfAiSupportInteractionStore(
    DoSelectDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<OpenAiResponsesOptions> options,
    IOutboxWriter outboxWriter) : IAiSupportInteractionStore
{
    private const decimal BudgetWarningThresholdUsd = 70m;
    private const string PromptVersion = "support-v1";
    private const string SchemaVersion = "support-answer-v1";

    public async Task<AiSupportInteractionWriteResult> SaveAsync(
        AiSupportInteractionWrite interaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var memberUserId = interaction.MemberId.ToString("D");
            AiConversation conversation;
            if (interaction.ConversationPublicId is { } conversationPublicId)
            {
                conversation = await dbContext.AiConversations
                    .FromSqlInterpolated(
                        $"SELECT * FROM [AiConversations] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {conversationPublicId}")
                    .SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException(
                        "The AI conversation was not found.");
                if (conversation.MemberUserId != memberUserId ||
                    conversation.Status != AiConversationStatus.Active)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new AiSupportInteractionWriteResult(false, Guid.Empty);
                }

                conversation.RecordActivity(now, now.AddDays(180));
            }
            else
            {
                conversation = AiConversation.StartSupport(
                    Guid.NewGuid(),
                    memberUserId,
                    interaction.Locale,
                    AiConsentPolicy.CurrentVersion,
                    now.AddDays(180),
                    now);
                dbContext.AiConversations.Add(conversation);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var sequence = await dbContext.AiInteractions
                .Where(item => item.AiConversationId == conversation.Id)
                .Select(item => (int?)item.Sequence)
                .MaxAsync(cancellationToken) ?? 0;
            var usage = interaction.ModelUsage;
            var inputTokens = usage?.InputTokens ?? 0;
            var outputTokens = usage?.OutputTokens ?? 0;
            var estimatedCost = CalculateCost(inputTokens, outputTokens, options.Value);
            var cumulativeCostBefore = await dbContext.AiInteractions
                .AsNoTracking()
                .SumAsync(item => (decimal?)item.EstimatedCostUsd, cancellationToken) ?? 0m;
            var entity = AiInteraction.RecordSupport(
                interaction.InteractionPublicId,
                conversation.Id,
                sequence + 1,
                interaction.UserMessage,
                interaction.Answer,
                usage?.Model ?? "unavailable",
                PromptVersion,
                SchemaVersion,
                inputTokens,
                outputTokens,
                estimatedCost,
                interaction.IsDegraded ? AiInteractionStatus.Degraded : AiInteractionStatus.Answered,
                interaction.FallbackReason,
                interaction.LatencyMs,
                now);
            dbContext.AiInteractions.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            for (var index = 0; index < interaction.Citations.Count; index++)
            {
                var citation = interaction.Citations[index];
                dbContext.AiCitations.Add(new AiCitation(
                    entity.Id,
                    citation.SourceType,
                    Guid.TryParse(citation.SourceId, out var sourcePublicId) ? sourcePublicId : null,
                    citation.VersionOrUpdatedAt,
                    citation.Title,
                    index,
                    now));
            }

            if (cumulativeCostBefore < BudgetWarningThresholdUsd &&
                cumulativeCostBefore + estimatedCost >= BudgetWarningThresholdUsd)
            {
                var recipientPublicId = await ResolveBudgetAlertRecipientAsync(
                    options.Value.BudgetAlertRecipientAdminPublicId,
                    cancellationToken) ?? throw new InvalidOperationException(
                        "The configured AI budget alert recipient is not an active SuperAdmin.");
                AddBudgetAlertNotifications(
                    recipientPublicId,
                    interaction.InteractionPublicId,
                    now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AiSupportInteractionWriteResult(true, conversation.PublicId);
        }
        catch (DbException)
        {
            dbContext.ChangeTracker.Clear();
            return Failed(interaction);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return Failed(interaction);
        }
        catch (InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Failed(interaction);
        }
    }

    private static decimal CalculateCost(
        int inputTokens,
        int outputTokens,
        OpenAiResponsesOptions cost) =>
        decimal.Round(
            inputTokens / 1_000_000m * cost.SupportInputCostPerMillionTokens +
            outputTokens / 1_000_000m * cost.SupportOutputCostPerMillionTokens,
            6,
            MidpointRounding.AwayFromZero);

    private async Task<Guid?> ResolveBudgetAlertRecipientAsync(
        Guid? configuredPublicId,
        CancellationToken cancellationToken)
    {
        if (!configuredPublicId.HasValue || configuredPublicId.Value == Guid.Empty)
        {
            return null;
        }

        return await (
            from user in dbContext.Users.AsNoTracking()
            join profile in dbContext.AdminProfiles.AsNoTracking()
                on user.Id equals profile.UserId
            join userRole in dbContext.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where user.PublicId == configuredPublicId.Value &&
                user.AccountType == AccountType.Admin &&
                user.AccountStatus == AccountStatus.Active &&
                profile.IsActive &&
                role.Name == AuditRoleNames.SuperAdmin
            select (Guid?)user.PublicId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void AddBudgetAlertNotifications(
        Guid recipientPublicId,
        Guid interactionPublicId,
        DateTime occurredAtUtc)
    {
        var emailNotificationPublicId = Guid.NewGuid();
        outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.NewGuid(),
            "AiBudget",
            interactionPublicId,
            new EmailNotificationRequestedV1(
                emailNotificationPublicId,
                AiBudgetAlertNotificationContract.TemplateKey,
                AiBudgetAlertNotificationContract.RecipientPurpose,
                AiBudgetAlertNotificationContract.ResourceType,
                recipientPublicId,
                AiBudgetAlertNotificationContract.Locale,
                AiBudgetAlertNotificationContract.ParameterSetVersion),
            occurredAtUtc,
            occurredAtUtc,
            interactionPublicId.ToString("D")));

        var inAppNotificationPublicId = Guid.NewGuid();
        outboxWriter.Add(OutboxWriteRequest.Create(
            Guid.NewGuid(),
            "AiBudget",
            interactionPublicId,
            new InAppNotificationRequestedV1(
                inAppNotificationPublicId,
                recipientPublicId,
                AiBudgetAlertNotificationContract.TemplateKey,
                AiBudgetAlertNotificationContract.ResourceType,
                recipientPublicId,
                AiBudgetAlertNotificationContract.Locale,
                AiBudgetAlertNotificationContract.ParameterSetVersion),
            occurredAtUtc,
            occurredAtUtc,
            interactionPublicId.ToString("D")));
    }

    private static AiSupportInteractionWriteResult Failed(AiSupportInteractionWrite interaction) =>
        new(false, interaction.ConversationPublicId ?? Guid.Empty);
}
