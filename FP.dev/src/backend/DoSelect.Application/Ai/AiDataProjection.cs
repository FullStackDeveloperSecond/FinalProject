namespace DoSelect.Application.Ai;

public sealed record AiOrderItemSource(
    string ProductName,
    int Quantity);

public sealed record AiOrderItemPayload(
    string ProductName,
    int Quantity);

public sealed record AiOrderSummarySource(
    string OwnerMemberId,
    string OrderNumber,
    string OrderStatus,
    string PaymentStatus,
    string ShippingStatus,
    string CustomerName,
    string Email,
    string Phone,
    string ShippingAddress,
    IReadOnlyList<AiOrderItemSource> Items,
    string ProcessHint);

public sealed record AiOrderSummaryPayload(
    string OrderNumber,
    string OrderStatus,
    string PaymentStatus,
    string ShippingStatus,
    IReadOnlyList<AiOrderItemPayload> Items,
    string ProcessHint);

public sealed record AiOrderSummaryProjection(
    AiProjectionStatus Status,
    AiOrderSummaryPayload? Payload,
    AiSafetyReason Reason);

public static class AiOrderSummaryProjector
{
    public static AiOrderSummaryProjection Project(
        string trustedMemberId,
        AiOrderSummarySource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedMemberId);
        ArgumentNullException.ThrowIfNull(source);

        if (!string.Equals(
            trustedMemberId,
            source.OwnerMemberId,
            StringComparison.Ordinal))
        {
            return new AiOrderSummaryProjection(
                AiProjectionStatus.Forbidden,
                Payload: null,
                AiSafetyReason.ResourceOwnershipMismatch);
        }

        var items = source.Items
            .Select(item => new AiOrderItemPayload(item.ProductName, item.Quantity))
            .ToArray();
        var outboundContent = items.Select(item => item.ProductName)
            .Append(source.ProcessHint)
            .ToArray();
        var inspection = AiOutboundContentGuard.Inspect(outboundContent);
        if (!inspection.IsAllowed)
        {
            return new AiOrderSummaryProjection(
                AiProjectionStatus.UnsafeContent,
                Payload: null,
                inspection.Reason);
        }

        var payload = new AiOrderSummaryPayload(
            source.OrderNumber,
            source.OrderStatus,
            source.PaymentStatus,
            source.ShippingStatus,
            items,
            source.ProcessHint);

        return new AiOrderSummaryProjection(
            AiProjectionStatus.Allowed,
            payload,
            AiSafetyReason.None);
    }
}

public sealed record AiSupportHistorySource(
    string OwnerMemberId,
    string TicketPublicId,
    IReadOnlyList<string> Messages);

public sealed record AiSupportHistoryPayload(
    string TicketPublicId,
    IReadOnlyList<string> Messages);

public sealed record AiSupportHistoryProjection(
    AiProjectionStatus Status,
    AiSupportHistoryPayload? Payload,
    AiSafetyReason Reason);

public static class AiSupportHistoryProjector
{
    public static AiSupportHistoryProjection Project(
        string trustedMemberId,
        AiSupportHistorySource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedMemberId);
        ArgumentNullException.ThrowIfNull(source);

        if (!string.Equals(
            trustedMemberId,
            source.OwnerMemberId,
            StringComparison.Ordinal))
        {
            return new AiSupportHistoryProjection(
                AiProjectionStatus.Forbidden,
                Payload: null,
                AiSafetyReason.ResourceOwnershipMismatch);
        }

        var messages = source.Messages.ToArray();
        var inspection = AiOutboundContentGuard.Inspect(messages);
        if (!inspection.IsAllowed)
        {
            return new AiSupportHistoryProjection(
                AiProjectionStatus.UnsafeContent,
                Payload: null,
                inspection.Reason);
        }

        return new AiSupportHistoryProjection(
            AiProjectionStatus.Allowed,
            new AiSupportHistoryPayload(source.TicketPublicId, messages),
            AiSafetyReason.None);
    }
}
