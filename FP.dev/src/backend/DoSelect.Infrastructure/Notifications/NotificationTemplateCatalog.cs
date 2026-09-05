using System.Security.Cryptography;
using DoSelect.Application.Ai;
using DoSelect.Application.Auditing;
using DoSelect.Application.Notifications;
using DoSelect.Application.Orders;
using DoSelect.Application.Outbox;
using DoSelect.Application.Support;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Notifications;

internal sealed record NotificationTemplate(string Subject, string Body);

internal static class NotificationTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, NotificationTemplate>>
        Templates = new Dictionary<string, IReadOnlyDictionary<string, NotificationTemplate>>(
            StringComparer.Ordinal)
        {
            ["order.created"] = Localized("訂單已成立", "您的訂單已成立，請至訂單頁查看詳情。", "注文を受け付けました", "注文ページで詳細をご確認ください。", "주문이 접수되었습니다", "주문 페이지에서 상세 내용을 확인해 주세요."),
            ["payment.succeeded"] = Localized("付款成功", "您的付款已完成。", "お支払いが完了しました", "お支払いが正常に完了しました。", "결제가 완료되었습니다", "결제가 정상적으로 완료되었습니다."),
            ["payment.failed"] = Localized("付款未完成", "付款未完成，請至訂單頁確認付款狀態。", "お支払いを完了できませんでした", "注文ページで支払い状況をご確認ください。", "결제가 완료되지 않았습니다", "주문 페이지에서 결제 상태를 확인해 주세요."),
            ["payment.expired"] = Localized("付款期限已到", "付款期限已到，訂單狀態請以訂單頁為準。", "お支払い期限が切れました", "注文ページで現在の状態をご確認ください。", "결제 기한이 만료되었습니다", "주문 페이지에서 현재 상태를 확인해 주세요."),
            ["payment.cancelled"] = Localized("付款已取消", "本次付款已取消，訂單狀態請以訂單頁為準。", "お支払いをキャンセルしました", "今回のお支払いはキャンセルされました。注文ページで現在の状態をご確認ください。", "결제가 취소되었습니다", "이번 결제가 취소되었습니다. 주문 페이지에서 현재 상태를 확인해 주세요."),
            ["order.cancelled"] = Localized("訂單已取消", "您的訂單已取消。", "注文をキャンセルしました", "注文はキャンセルされました。", "주문이 취소되었습니다", "주문이 취소되었습니다."),
            ["shipment.updated"] = Localized("物流狀態已更新", "您的商品物流狀態已更新。", "配送状況が更新されました", "商品の配送状況が更新されました。", "배송 상태가 업데이트되었습니다", "상품 배송 상태가 업데이트되었습니다."),
            ["return.updated"] = Localized("退貨狀態已更新", "您的退貨申請狀態已更新。", "返品状況が更新されました", "返品申請の状態が更新されました。", "반품 상태가 업데이트되었습니다", "반품 신청 상태가 업데이트되었습니다."),
            ["refund.succeeded"] = Localized("退款完成", "您的退款已完成。", "返金が完了しました", "返金処理が完了しました。", "환불이 완료되었습니다", "환불 처리가 완료되었습니다."),
            ["refund.failed"] = Localized("退款處理未完成", "退款處理未完成，請聯絡客服。", "返金処理を完了できませんでした", "カスタマーサポートへお問い合わせください。", "환불 처리가 완료되지 않았습니다", "고객센터에 문의해 주세요."),
            ["support.replied"] = Localized("客服已回覆", "您的客服案件已有新回覆。", "サポートから返信がありました", "お問い合わせに新しい返信があります。", "고객센터 답변이 등록되었습니다", "문의에 새로운 답변이 등록되었습니다."),
            [SupportSlaNotificationContract.Warning80TemplateKey] = Localized(
                "客服案件即將逾時",
                "承辦案件已使用 80% SLA 時間，請儘速處理。",
                "サポート案件の期限が近づいています",
                "担当案件が SLA 時間の 80% に達しました。早めにご対応ください。",
                "고객지원 건의 기한이 임박했습니다",
                "담당 건이 SLA 시간의 80%에 도달했습니다. 신속히 처리해 주세요."),
            [SupportSlaNotificationContract.Overdue100TemplateKey] = Localized(
                "客服案件已逾時",
                "客服案件已超過 SLA 時限，請優先處理。",
                "サポート案件が期限を超過しました",
                "サポート案件が SLA 期限を超過しました。優先してご対応ください。",
                "고객지원 건이 기한을 초과했습니다",
                "고객지원 건이 SLA 기한을 초과했습니다. 우선 처리해 주세요."),
            [AiBudgetAlertNotificationContract.TemplateKey] = Localized(
                "AI 成本已達警示門檻",
                "AI 累計估算成本已達 US$70，請至 AI 用量頁確認成本與展示額度。",
                "AI コストが警告しきい値に達しました",
                "AI の累積推定コストが US$70 に達しました。AI 使用量ページをご確認ください。",
                "AI 비용이 경고 기준에 도달했습니다",
                "AI 누적 예상 비용이 US$70에 도달했습니다. AI 사용량 페이지를 확인해 주세요."),
        };

    public static NotificationTemplate? Find(string key, string locale)
    {
        if (!Templates.TryGetValue(key, out var localized))
        {
            return null;
        }

        return localized.TryGetValue(locale, out var exact)
            ? exact
            : localized["zh-TW"];
    }

    private static IReadOnlyDictionary<string, NotificationTemplate> Localized(
        string zhTitle,
        string zhBody,
        string jaTitle,
        string jaBody,
        string koTitle,
        string koBody) =>
        new Dictionary<string, NotificationTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-TW"] = new(zhTitle, zhBody),
            ["ja-JP"] = new(jaTitle, jaBody),
            ["ko-KR"] = new(koTitle, koBody),
        };
}

public sealed class InAppNotificationContentRenderer : IInAppNotificationContentRenderer
{
    public InAppNotificationContent? Render(InAppNotificationRequestedV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = NotificationTemplateCatalog.Find(request.MessageKey, request.Locale);
        return template is null
            ? null
            : new InAppNotificationContent(request.MessageKey, template.Subject, template.Body);
    }
}

public sealed class EmailNotificationContentResolver(
    DoSelectDbContext context,
    IGuestOrderAccessHasher guestOrderAccessHasher,
    TimeProvider timeProvider)
    : IEmailNotificationContentResolver
{
    public async Task<EmailNotificationContent?> ResolveAsync(
        EmailNotificationRequestedV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is
            {
                TemplateKey: GuestOrderAccessNotificationContract.TemplateKey,
                RecipientPurpose: GuestOrderAccessNotificationContract.RecipientPurpose,
                ResourceType: GuestOrderAccessNotificationContract.ResourceType,
            })
        {
            return await ResolveGuestOrderAccessAsync(request, cancellationToken);
        }

        var template = NotificationTemplateCatalog.Find(request.TemplateKey, request.Locale);
        if (template is null || !PurposeMatchesResource(request.RecipientPurpose, request.ResourceType))
        {
            return null;
        }

        var recipient = await ResolveRecipientAsync(
            request.RecipientPurpose,
            request.ResourceType,
            request.ResourcePublicId,
            cancellationToken);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Value.Email))
        {
            return null;
        }

        return new EmailNotificationContent(
            recipient.Value.UserId,
            new EmailMessage(recipient.Value.Email, template.Subject, template.Body));
    }

    private async Task<EmailNotificationContent?> ResolveGuestOrderAccessAsync(
        EmailNotificationRequestedV1 request,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var resource = await (
            from accessRequest in context.GuestOrderAccessRequests.AsNoTracking()
            join order in context.Orders.AsNoTracking() on accessRequest.OrderId equals order.Id
            where accessRequest.PublicId == request.ResourcePublicId &&
                  accessRequest.SendCount == request.ParameterSetVersion &&
                  accessRequest.CodeHash != null &&
                  accessRequest.ExpiresAtUtc > nowUtc &&
                  accessRequest.ConsumedAtUtc == null &&
                  accessRequest.LockedAtUtc == null &&
                  accessRequest.RevokedAtUtc == null
            select new
            {
                accessRequest.CodeHash,
                order.RecipientEmail,
                order.OrderNumber,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (resource is null || string.IsNullOrWhiteSpace(resource.RecipientEmail))
        {
            return null;
        }

        var code = guestOrderAccessHasher.DeriveVerificationCode(
            request.ResourcePublicId,
            request.ParameterSetVersion);
        var codeHash = guestOrderAccessHasher.HashCode(code);
        if (!CryptographicOperations.FixedTimeEquals(codeHash, resource.CodeHash!))
        {
            return null;
        }

        return new EmailNotificationContent(
            RecipientUserId: null,
            GuestOrderAccessEmailComposer.Compose(
                resource.RecipientEmail,
                resource.OrderNumber,
                code));
    }

    private async Task<(string? UserId, string Email)?> ResolveRecipientAsync(
        string recipientPurpose,
        string resourceType,
        Guid resourcePublicId,
        CancellationToken cancellationToken)
    {
        var orderRecipient = (recipientPurpose, resourceType) switch
        {
            (_, "Order") => context.Orders.AsNoTracking()
                .Where(order => order.PublicId == resourcePublicId)
                .Select(order => new { order.MemberUserId, order.GuestEmailNormalized }),
            (_, "PaymentAttempt") =>
                from payment in context.PaymentAttempts.AsNoTracking()
                join order in context.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            (_, "Shipment") =>
                from shipment in context.Shipments.AsNoTracking()
                join order in context.Orders.AsNoTracking() on shipment.OrderId equals order.Id
                where shipment.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            (_, "ReturnRequest") =>
                from returnRequest in context.ReturnRequests.AsNoTracking()
                join order in context.Orders.AsNoTracking() on returnRequest.OrderId equals order.Id
                where returnRequest.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            (_, "Refund") =>
                from refund in context.Refunds.AsNoTracking()
                join order in context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                where refund.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            (_, "SupportTicket") => context.SupportTickets.AsNoTracking()
                .Where(ticket => ticket.PublicId == resourcePublicId)
                .Select(ticket => new
                {
                    MemberUserId = (string?)ticket.MemberUserId,
                    GuestEmailNormalized = (string?)null,
                }),
            (AiBudgetAlertNotificationContract.RecipientPurpose,
                AiBudgetAlertNotificationContract.ResourceType) =>
                from user in context.Users.AsNoTracking()
                join profile in context.AdminProfiles.AsNoTracking()
                    on user.Id equals profile.UserId
                join userRole in context.UserRoles.AsNoTracking()
                    on user.Id equals userRole.UserId
                join role in context.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where user.PublicId == resourcePublicId &&
                    user.AccountType == AccountType.Admin &&
                    user.AccountStatus == AccountStatus.Active &&
                    profile.IsActive &&
                    role.Name == AuditRoleNames.SuperAdmin
                select new
                {
                    MemberUserId = (string?)user.Id,
                    GuestEmailNormalized = user.Email,
                },
            (SupportSlaNotificationContract.RecipientPurpose,
                SupportSlaNotificationContract.EmailRecipientResourceType) =>
                from user in context.Users.AsNoTracking()
                join profile in context.AdminProfiles.AsNoTracking()
                    on user.Id equals profile.UserId
                where user.PublicId == resourcePublicId &&
                    user.AccountType == AccountType.Admin &&
                    user.AccountStatus == AccountStatus.Active &&
                    profile.IsActive
                select new
                {
                    MemberUserId = (string?)user.Id,
                    GuestEmailNormalized = user.Email,
                },
            _ => null,
        };

        if (orderRecipient is null)
        {
            return null;
        }

        var resolved = await orderRecipient.FirstOrDefaultAsync(cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        if (resolved.MemberUserId is not null)
        {
            var email = await context.Users.AsNoTracking()
                .Where(user => user.Id == resolved.MemberUserId)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(email)
                ? null
                : (resolved.MemberUserId, email);
        }

        return string.IsNullOrWhiteSpace(resolved.GuestEmailNormalized)
            ? null
            : (null, resolved.GuestEmailNormalized);
    }

    private static bool PurposeMatchesResource(string purpose, string resourceType) =>
        (purpose, resourceType) switch
        {
            ("order.customer", "Order") => true,
            ("payment.customer", "PaymentAttempt") => true,
            ("shipment.customer", "Shipment") => true,
            ("return.customer", "ReturnRequest") => true,
            ("refund.customer", "Refund") => true,
            ("support.customer", "SupportTicket") => true,
            (AiBudgetAlertNotificationContract.RecipientPurpose,
                AiBudgetAlertNotificationContract.ResourceType) => true,
            (SupportSlaNotificationContract.RecipientPurpose,
                SupportSlaNotificationContract.EmailRecipientResourceType) => true,
            _ => false,
        };
}
