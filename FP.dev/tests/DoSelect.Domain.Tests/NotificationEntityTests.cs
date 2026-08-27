using DoSelect.Domain.Notifications;

namespace DoSelect.Domain.Tests;

public sealed class NotificationEntityTests
{
    private static readonly DateTime Now =
        new(2026, 8, 27, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Notification_RequiresResourceTypeAndPublicIdTogether()
    {
        Assert.Throws<ArgumentException>(() => new Notification(
            Guid.CreateVersion7(),
            "member-id",
            "order.created",
            "title",
            "body",
            "Order",
            null,
            null,
            Now));
    }

    [Fact]
    public void MarkRead_IsIdempotentAndKeepsTheFirstTimestamp()
    {
        var notification = new Notification(
            Guid.CreateVersion7(),
            "member-id",
            "order.created",
            "title",
            "body",
            "Order",
            Guid.CreateVersion7(),
            null,
            Now);

        notification.MarkRead(Now.AddMinutes(1));
        notification.MarkRead(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), notification.ReadAtUtc);
    }

    [Fact]
    public void EmailDelivery_UsesNamedStateTransitionsAndCountsAttempts()
    {
        var delivery = new EmailDelivery(
            Guid.CreateVersion7(),
            null,
            "customer@example.test",
            "order.created",
            1,
            "order.customer",
            Now);

        delivery.BeginAttempt(Now);
        delivery.ScheduleRetry(
            "email_transport_unavailable",
            Now.AddMinutes(1),
            Now);
        delivery.BeginAttempt(Now.AddMinutes(1));
        delivery.MarkSent("provider-message-id", Now.AddMinutes(1));

        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(EmailDeliveryStatus.Sent, delivery.Status);
        Assert.Equal("provider-message-id", delivery.ProviderMessageId);
        Assert.Null(delivery.NextAttemptAtUtc);
    }
}
