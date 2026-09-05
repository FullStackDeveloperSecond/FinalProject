using DoSelect.Application.Outbox;

namespace DoSelect.Application.Tests.Outbox;

public sealed class OutboxWriteRequestTests
{
    [Fact]
    public void SimulatedInvoiceRequest_UsesTheRegisteredVersionedEventType()
    {
        var orderPublicId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);

        var request = OutboxWriteRequest.Create(
            Guid.NewGuid(),
            "Order",
            orderPublicId,
            new SimulatedInvoiceRequestedV1(orderPublicId),
            nowUtc,
            nowUtc,
            "correlation-invoice-test");

        Assert.Equal(OutboxEventTypes.SimulatedInvoiceRequestedV1, request.Type);
        Assert.Equal(1, request.PayloadVersion);
    }

    private static readonly DateTime Now =
        new(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmailFactory_UsesTheApprovedV1Contract()
    {
        var notificationPublicId = Guid.CreateVersion7();
        var orderPublicId = Guid.CreateVersion7();

        var request = OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Order",
            orderPublicId,
            new EmailNotificationRequestedV1(
                notificationPublicId,
                "order.created",
                "order.customer",
                "Order",
                orderPublicId,
                "zh-TW",
                1),
            Now,
            Now,
            "correlation-order-created");

        Assert.Equal(OutboxEventTypes.EmailNotificationRequestedV1, request.Type);
        Assert.Equal(1, request.PayloadVersion);
        Assert.Equal("Order", request.AggregateType);
        Assert.Same(request.Payload, Assert.IsType<EmailNotificationRequestedV1>(request.Payload));
    }

    [Fact]
    public void Factory_RejectsInvalidPayloadIdentityAndNonUtcTime()
    {
        var resourcePublicId = Guid.CreateVersion7();
        Assert.Throws<ArgumentException>(() => OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Order",
            resourcePublicId,
            new EmailNotificationRequestedV1(
                Guid.Empty,
                "order.created",
                "order.customer",
                "Order",
                resourcePublicId,
                "zh-TW",
                1),
            Now,
            Now,
            "correlation-order-created"));

        Assert.Throws<ArgumentException>(() => OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "InventoryBalance",
            resourcePublicId,
            new InventoryReconciliationMismatchDetectedV1(
                Guid.CreateVersion7(),
                resourcePublicId,
                3,
                2,
                DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)),
            Now,
            Now,
            "correlation-inventory"));
    }

    [Theory]
    [InlineData("customer@example.com")]
    [InlineData("order created")]
    [InlineData("order/created")]
    public void Factory_RejectsFreeFormTemplateIdentifiers(string templateKey)
    {
        var resourcePublicId = Guid.CreateVersion7();
        Assert.Throws<ArgumentException>(() => OutboxWriteRequest.Create(
            Guid.CreateVersion7(),
            "Order",
            resourcePublicId,
            new EmailNotificationRequestedV1(
                Guid.CreateVersion7(),
                templateKey,
                "order.customer",
                "Order",
                resourcePublicId,
                "zh-TW",
                1),
            Now,
            Now,
            "correlation-order-created"));
    }
}
