using DoSelect.Application.Ai;
using DoSelect.Application.Auditing;
using DoSelect.Application.Outbox;
using DoSelect.Domain.Ai;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Members;
using DoSelect.Domain.Orders;
using DoSelect.Domain.Shipping;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Ai;
using DoSelect.Infrastructure.Outbox;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class EfAiSafetyInfrastructureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task ProductSearchAdmission_AnonymousOwner_ReservesAndReplaysIdempotently()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var admin = await SeedAdminAsync(context, assignSuperAdmin: true);
            var options = Options.Create(new OpenAiResponsesOptions
            {
                BudgetAlertRecipientAdminPublicId = admin.PublicId,
            });
            var actor = new AiProductSearchActor(
                MemberUserId: null,
                AnonymousSessionKeyHash: Enumerable.Repeat((byte)0x2A, 32).ToArray(),
                IsDemoAllowlisted: false);
            var requestPublicId = Guid.NewGuid();
            var gate = new EfAiProductSearchAdmissionGate(
                context,
                new FixedTimeProvider(Now),
                options);

            var first = await gate.TryReserveAsync(actor, requestPublicId, CancellationToken.None);
            var replay = await gate.TryReserveAsync(actor, requestPublicId, CancellationToken.None);

            Assert.True(first.IsReserved);
            Assert.True(replay.IsReserved);
            Assert.Equal(9, first.State.RemainingDailyRequests);
            Assert.Equal(9, replay.State.RemainingDailyRequests);
            Assert.Equal(1, await context.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task ProductSearchAdmission_ConcurrentAnonymousLastQuota_AllowsExactlyOneReservation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var ownerHash = Enumerable.Repeat((byte)0x4B, 32).ToArray();
            Guid recipientPublicId;
            await using (var seed = CreateContext(connectionString))
            {
                var admin = await SeedAdminAsync(seed, assignSuperAdmin: true);
                recipientPublicId = admin.PublicId;
                for (var index = 0; index < EfAiProductSearchAdmissionGate.AnonymousDailyLimit - 1; index++)
                {
                    seed.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveProductSearch(
                        memberUserId: null,
                        anonymousSessionKeyHash: ownerHash,
                        requestPublicId: Guid.NewGuid(),
                        occurredAtUtc: Now.UtcDateTime.AddMinutes(index)));
                }

                await seed.SaveChangesAsync();
            }

            async Task<AiProductSearchReservationResult> ReserveAsync()
            {
                await using var context = CreateContext(connectionString);
                var gate = new EfAiProductSearchAdmissionGate(
                    context,
                    new FixedTimeProvider(Now),
                    Options.Create(new OpenAiResponsesOptions
                    {
                        BudgetAlertRecipientAdminPublicId = recipientPublicId,
                    }));
                return await gate.TryReserveAsync(
                    new AiProductSearchActor(null, ownerHash, IsDemoAllowlisted: false),
                    Guid.NewGuid(),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(ReserveAsync(), ReserveAsync());

            Assert.Equal(1, results.Count(result => result.IsReserved));
            await using var verify = CreateContext(connectionString);
            Assert.Equal(
                EfAiProductSearchAdmissionGate.AnonymousDailyLimit,
                await verify.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_GrantedConsent_ReservesOnceAndReplaysIdempotently()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            var requestPublicId = Guid.NewGuid();
            var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));

            var before = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var first = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                requestPublicId,
                CancellationToken.None);
            var replay = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                requestPublicId,
                CancellationToken.None);

            Assert.Equal(AiConsentState.Granted, before.ConsentState);
            Assert.Equal(EfAiSupportAdmissionGate.DailySupportLimit, before.RemainingDailyMessages);
            Assert.True(first.IsReserved);
            Assert.Equal(19, first.State.RemainingDailyMessages);
            Assert.True(replay.IsReserved);
            Assert.Equal(19, replay.State.RemainingDailyMessages);
            Assert.Equal(1, await context.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task MigrationChain_CreatesAiSafetyAndSupportTables()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);

            Assert.Contains(
                await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith(
                    "_AddAiSafetyConsentAndUsage",
                    StringComparison.Ordinal));
            Assert.Contains(
                await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith(
                    "_AddAiSupportConversationsAndInteractions",
                    StringComparison.Ordinal));
            Assert.Equal(0, await context.AiConsentRecords.CountAsync());
            Assert.Equal(0, await context.AiUsageLedger.CountAsync());
            Assert.Equal(0, await context.AiConversations.CountAsync());
            Assert.Equal(0, await context.AiInteractions.CountAsync());
            Assert.Equal(0, await context.AiCitations.CountAsync());
        }, useMigrations: true);
    }

    [SqlServerFact]
    public async Task AdmissionGate_ConcurrentLastQuota_AllowsExactlyOneReservation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            string memberUserId;
            await using (var seed = CreateContext(connectionString))
            {
                var member = await SeedMemberWithConsentAsync(seed);
                memberUserId = member.Id;
                for (var index = 0;
                     index < EfAiSupportAdmissionGate.DailySupportLimit - 1;
                     index++)
                {
                    seed.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                        memberUserId,
                        Guid.NewGuid(),
                        Now.UtcDateTime.AddMinutes(index)));
                }

                await seed.SaveChangesAsync();
            }

            async Task<AiSupportReservationResult> ReserveAsync()
            {
                await using var context = CreateContext(connectionString);
                var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));
                return await gate.TryReserveAsync(
                    Guid.Parse(memberUserId),
                    Guid.NewGuid(),
                    CancellationToken.None);
            }

            var results = await Task.WhenAll(ReserveAsync(), ReserveAsync());

            Assert.Equal(1, results.Count(result => result.IsReserved));
            await using var verify = CreateContext(connectionString);
            Assert.Equal(
                EfAiSupportAdmissionGate.DailySupportLimit,
                await verify.AiUsageLedger.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_LatestWithdrawal_DeniesWithoutWritingUsage()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            context.AiConsentRecords.Add(AiConsentRecord.Withdraw(
                member.Id,
                policyVersion: 1,
                AiConsentPurpose.Support,
                SupportedLocale.ZhTw,
                source: "MemberWeb",
                Now.UtcDateTime,
                Now.UtcDateTime.AddMinutes(1)));
            await context.SaveChangesAsync();
            var gate = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(Now.AddMinutes(2)));

            var state = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var reservation = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal(AiConsentState.Denied, state.ConsentState);
            Assert.False(reservation.IsReserved);
            Assert.Empty(await context.AiUsageLedger.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_MismatchedConsentPolicyVersion_DeniesWithoutWritingUsage()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"member-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            context.AiConsentRecords.Add(AiConsentRecord.Grant(
                member.Id,
                policyVersion: 2,
                AiConsentPurpose.Support,
                SupportedLocale.ZhTw,
                source: "MemberWeb",
                Now.UtcDateTime));
            await context.SaveChangesAsync();
            var gate = new EfAiSupportAdmissionGate(context, new FixedTimeProvider(Now));

            var state = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var reservation = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal(AiConsentState.Missing, state.ConsentState);
            Assert.False(reservation.IsReserved);
            Assert.Empty(await context.AiUsageLedger.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_DailyQuota_ResetsAtTaipeiMidnight()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            for (var index = 0; index < EfAiSupportAdmissionGate.DailySupportLimit; index++)
            {
                context.AiUsageLedger.Add(AiUsageLedgerEntry.ReserveSupport(
                    member.Id,
                    Guid.NewGuid(),
                    Now.UtcDateTime.AddMinutes(index)));
            }

            await context.SaveChangesAsync();

            var beforeMidnight = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 28, 15, 59, 0, TimeSpan.Zero)));
            var afterMidnight = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)));

            var before = await beforeMidnight.ReadAsync(
                Guid.Parse(member.Id),
                CancellationToken.None);
            var after = await afterMidnight.ReadAsync(
                Guid.Parse(member.Id),
                CancellationToken.None);

            Assert.Equal(0, before.RemainingDailyMessages);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero),
                before.ResetAtUtc);
            Assert.Equal(EfAiSupportAdmissionGate.DailySupportLimit, after.RemainingDailyMessages);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 29, 16, 0, 0, TimeSpan.Zero),
                after.ResetAtUtc);
        });
    }

    [SqlServerFact]
    public async Task ContextReader_ReturnsOnlyOwnerScopedDeidentifiedOrderData()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var owner = await SeedMemberWithConsentAsync(context);
            var other = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"other-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(other);
            await context.SaveChangesAsync();
            var order = await SeedMemberOrderAsync(context, owner.Id);
            var reader = new EfAiSupportContextReader(context);

            var ownerResult = await reader.ReadAsync(
                Guid.Parse(owner.Id),
                null,
                [order.PublicId],
                [],
                CancellationToken.None);
            var otherResult = await reader.ReadAsync(
                Guid.Parse(other.Id),
                null,
                [order.PublicId],
                [],
                CancellationToken.None);

            Assert.Equal(AiSupportContextStatus.Allowed, ownerResult.Status);
            var payload = Assert.Single(ownerResult.DataItems);
            Assert.Equal("order", payload.SourceType);
            Assert.Equal(order.PublicId.ToString("D"), payload.SourceId);
            Assert.Equal(order.OrderNumber, payload.Title);
            Assert.Contains("Creator GPU", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain(order.OrderNumber, payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("[[SYNTHETIC_NAME]]", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-owner@example.test", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("0912345678", payload.Content, StringComparison.Ordinal);
            Assert.Equal(AiSupportContextStatus.ResourceNotFound, otherResult.Status);
            Assert.Empty(otherResult.DataItems);

            var prompt = AiPromptEnvelopeFactory.TryCreateSupport(
                SupportedLocale.ZhTw,
                "請查看這張訂單",
                ownerResult.DataItems);
            Assert.NotNull(prompt.Envelope);
            Assert.Equal(AiSafetyReason.None, prompt.Reason);
        });
    }

    [SqlServerFact]
    public async Task ConsentManager_GrantAndWithdraw_AreAppendOnlyAndIdempotent()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"consent-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(member);
            await context.SaveChangesAsync();
            var manager = new EfAiConsentManager(context, new FixedTimeProvider(Now));
            var memberId = Guid.Parse(member.Id);

            var granted = await manager.GrantAsync(
                memberId,
                AiConsentPolicy.CurrentVersion,
                SupportedLocale.ZhTw,
                CancellationToken.None);
            var replay = await manager.GrantAsync(
                memberId,
                AiConsentPolicy.CurrentVersion,
                SupportedLocale.ZhTw,
                CancellationToken.None);
            var withdrawn = await manager.WithdrawAsync(memberId, CancellationToken.None);

            Assert.Equal(AiConsentState.Granted, granted.State);
            Assert.Equal(granted, replay);
            Assert.Equal(AiConsentState.Denied, withdrawn.State);
            Assert.Equal(2, await context.AiConsentRecords.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task ContextReader_SupportTicket_ExcludesInternalMessagesAndCrossOwnerAccess()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var owner = await SeedMemberWithConsentAsync(context);
            var other = ApplicationUser.CreateMember(
                Guid.NewGuid(),
                $"ticket-other-{Guid.NewGuid():N}@example.test",
                Now.UtcDateTime);
            context.Users.Add(other);
            await context.SaveChangesAsync();
            var ticket = new SupportTicket(
                Guid.NewGuid(),
                "SUP-0912-345-678",
                owner.Id,
                null,
                SupportTicketCategory.ProductWarranty,
                "顯示器保固問題",
                CasePriority.Normal,
                Now.UtcDateTime.AddHours(4),
                Now.UtcDateTime.AddDays(2),
                Now.UtcDateTime);
            context.SupportTickets.Add(ticket);
            await context.SaveChangesAsync();
            context.SupportMessages.AddRange(
                new SupportMessage(
                    Guid.NewGuid(), ticket.Id, SupportSenderType.Member, owner.Id,
                    "螢幕偶爾閃爍", false, false, null, "zh-TW", Now.UtcDateTime.AddMinutes(1)),
                new SupportMessage(
                    Guid.NewGuid(), ticket.Id, SupportSenderType.Admin, other.Id,
                    "INTERNAL_DIAGNOSTIC_ONLY", true, false, null, "zh-TW", Now.UtcDateTime.AddMinutes(2)));
            await context.SaveChangesAsync();
            var reader = new EfAiSupportContextReader(context);

            var ownerResult = await reader.ReadAsync(
                Guid.Parse(owner.Id), null, [], [ticket.PublicId], CancellationToken.None);
            var otherResult = await reader.ReadAsync(
                Guid.Parse(other.Id), null, [], [ticket.PublicId], CancellationToken.None);

            var payload = Assert.Single(ownerResult.DataItems);
            Assert.Equal("support_ticket", payload.SourceType);
            Assert.Contains("螢幕偶爾閃爍", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("INTERNAL_DIAGNOSTIC_ONLY", payload.Content, StringComparison.Ordinal);
            Assert.DoesNotContain(ticket.TicketNumber, payload.Content, StringComparison.Ordinal);
            Assert.Equal(AiSupportContextStatus.ResourceNotFound, otherResult.Status);

            var prompt = AiPromptEnvelopeFactory.TryCreateSupport(
                SupportedLocale.ZhTw,
                "請查看這張客服案件",
                ownerResult.DataItems);
            Assert.NotNull(prompt.Envelope);
            Assert.Equal(AiSafetyReason.None, prompt.Reason);
        });
    }

    [SqlServerFact]
    public async Task InteractionStore_PersistsUsageCostCitationAndOwnerScopedConversation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var owner = await SeedMemberWithConsentAsync(context);
            var store = new EfAiSupportInteractionStore(
                context,
                new FixedTimeProvider(Now),
                Options.Create(new OpenAiResponsesOptions
                {
                    SupportInputCostPerMillionTokens = 10m,
                    SupportOutputCostPerMillionTokens = 20m,
                }),
                new EfOutboxWriter(context, new FixedTimeProvider(Now)));
            var interactionPublicId = Guid.NewGuid();

            var saved = await store.SaveAsync(
                new AiSupportInteractionWrite(
                    Guid.Parse(owner.Id),
                    ConversationPublicId: null,
                    interactionPublicId,
                    "退貨流程是什麼？",
                    SupportedLocale.ZhTw,
                    "請由訂單頁提出申請。",
                    [new AiSupportCitation("faq", Guid.NewGuid().ToString("D"), "退貨規則", "v1")],
                    new AiSupportModelUsage("integration-model", 1_000, 500),
                    IsDegraded: false,
                    FallbackReason: null,
                    LatencyMs: 250),
                CancellationToken.None);

            var conversation = await context.AiConversations.AsNoTracking().SingleAsync();
            var interaction = await context.AiInteractions.AsNoTracking().SingleAsync();
            Assert.True(saved.Succeeded);
            Assert.Equal(conversation.PublicId, saved.ConversationPublicId);
            Assert.Equal(Now.UtcDateTime.AddDays(180), conversation.ExpiresAtUtc);
            Assert.Equal(interactionPublicId, interaction.PublicId);
            Assert.Equal(0.02m, interaction.EstimatedCostUsd);
            Assert.Equal(1_000, interaction.InputTokens);
            Assert.Equal(500, interaction.OutputTokens);
            Assert.Single(await context.AiCitations.AsNoTracking().ToListAsync());

            var adminUsage = await new EfAiAdminUsageReader(
                context,
                new FixedTimeProvider(Now)).ReadAsync(
                    Now.AddDays(-1),
                    Now.AddDays(1),
                    CancellationToken.None);
            Assert.NotNull(adminUsage);
            var usageRow = Assert.Single(adminUsage.Rows);
            Assert.Equal("support", usageRow.Feature);
            Assert.Equal("integration-model", usageRow.Model);
            Assert.Equal(0.02m, usageRow.EstimatedCostUsd);

            var reader = new EfAiSupportContextReader(context);
            var denied = await reader.ReadAsync(
                Guid.NewGuid(),
                conversation.PublicId,
                [],
                [],
                CancellationToken.None);
            Assert.Equal(AiSupportContextStatus.ResourceNotFound, denied.Status);
        });
    }

    [SqlServerFact]
    public async Task InteractionStore_CrossesBudgetWarning_QueuesEmailAndInAppOnlyOnce()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            var admin = await SeedAdminAsync(context, assignSuperAdmin: true);
            var priorConversation = AiConversation.StartSupport(
                Guid.NewGuid(),
                member.Id,
                SupportedLocale.ZhTw,
                AiConsentPolicy.CurrentVersion,
                Now.UtcDateTime.AddDays(180),
                Now.UtcDateTime);
            context.AiConversations.Add(priorConversation);
            await context.SaveChangesAsync();
            context.AiInteractions.Add(AiInteraction.RecordSupport(
                Guid.NewGuid(),
                priorConversation.Id,
                sequence: 1,
                "先前互動",
                "先前回答",
                "integration-model",
                "support-v1",
                "support-answer-v1",
                inputTokens: 0,
                outputTokens: 0,
                estimatedCostUsd: 69.99m,
                AiInteractionStatus.Answered,
                fallbackReason: null,
                latencyMs: 100,
                Now.UtcDateTime));
            await context.SaveChangesAsync();

            var options = Options.Create(new OpenAiResponsesOptions
            {
                SupportInputCostPerMillionTokens = 20m,
                SupportOutputCostPerMillionTokens = 0m,
                BudgetAlertRecipientAdminPublicId = admin.PublicId,
            });
            var store = new EfAiSupportInteractionStore(
                context,
                new FixedTimeProvider(Now),
                options,
                new EfOutboxWriter(context, new FixedTimeProvider(Now)));

            var first = await store.SaveAsync(
                CreateInteractionWrite(member, conversationPublicId: null),
                CancellationToken.None);
            var second = await store.SaveAsync(
                CreateInteractionWrite(member, first.ConversationPublicId),
                CancellationToken.None);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            var alerts = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.AggregateType == "AiBudget")
                .OrderBy(message => message.Type)
                .ToListAsync();
            Assert.Equal(2, alerts.Count);
            Assert.Contains(alerts, message =>
                message.Type == OutboxEventTypes.EmailNotificationRequestedV1);
            Assert.Contains(alerts, message =>
                message.Type == OutboxEventTypes.InAppNotificationRequestedV1);
            Assert.All(alerts, message =>
                Assert.Contains(admin.PublicId.ToString("D"), message.PayloadJson, StringComparison.Ordinal));
        });
    }

    [SqlServerFact]
    public async Task AdmissionGate_InvalidBudgetAlertRecipient_FailsClosedBeforeReservation()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var member = await SeedMemberWithConsentAsync(context);
            var adminWithoutSuperRole = await SeedAdminAsync(context, assignSuperAdmin: false);
            var gate = new EfAiSupportAdmissionGate(
                context,
                new FixedTimeProvider(Now),
                Options.Create(new OpenAiResponsesOptions
                {
                    BudgetAlertRecipientAdminPublicId = adminWithoutSuperRole.PublicId,
                }));

            var state = await gate.ReadAsync(Guid.Parse(member.Id), CancellationToken.None);
            var reservation = await gate.TryReserveAsync(
                Guid.Parse(member.Id),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal(AiConsentState.Unavailable, state.ConsentState);
            Assert.False(reservation.IsReserved);
            Assert.Equal(AiConsentState.Unavailable, reservation.State.ConsentState);
            Assert.Empty(await context.AiUsageLedger.AsNoTracking().ToListAsync());
        });
    }

    private static AiSupportInteractionWrite CreateInteractionWrite(
        ApplicationUser member,
        Guid? conversationPublicId) =>
        new(
            Guid.Parse(member.Id),
            conversationPublicId,
            Guid.NewGuid(),
            "成本警示測試",
            SupportedLocale.ZhTw,
            "測試回答",
            [],
            new AiSupportModelUsage("integration-model", 1_000, 0),
            IsDegraded: false,
            FallbackReason: null,
            LatencyMs: 100);

    private static async Task<ApplicationUser> SeedAdminAsync(
        DoSelectDbContext context,
        bool assignSuperAdmin)
    {
        var admin = ApplicationUser.CreateAdmin(
            Guid.NewGuid(),
            $"admin-{Guid.NewGuid():N}@example.test",
            Now.UtcDateTime);
        admin.ConfirmEmail(Now.UtcDateTime);
        context.Users.Add(admin);
        context.AdminProfiles.Add(new AdminProfile(
            admin.Id,
            admin.PublicId,
            $"AI-{Guid.NewGuid():N}"[..16],
            "AI 預算通知管理員",
            Now.UtcDateTime));

        if (assignSuperAdmin)
        {
            var role = new IdentityRole(AuditRoleNames.SuperAdmin)
            {
                NormalizedName = AuditRoleNames.SuperAdmin.ToUpperInvariant(),
            };
            context.Roles.Add(role);
            context.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = admin.Id,
                RoleId = role.Id,
            });
        }

        await context.SaveChangesAsync();
        return admin;
    }

    private static async Task<ApplicationUser> SeedMemberWithConsentAsync(
        DoSelectDbContext context)
    {
        var member = ApplicationUser.CreateMember(
            Guid.NewGuid(),
            $"member-{Guid.NewGuid():N}@example.test",
            Now.UtcDateTime);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        context.AiConsentRecords.Add(AiConsentRecord.Grant(
            member.Id,
            policyVersion: AiConsentPolicy.CurrentVersion,
            AiConsentPurpose.Support,
            SupportedLocale.ZhTw,
            source: "MemberWeb",
            Now.UtcDateTime));
        await context.SaveChangesAsync();
        return member;
    }

    private static async Task<Order> SeedMemberOrderAsync(
        DoSelectDbContext context,
        string memberUserId)
    {
        var profile = new ShippingProviderProfile(
            Guid.NewGuid(),
            $"AI-{Guid.NewGuid():N}"[..16],
            1,
            "Active",
            null,
            null,
            "{}",
            1,
            Now.UtcDateTime);
        context.ShippingProviderProfiles.Add(profile);
        await context.SaveChangesAsync();
        var packageLimit = new PackageLimitVersion(
            Guid.NewGuid(),
            profile.Id,
            1,
            30m,
            150m,
            100m,
            100m,
            250m,
            50_000m,
            null,
            null,
            Now.UtcDateTime);
        context.PackageLimitVersions.Add(packageLimit);
        await context.SaveChangesAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            new OrderCreation(
                $"AI-{Guid.NewGuid():N}"[..32],
                memberUserId,
                null,
                OrderStatus.Confirmed,
                PaymentStatus.Paid,
                FulfillmentStatus.Preparing,
                AssemblyStatus.NotRequired,
                100m,
                0m,
                10m,
                0m,
                110m,
                "[[SYNTHETIC_NAME]]",
                "0912345678",
                "synthetic-owner@example.test",
                "100",
                "Taipei",
                "Zhongzheng",
                "[[SYNTHETIC_ADDRESS]]",
                null,
                "HOME",
                profile.Id,
                null,
                null,
                null,
                1,
                1,
                null,
                null,
                $"ai-{Guid.NewGuid():N}",
                null,
                1,
                1,
                new OrderInvoicePreference(
                    SimulatedInvoiceBuyerType.Individual,
                    "synthetic-owner@example.test",
                    null,
                    null,
                    null,
                    null),
                1_000m,
                null,
                new OrderPackageSnapshot(
                    packageLimit.Id,
                    1m,
                    40m,
                    30m,
                    20m,
                    90m,
                    100m)),
            Now.UtcDateTime);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.OrderItems.Add(new OrderItem(
            Guid.NewGuid(),
            order.Id,
            skuId: null,
            skuCodeSnapshot: "GPU-CREATOR",
            productNameSnapshot: "Creator GPU",
            skuNameSnapshot: "24GB",
            quantity: 1,
            listUnitPrice: 100m,
            saleUnitPrice: 100m,
            finalUnitPrice: 100m,
            unitCostSnapshot: 80m,
            lineSubtotal: 100m,
            discountAllocation: 0m,
            lineTotal: 100m,
            assemblyGroupKey: null,
            returnableQuantity: 1,
            Now.UtcDateTime,
            isCouponEligible: true,
            new OrderItemSpecificationSnapshot("24GB", "{\"vram\":\"24GB\"}", 1)));
        await context.SaveChangesAsync();
        return order;
    }

    private static async Task WithDatabaseAsync(
        Func<string, Task> test,
        bool useMigrations = true)
    {
        var connectionString = SqlServerTestConnection.Build(
            $"DoSelectAiSafety_{Guid.NewGuid():N}") + ";Encrypt=False;";
        await using var setup = CreateContext(connectionString);
        try
        {
            await setup.Database.MigrateAsync();
            await test(connectionString);
        }
        finally
        {
            await setup.Database.CloseConnectionAsync();
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static DoSelectDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
