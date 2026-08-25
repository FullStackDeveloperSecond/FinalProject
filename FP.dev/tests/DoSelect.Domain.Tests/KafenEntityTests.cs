using System.Reflection;
using DoSelect.Domain.Reports;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Support;

namespace DoSelect.Domain.Tests;

public sealed class KafenEntityTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 8, 19, 3, 0, 0, DateTimeKind.Utc);
    public static TheoryData<Type> Types => new()
    {
        typeof(SupportTicket), typeof(SupportMessage), typeof(SupportAttachment), typeof(SupportAssignmentHistory), typeof(SupportStatusHistory), typeof(SupportSlaEvent), typeof(SupportSummary),
        typeof(ReturnRequest), typeof(ReturnItem), typeof(ReturnInspection), typeof(ReturnAttachment), typeof(ReturnAssignmentHistory), typeof(ReturnStatusHistory), typeof(ReturnShipment), typeof(ReturnShipmentEvent),
        typeof(ReportCase), typeof(ReportAttachment), typeof(ReportAssignmentHistory), typeof(ReportStatusHistory), typeof(CaseWorkbenchRow),
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void Entity_DoesNotExposePublicPropertySetters(Type type) => Assert.DoesNotContain(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property => property.SetMethod?.IsPublic == true);

    [Fact]
    public void SupportTicket_UsesConfirmedAssignmentAndStatusTransitions()
    {
        var ticket = new SupportTicket(Guid.NewGuid(), "CS-1", "member", null, SupportTicketCategory.Order, "訂單問題", CasePriority.Normal, CreatedAtUtc.AddHours(8), CreatedAtUtc.AddDays(3), CreatedAtUtc);
        ticket.Assign("agent", CreatedAtUtc.AddMinutes(1)); ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2)); ticket.Transition(SupportTicketStatus.Resolved, CreatedAtUtc.AddHours(1)); ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddHours(2));
        Assert.Equal(1, ticket.ReopenCount); Assert.Equal("agent", ticket.AssigneeAdminUserId);
    }

    [Fact]
    public void SupportMessage_RejectsInternalMemberMessage() => Assert.Throws<ArgumentException>(() => new SupportMessage(Guid.NewGuid(), 1, SupportSenderType.Member, "member", "private", true, false, null, "zh-TW", CreatedAtUtc));

    [Fact]
    public void ReturnRequest_ApprovalSeparatesShipmentAndRefundPaths()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-1", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);
        request.Transition(ReturnRequestStatus.UnderReview, CreatedAtUtc.AddMinutes(1)); request.Approve("manager", true, CreatedAtUtc.AddMinutes(2));
        Assert.Equal(ReturnRequestStatus.AwaitingShipment, request.Status); Assert.Equal(CreatedAtUtc.AddMinutes(2).AddDays(7), request.ReturnShipmentDueAtUtc);
    }

    [Fact]
    public void ReturnRequest_DefaultsToNormalPriorityAndAllowsStaffAdjustment()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-2", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);

        Assert.Equal(CasePriority.Normal, request.Priority);

        request.ChangePriority(CasePriority.High, CreatedAtUtc.AddMinutes(1));

        Assert.Equal(CasePriority.High, request.Priority);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), request.UpdatedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => request.ChangePriority((CasePriority)999, CreatedAtUtc.AddMinutes(2)));
        Assert.Equal(CasePriority.High, request.Priority);
    }

    [Fact]
    public void ReportOpenCaseKey_IsReleasedOnlyAfterClosure()
    {
        var report = new ReportCase(Guid.NewGuid(), "RP-1", "member", ReportTargetType.Product, Guid.NewGuid(), "Misleading", "內容不實", CasePriority.Normal, CreatedAtUtc);
        Assert.Equal(32, report.OpenCaseKeyHash?.Length); report.Assign("agent", CreatedAtUtc.AddMinutes(1)); report.Transition(ReportCaseStatus.InReview, CreatedAtUtc.AddMinutes(2)); report.Decide(false, "NoViolation", "證據不足", CreatedAtUtc.AddMinutes(3)); report.Transition(ReportCaseStatus.Closed, CreatedAtUtc.AddMinutes(4));
        Assert.Null(report.OpenCaseKeyHash);
    }

    [Fact]
    public void ReportOpenCaseKey_NormalizesCaseForSqlServerSemantics()
    {
        var target = Guid.NewGuid();
        var first = new ReportCase(Guid.NewGuid(), "RP-1", "Member-A", ReportTargetType.Product, target, "Misleading", "內容不實", CasePriority.Normal, CreatedAtUtc);
        var second = new ReportCase(Guid.NewGuid(), "RP-2", "member-a", ReportTargetType.Product, target, "misleading", "內容不實", CasePriority.Normal, CreatedAtUtc);

        Assert.Equal(first.OpenCaseKeyHash, second.OpenCaseKeyHash);
    }

    [Fact]
    public void PrivateAttachment_RejectsFilesOverTenMegabytes() => Assert.Throws<ArgumentOutOfRangeException>(() => new SupportAttachment(Guid.NewGuid(), 1, null, "member", "large.pdf", "private/large", "pdf", "application/pdf", 10_485_761, new byte[32], CreatedAtUtc));

    [Fact]
    public void SupportTicket_RecordActivity_TouchesLastActivityWithoutChangingStatus()
    {
        var ticket = new SupportTicket(Guid.NewGuid(), "CS-2", "member", null, SupportTicketCategory.Order, "訂單問題", CasePriority.Normal, CreatedAtUtc.AddHours(8), CreatedAtUtc.AddDays(3), CreatedAtUtc);
        ticket.Assign("agent", CreatedAtUtc.AddMinutes(1)); ticket.Transition(SupportTicketStatus.InProgress, CreatedAtUtc.AddMinutes(2));

        ticket.RecordActivity(CreatedAtUtc.AddMinutes(5));

        Assert.Equal(SupportTicketStatus.InProgress, ticket.Status);
        Assert.Equal(CreatedAtUtc.AddMinutes(5), ticket.LastActivityAtUtc);
        Assert.Equal(CreatedAtUtc.AddMinutes(5), ticket.UpdatedAtUtc);
    }

    [Fact]
    public void SupportTicket_RecordActivity_RejectsTerminalStatuses()
    {
        var ticket = new SupportTicket(Guid.NewGuid(), "CS-3", "member", null, SupportTicketCategory.Order, "訂單問題", CasePriority.Normal, CreatedAtUtc.AddHours(8), CreatedAtUtc.AddDays(3), CreatedAtUtc);
        ticket.Transition(SupportTicketStatus.Cancelled, CreatedAtUtc.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => ticket.RecordActivity(CreatedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void ReturnRequest_RejectCapturesReviewerFromUnderReviewOrInspecting()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-3", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);
        request.Transition(ReturnRequestStatus.UnderReview, CreatedAtUtc.AddMinutes(1));
        request.Reject("manager", CreatedAtUtc.AddMinutes(2));

        Assert.Equal(ReturnRequestStatus.Rejected, request.Status);
        Assert.Equal("manager", request.ReviewedByAdminUserId);
        Assert.Equal(CreatedAtUtc.AddMinutes(2), request.ClosedAtUtc);
    }

    [Fact]
    public void ReturnRequest_ShipmentDeadline_CanBeExtendedExactlyOnce()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-4", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);
        request.Transition(ReturnRequestStatus.UnderReview, CreatedAtUtc.AddMinutes(1));
        request.Approve("manager", requiresShipment: true, CreatedAtUtc.AddMinutes(2));
        var originalDue = request.ReturnShipmentDueAtUtc!.Value;

        Assert.False(request.HasShipmentDeadlineBeenExtended);

        request.ExtendShipmentDeadline(CreatedAtUtc.AddDays(1));

        Assert.Equal(originalDue.AddDays(7), request.ReturnShipmentDueAtUtc);
        Assert.True(request.HasShipmentDeadlineBeenExtended);
        Assert.Throws<InvalidOperationException>(() => request.ExtendShipmentDeadline(CreatedAtUtc.AddDays(2)));
    }

    [Fact]
    public void ReturnRequest_ShipmentDeadline_RejectsExtensionAfterExpiry()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-5", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);
        request.Transition(ReturnRequestStatus.UnderReview, CreatedAtUtc.AddMinutes(1));
        request.Approve("manager", requiresShipment: true, CreatedAtUtc.AddMinutes(2));
        var pastDue = request.ReturnShipmentDueAtUtc!.Value.AddDays(1);

        Assert.Throws<InvalidOperationException>(() => request.ExtendShipmentDeadline(pastDue));
    }

    [Fact]
    public void ReturnRequest_ShipmentDeadline_RejectsExtensionOutsideAwaitingShipment()
    {
        var request = new ReturnRequest(Guid.NewGuid(), "RT-6", 1, "member", "Defect", "商品故障", 1, CreatedAtUtc);
        request.Transition(ReturnRequestStatus.UnderReview, CreatedAtUtc.AddMinutes(1));
        request.Approve("manager", requiresShipment: false, CreatedAtUtc.AddMinutes(2));

        Assert.Equal(ReturnRequestStatus.AwaitingRefund, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.ExtendShipmentDeadline(CreatedAtUtc.AddMinutes(3)));
    }

    [Fact]
    public void ReturnShipment_HomePickup_RequiresRecipientSnapshot() =>
        Assert.Throws<ArgumentException>(() => new ReturnShipment(
            Guid.NewGuid(), 1, "RS-1", ReturnShipmentMethod.HomePickup,
            carrierCode: "black-cat", trackingNumber: null,
            recipientName: null, recipientPhone: null, postalCode: null, addressLine: null,
            storeCode: null, storeName: null, CreatedAtUtc));

    [Fact]
    public void ReturnShipment_ApplyEventStatus_RefusesChangeAfterTerminalState()
    {
        var shipment = new ReturnShipment(
            Guid.NewGuid(), 1, "RS-2", ReturnShipmentMethod.SelfShip,
            carrierCode: null, trackingNumber: null,
            recipientName: null, recipientPhone: null, postalCode: null, addressLine: null,
            storeCode: null, storeName: null, CreatedAtUtc);

        shipment.ApplyEventStatus(ReturnShipmentStatus.InTransit, CreatedAtUtc.AddHours(1));
        shipment.ApplyEventStatus(ReturnShipmentStatus.Delivered, CreatedAtUtc.AddHours(2));

        Assert.Equal(ReturnShipmentStatus.Delivered, shipment.Status);
        Assert.Equal(CreatedAtUtc.AddHours(2), shipment.ReceivedAtUtc);
        Assert.Throws<InvalidOperationException>(() => shipment.ApplyEventStatus(ReturnShipmentStatus.InTransit, CreatedAtUtc.AddHours(3)));
    }
}
