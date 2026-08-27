using DoSelect.Application.Notifications;
using DoSelect.Application.Outbox;
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
            ["order.cancelled"] = Localized("訂單已取消", "您的訂單已取消。", "注文をキャンセルしました", "注文はキャンセルされました。", "주문이 취소되었습니다", "주문이 취소되었습니다."),
            ["shipment.updated"] = Localized("物流狀態已更新", "您的商品物流狀態已更新。", "配送状況が更新されました", "商品の配送状況が更新されました。", "배송 상태가 업데이트되었습니다", "상품 배송 상태가 업데이트되었습니다."),
            ["return.updated"] = Localized("退貨狀態已更新", "您的退貨申請狀態已更新。", "返品状況が更新されました", "返品申請の状態が更新されました。", "반품 상태가 업데이트되었습니다", "반품 신청 상태가 업데이트되었습니다."),
            ["refund.succeeded"] = Localized("退款完成", "您的退款已完成。", "返金が完了しました", "返金処理が完了しました。", "환불이 완료되었습니다", "환불 처리가 완료되었습니다."),
            ["refund.failed"] = Localized("退款處理未完成", "退款處理未完成，請聯絡客服。", "返金処理を完了できませんでした", "カスタマーサポートへお問い合わせください。", "환불 처리가 완료되지 않았습니다", "고객센터에 문의해 주세요."),
            ["support.replied"] = Localized("客服已回覆", "您的客服案件已有新回覆。", "サポートから返信がありました", "お問い合わせに新しい返信があります。", "고객센터 답변이 등록되었습니다", "문의에 새로운 답변이 등록되었습니다."),
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

public sealed class EmailNotificationContentResolver(DoSelectDbContext context)
    : IEmailNotificationContentResolver
{
    public async Task<EmailNotificationContent?> ResolveAsync(
        EmailNotificationRequestedV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = NotificationTemplateCatalog.Find(request.TemplateKey, request.Locale);
        if (template is null || !PurposeMatchesResource(request.RecipientPurpose, request.ResourceType))
        {
            return null;
        }

        var recipient = await ResolveRecipientAsync(
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

    private async Task<(string? UserId, string Email)?> ResolveRecipientAsync(
        string resourceType,
        Guid resourcePublicId,
        CancellationToken cancellationToken)
    {
        var orderRecipient = resourceType switch
        {
            "Order" => context.Orders.AsNoTracking()
                .Where(order => order.PublicId == resourcePublicId)
                .Select(order => new { order.MemberUserId, order.GuestEmailNormalized }),
            "PaymentAttempt" =>
                from payment in context.PaymentAttempts.AsNoTracking()
                join order in context.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            "Shipment" =>
                from shipment in context.Shipments.AsNoTracking()
                join order in context.Orders.AsNoTracking() on shipment.OrderId equals order.Id
                where shipment.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            "ReturnRequest" =>
                from returnRequest in context.ReturnRequests.AsNoTracking()
                join order in context.Orders.AsNoTracking() on returnRequest.OrderId equals order.Id
                where returnRequest.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            "Refund" =>
                from refund in context.Refunds.AsNoTracking()
                join order in context.Orders.AsNoTracking() on refund.OrderId equals order.Id
                where refund.PublicId == resourcePublicId
                select new { order.MemberUserId, order.GuestEmailNormalized },
            "SupportTicket" => context.SupportTickets.AsNoTracking()
                .Where(ticket => ticket.PublicId == resourcePublicId)
                .Select(ticket => new
                {
                    MemberUserId = (string?)ticket.MemberUserId,
                    GuestEmailNormalized = (string?)null,
                }),
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
            _ => false,
        };
}
