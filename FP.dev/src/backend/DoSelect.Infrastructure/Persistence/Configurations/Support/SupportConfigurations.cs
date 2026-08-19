using DoSelect.Domain.Orders;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Support;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ConfigureMutablePublicEntity("SupportTickets"); b.Property(x => x.TicketNumber).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.TicketNumber).IsUnique().HasDatabaseName("UX_SupportTickets_TicketNumber");
        b.Property(x => x.MemberUserId).HasMaxLength(450).IsRequired(); Enum(b.Property(x => x.Category), 32); b.Property(x => x.Subject).HasMaxLength(200).IsRequired(); Enum(b.Property(x => x.Status), 32); Enum(b.Property(x => x.Priority), 16);
        b.Property(x => x.AssigneeAdminUserId).HasMaxLength(450); Time(b.Property(x => x.FirstResponseDueAtUtc)); Time(b.Property(x => x.ResolutionDueAtUtc)); Time(b.Property(x => x.FirstHumanResponseAtUtc)); Time(b.Property(x => x.WaitingForCustomerStartedAtUtc)); Time(b.Property(x => x.ResolvedAtUtc)); Time(b.Property(x => x.ClosedAtUtc)); Time(b.Property(x => x.LastActivityAtUtc));
        b.Property(x => x.PausedSeconds).HasDefaultValue(0).IsRequired(); b.Property(x => x.ReopenCount).HasDefaultValue(0).IsRequired();
        b.HasIndex(x => new { x.Status, x.AssigneeAdminUserId, x.LastActivityAtUtc }).HasDatabaseName("IX_SupportTickets_Status_AssigneeAdminUserId_LastActivityAtUtc"); b.HasIndex(x => new { x.Status, x.FirstResponseDueAtUtc }).HasDatabaseName("IX_SupportTickets_Status_FirstResponseDueAtUtc"); b.HasIndex(x => new { x.Status, x.ResolutionDueAtUtc }).HasDatabaseName("IX_SupportTickets_Status_ResolutionDueAtUtc"); b.HasIndex(x => new { x.MemberUserId, x.CreatedAtUtc }).HasDatabaseName("IX_SupportTickets_MemberUserId_CreatedAtUtc");
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.MemberUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssigneeAdminUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("SupportTickets", t => { t.HasCheckConstraint("CK_SupportTickets_PausedSeconds", "[PausedSeconds] >= 0 AND [PausedSeconds] <= 259200"); t.HasCheckConstraint("CK_SupportTickets_ReopenCount", "[ReopenCount] >= 0"); });
    }
    private static void Enum<T>(PropertyBuilder<T> p, int length) where T : struct, Enum => p.HasConversion<string>().HasMaxLength(length).IsUnicode(false).IsRequired();
    private static void Time(PropertyBuilder<DateTime> p) => p.HasPrecision(3).IsRequired(); private static void Time(PropertyBuilder<DateTime?> p) => p.HasPrecision(3);
}

public sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> b)
    {
        b.ConfigurePublicEntity("SupportMessages"); b.Property(x => x.SenderType).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired(); b.Property(x => x.SenderUserId).HasMaxLength(450); b.Property(x => x.Body).HasMaxLength(4000).IsRequired(); b.Property(x => x.IsInternal).HasDefaultValue(false).IsRequired(); b.Property(x => x.AiGenerated).HasDefaultValue(false).IsRequired(); b.Property(x => x.Language).HasMaxLength(10).IsUnicode(false).HasDefaultValue("zh-TW").IsRequired(); b.Property(x => x.SentAtUtc).HasPrecision(3).IsRequired();
        b.HasIndex(x => new { x.SupportTicketId, x.SentAtUtc, x.Id }).HasDatabaseName("IX_SupportMessages_SupportTicketId_SentAtUtc_Id"); b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<SupportMessage>().WithMany().HasForeignKey(x => x.ReplyToMessageId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("SupportMessages", t => t.HasCheckConstraint("CK_SupportMessages_InternalSender", "[IsInternal] = 0 OR [SenderType] = 'Admin'"));
    }
}

public sealed class SupportAssignmentHistoryConfiguration : IEntityTypeConfiguration<SupportAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<SupportAssignmentHistory> b)
    {
        Plain(b, "SupportAssignmentHistories"); b.Property(x => x.FromAdminUserId).HasMaxLength(450); b.Property(x => x.ToAdminUserId).HasMaxLength(450); b.Property(x => x.Action).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.SupportTicketId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_SupportAssignmentHistories_SupportTicketId_OccurredAtUtc_Id");
        b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict); User(b, nameof(SupportAssignmentHistory.FromAdminUserId)); User(b, nameof(SupportAssignmentHistory.ToAdminUserId)); User(b, nameof(SupportAssignmentHistory.ActorUserId));
    }
    private static void Plain(EntityTypeBuilder<SupportAssignmentHistory> b, string table) { b.ToTable(table); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); }
    private static void User(EntityTypeBuilder<SupportAssignmentHistory> b, string fk) => b.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
}

public sealed class SupportStatusHistoryConfiguration : IEntityTypeConfiguration<SupportStatusHistory>
{
    public void Configure(EntityTypeBuilder<SupportStatusHistory> b)
    {
        b.ToTable("SupportStatusHistories"); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32).IsUnicode(false); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired(); b.Property(x => x.ReasonCode).HasMaxLength(64).IsUnicode(false); b.Property(x => x.Note).HasMaxLength(500); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.SupportTicketId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_SupportStatusHistories_SupportTicketId_OccurredAtUtc_Id"); b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupportSlaEventConfiguration : IEntityTypeConfiguration<SupportSlaEvent>
{
    public void Configure(EntityTypeBuilder<SupportSlaEvent> b)
    {
        b.ToTable("SupportSlaEvents", t => { t.HasCheckConstraint("CK_SupportSlaEvents_Duration", "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0"); t.HasCheckConstraint("CK_SupportSlaEvents_MetadataJson", "[MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1"); }); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired(); b.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.DueAtUtc).HasPrecision(3); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.Property(x => x.MetadataJson).HasMaxLength(2000); b.HasIndex(x => new { x.SupportTicketId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_SupportSlaEvents_SupportTicketId_OccurredAtUtc_Id"); b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupportSummaryConfiguration : IEntityTypeConfiguration<SupportSummary>
{
    public void Configure(EntityTypeBuilder<SupportSummary> b)
    {
        b.ConfigureMutablePublicEntity("SupportSummaries"); b.Property(x => x.Summary).HasMaxLength(1500).IsRequired(); b.Property(x => x.Model).HasMaxLength(100).IsRequired(); b.Property(x => x.PromptVersion).HasMaxLength(64).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsUnicode(false).HasDefaultValue(SupportSummaryStatus.Pending).IsRequired(); b.Property(x => x.GeneratedAtUtc).HasPrecision(3); b.HasIndex(x => new { x.SupportTicketId, x.SourceLastMessageId }).IsUnique().HasDatabaseName("UX_SupportSummaries_SupportTicketId_SourceLastMessageId"); b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict); b.HasOne<SupportMessage>().WithMany().HasForeignKey(x => x.SourceLastMessageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupportAttachmentConfiguration : IEntityTypeConfiguration<SupportAttachment>
{
    public void Configure(EntityTypeBuilder<SupportAttachment> b)
    {
        b.ConfigureMutablePublicEntity("SupportAttachments"); ConfigureAttachment(b); b.HasIndex(x => x.SupportTicketId).HasDatabaseName("IX_SupportAttachments_SupportTicketId"); b.HasIndex(x => x.SupportMessageId).HasDatabaseName("IX_SupportAttachments_SupportMessageId"); b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.SupportTicketId).OnDelete(DeleteBehavior.Restrict); b.HasOne<SupportMessage>().WithMany().HasForeignKey(x => x.SupportMessageId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("SupportAttachments", t => t.HasCheckConstraint("CK_SupportAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760"));
    }
    private static void ConfigureAttachment(EntityTypeBuilder<SupportAttachment> b)
    { b.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired(); b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired(); b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired(); b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("UX_SupportAttachments_StorageKey"); b.Property(x => x.Extension).HasMaxLength(10).IsUnicode(false).IsRequired(); b.Property(x => x.MimeType).HasMaxLength(100).IsUnicode(false).IsRequired(); b.Property(x => x.Sha256).HasColumnType("binary(32)").IsRequired(); b.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired(); b.Property(x => x.ScannedAtUtc).HasPrecision(3); b.Property(x => x.RetentionUntilUtc).HasPrecision(3); b.Property(x => x.LegalHold).HasDefaultValue(false).IsRequired(); b.Property(x => x.DeletedAtUtc).HasPrecision(3); }
}
