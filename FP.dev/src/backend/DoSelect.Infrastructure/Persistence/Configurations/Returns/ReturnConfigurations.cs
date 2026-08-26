using DoSelect.Domain.Orders;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Returns;

public sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> b)
    {
        b.ConfigureMutablePublicEntity("ReturnRequests"); b.Property(x => x.ReturnNumber).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.ReturnNumber).IsUnique().HasDatabaseName("UX_ReturnRequests_ReturnNumber"); b.Property(x => x.RequesterUserId).HasMaxLength(450); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16).IsUnicode(false).HasDefaultValue(CasePriority.Normal).IsRequired(); b.Property(x => x.ReasonCode).HasMaxLength(64).IsUnicode(false).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000).IsRequired(); b.Property(x => x.AssigneeAdminUserId).HasMaxLength(450); b.Property(x => x.ReviewedByAdminUserId).HasMaxLength(450); b.Property(x => x.RequestedAtUtc).HasPrecision(3); b.Property(x => x.ApprovedAtUtc).HasPrecision(3); b.Property(x => x.ReceivedAtUtc).HasPrecision(3); b.Property(x => x.ClosedAtUtc).HasPrecision(3); b.Property(x => x.ReturnShipmentDueAtUtc).HasPrecision(3);
        b.HasIndex(x => new { x.OrderId, x.Status }).HasDatabaseName("IX_ReturnRequests_OrderId_Status"); b.HasIndex(x => new { x.Status, x.Priority, x.AssigneeAdminUserId, x.UpdatedAtUtc }).HasDatabaseName("IX_ReturnRequests_Status_Priority_AssigneeAdminUserId_UpdatedAtUtc"); b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict); User(b, nameof(ReturnRequest.RequesterUserId)); User(b, nameof(ReturnRequest.AssigneeAdminUserId)); User(b, nameof(ReturnRequest.ReviewedByAdminUserId)); b.ToTable("ReturnRequests", t => t.HasCheckConstraint("CK_ReturnRequests_PolicyVersion", "[PolicyVersion] > 0"));
    }
    private static void User(EntityTypeBuilder<ReturnRequest> b, string fk) => b.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
}

public sealed class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> b)
    {
        b.ConfigurePublicEntity("ReturnItems"); b.Property(x => x.RequestedRefund).HasPrecision(18, 2).IsRequired(); b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.InspectionStatus).HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.RestockDisposition).HasConversion<string>().HasMaxLength(24).IsUnicode(false); b.HasIndex(x => new { x.ReturnRequestId, x.OrderItemId }).IsUnique().HasDatabaseName("UX_ReturnItems_ReturnRequestId_OrderItemId"); b.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict); b.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict); b.ToTable("ReturnItems", t => { t.HasCheckConstraint("CK_ReturnItems_Quantity", "[Quantity] > 0"); t.HasCheckConstraint("CK_ReturnItems_RequestedRefund", "[RequestedRefund] >= 0"); });
    }
}

public sealed class ReturnInspectionConfiguration : IEntityTypeConfiguration<ReturnInspection>
{
    public void Configure(EntityTypeBuilder<ReturnInspection> b)
    {
        b.ConfigurePublicEntity("ReturnInspections"); b.Property(x => x.Result).HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.ConditionCode).HasMaxLength(64).IsUnicode(false).IsRequired(); b.Property(x => x.Note).HasMaxLength(1000); b.Property(x => x.InspectedByAdminUserId).HasMaxLength(450).IsRequired(); b.Property(x => x.InspectedAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.ReturnItemId, x.InspectedAtUtc }).HasDatabaseName("IX_ReturnInspections_ReturnItemId_InspectedAtUtc"); b.HasOne<ReturnItem>().WithMany().HasForeignKey(x => x.ReturnItemId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InspectedByAdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReturnAttachmentConfiguration : IEntityTypeConfiguration<ReturnAttachment>
{
    public void Configure(EntityTypeBuilder<ReturnAttachment> b)
    {
        b.ConfigureMutablePublicEntity("ReturnAttachments"); b.Property(x => x.UploadedByUserId).HasMaxLength(450); b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired(); b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired(); b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("UX_ReturnAttachments_StorageKey"); b.Property(x => x.Extension).HasMaxLength(10).IsUnicode(false).IsRequired(); b.Property(x => x.MimeType).HasMaxLength(100).IsUnicode(false).IsRequired(); b.Property(x => x.Sha256).HasColumnType("binary(32)").IsRequired(); b.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired(); b.Property(x => x.ScannedAtUtc).HasPrecision(3); b.Property(x => x.RetentionUntilUtc).HasPrecision(3); b.Property(x => x.LegalHold).HasDefaultValue(false).IsRequired(); b.Property(x => x.DeletedAtUtc).HasPrecision(3); b.HasIndex(x => x.ReturnRequestId).HasDatabaseName("IX_ReturnAttachments_ReturnRequestId"); b.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Order>().WithMany().HasForeignKey(x => x.UploadedByGuestOrderId).OnDelete(DeleteBehavior.Restrict); b.ToTable("ReturnAttachments", t => { t.HasCheckConstraint("CK_ReturnAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760"); t.HasCheckConstraint("CK_ReturnAttachments_UploaderIdentity", "([UploadedByUserId] IS NOT NULL AND [UploadedByGuestOrderId] IS NULL) OR ([UploadedByUserId] IS NULL AND [UploadedByGuestOrderId] IS NOT NULL)"); });
    }
}

public sealed class ReturnAssignmentHistoryConfiguration : IEntityTypeConfiguration<ReturnAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<ReturnAssignmentHistory> b)
    {
        Plain(b, "ReturnAssignmentHistories"); b.Property(x => x.FromAdminUserId).HasMaxLength(450); b.Property(x => x.ToAdminUserId).HasMaxLength(450); b.Property(x => x.Action).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.ReturnRequestId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_ReturnAssignmentHistories_ReturnRequestId_OccurredAtUtc_Id"); b.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict); User(b, nameof(ReturnAssignmentHistory.FromAdminUserId)); User(b, nameof(ReturnAssignmentHistory.ToAdminUserId)); User(b, nameof(ReturnAssignmentHistory.ActorUserId));
    }
    private static void Plain(EntityTypeBuilder<ReturnAssignmentHistory> b, string table) { b.ToTable(table); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); }
    private static void User(EntityTypeBuilder<ReturnAssignmentHistory> b, string fk) => b.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
}

public sealed class ReturnStatusHistoryConfiguration : IEntityTypeConfiguration<ReturnStatusHistory>
{
    public void Configure(EntityTypeBuilder<ReturnStatusHistory> b)
    {
        b.ToTable("ReturnStatusHistories"); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24).IsUnicode(false); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.ReasonCode).HasMaxLength(64).IsUnicode(false); b.Property(x => x.Note).HasMaxLength(500); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.ReturnRequestId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_ReturnStatusHistories_ReturnRequestId_OccurredAtUtc_Id"); b.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> b)
    {
        b.ConfigureMutablePublicEntity("ReturnShipments"); b.Property(x => x.ShipmentNumber).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.ShipmentNumber).IsUnique().HasDatabaseName("UX_ReturnShipments_ShipmentNumber"); b.HasIndex(x => x.ReturnRequestId).IsUnique().HasDatabaseName("UX_ReturnShipments_ReturnRequestId"); b.Property(x => x.Method).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.CarrierCode).HasMaxLength(32).IsUnicode(false); b.Property(x => x.TrackingNumber).HasMaxLength(64); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.RecipientName).HasMaxLength(160); b.Property(x => x.RecipientPhone).HasMaxLength(32); b.Property(x => x.PostalCode).HasMaxLength(16); b.Property(x => x.AddressLine).HasMaxLength(500); b.Property(x => x.StoreCode).HasMaxLength(160); b.Property(x => x.StoreName).HasMaxLength(160); b.Property(x => x.ScheduledPickupAtUtc).HasPrecision(3); b.Property(x => x.ShippedAtUtc).HasPrecision(3); b.Property(x => x.ReceivedAtUtc).HasPrecision(3); b.HasIndex(x => new { x.CarrierCode, x.TrackingNumber }).HasDatabaseName("IX_ReturnShipments_CarrierCode_TrackingNumber"); b.HasOne<ReturnRequest>().WithOne().HasForeignKey<ReturnShipment>(x => x.ReturnRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReturnShipmentEventConfiguration : IEntityTypeConfiguration<ReturnShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ReturnShipmentEvent> b)
    {
        b.ToTable("ReturnShipmentEvents", t => t.HasCheckConstraint("CK_ReturnShipmentEvents_PayloadJson", "[PayloadSummaryJson] IS NULL OR ISJSON([PayloadSummaryJson]) = 1")); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.ExternalEventId).HasMaxLength(128).IsRequired(); b.Property(x => x.Source).HasMaxLength(32).IsUnicode(false).IsRequired(); b.Property(x => x.EventType).HasMaxLength(64).IsUnicode(false).IsRequired(); b.Property(x => x.EventCode).HasMaxLength(64).IsUnicode(false); b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.Property(x => x.ReceivedAtUtc).HasPrecision(3).IsRequired(); b.Property(x => x.PayloadHash).HasColumnType("binary(32)"); b.Property(x => x.PayloadSummaryJson).HasMaxLength(2000); b.HasIndex(x => new { x.Source, x.ExternalEventId }).IsUnique().HasDatabaseName("UX_ReturnShipmentEvents_Source_ExternalEventId"); b.HasIndex(x => new { x.ReturnShipmentId, x.OccurredAtUtc }).HasDatabaseName("IX_ReturnShipmentEvents_ReturnShipmentId_OccurredAtUtc"); b.HasOne<ReturnShipment>().WithMany().HasForeignKey(x => x.ReturnShipmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
