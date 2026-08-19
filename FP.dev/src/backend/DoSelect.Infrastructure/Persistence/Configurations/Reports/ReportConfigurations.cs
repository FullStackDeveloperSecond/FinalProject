using DoSelect.Domain.Reports;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Reports;

public sealed class ReportCaseConfiguration : IEntityTypeConfiguration<ReportCase>
{
    public void Configure(EntityTypeBuilder<ReportCase> b)
    {
        b.ConfigureMutablePublicEntity("ReportCases"); b.Property(x => x.ReportNumber).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.ReportNumber).IsUnique().HasDatabaseName("UX_ReportCases_ReportNumber"); b.Property(x => x.ReporterUserId).HasMaxLength(450).IsRequired(); b.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired(); b.Property(x => x.ReasonCode).HasMaxLength(64).IsUnicode(false).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16).IsUnicode(false).IsRequired(); b.Property(x => x.AssigneeAdminUserId).HasMaxLength(450); b.Property(x => x.ResolutionCode).HasMaxLength(64).IsUnicode(false); b.Property(x => x.DecisionNote).HasMaxLength(1000); b.Property(x => x.ResolvedAtUtc).HasPrecision(3); b.Property(x => x.ClosedAtUtc).HasPrecision(3); b.Property(x => x.LastActivityAtUtc).HasPrecision(3).IsRequired(); b.Property(x => x.OpenCaseKeyHash).HasColumnType("binary(32)");
        b.HasIndex(x => new { x.Status, x.AssigneeAdminUserId, x.LastActivityAtUtc }).HasDatabaseName("IX_ReportCases_Status_AssigneeAdminUserId_LastActivityAtUtc"); b.HasIndex(x => new { x.TargetType, x.TargetPublicId, x.Status }).HasDatabaseName("IX_ReportCases_TargetType_TargetPublicId_Status"); b.HasIndex(x => x.OpenCaseKeyHash).IsUnique().HasFilter("[OpenCaseKeyHash] IS NOT NULL").HasDatabaseName("UX_ReportCases_OpenCaseKeyHash");
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssigneeAdminUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReportAttachmentConfiguration : IEntityTypeConfiguration<ReportAttachment>
{
    public void Configure(EntityTypeBuilder<ReportAttachment> b)
    {
        b.ConfigureMutablePublicEntity("ReportAttachments"); b.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired(); b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired(); b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired(); b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("UX_ReportAttachments_StorageKey"); b.Property(x => x.Extension).HasMaxLength(10).IsUnicode(false).IsRequired(); b.Property(x => x.MimeType).HasMaxLength(100).IsUnicode(false).IsRequired(); b.Property(x => x.Sha256).HasColumnType("binary(32)").IsRequired(); b.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired(); b.Property(x => x.ScannedAtUtc).HasPrecision(3); b.Property(x => x.RetentionUntilUtc).HasPrecision(3); b.Property(x => x.LegalHold).HasDefaultValue(false).IsRequired(); b.Property(x => x.DeletedAtUtc).HasPrecision(3); b.HasIndex(x => x.ReportCaseId).HasDatabaseName("IX_ReportAttachments_ReportCaseId"); b.HasOne<ReportCase>().WithMany().HasForeignKey(x => x.ReportCaseId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict); b.ToTable("ReportAttachments", t => t.HasCheckConstraint("CK_ReportAttachments_FileSize", "[FileSizeBytes] >= 1 AND [FileSizeBytes] <= 10485760"));
    }
}

public sealed class ReportAssignmentHistoryConfiguration : IEntityTypeConfiguration<ReportAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<ReportAssignmentHistory> b)
    {
        b.ToTable("ReportAssignmentHistories"); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.FromAdminUserId).HasMaxLength(450); b.Property(x => x.ToAdminUserId).HasMaxLength(450); b.Property(x => x.Action).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.ReportCaseId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_ReportAssignmentHistories_ReportCaseId_OccurredAtUtc_Id"); b.HasOne<ReportCase>().WithMany().HasForeignKey(x => x.ReportCaseId).OnDelete(DeleteBehavior.Restrict); User(b, nameof(ReportAssignmentHistory.FromAdminUserId)); User(b, nameof(ReportAssignmentHistory.ToAdminUserId)); User(b, nameof(ReportAssignmentHistory.ActorUserId));
    }
    private static void User(EntityTypeBuilder<ReportAssignmentHistory> b, string fk) => b.HasOne<ApplicationUser>().WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);
}

public sealed class ReportStatusHistoryConfiguration : IEntityTypeConfiguration<ReportStatusHistory>
{
    public void Configure(EntityTypeBuilder<ReportStatusHistory> b)
    {
        b.ToTable("ReportStatusHistories"); b.HasKey(x => x.Id); b.Property(x => x.Id).UseIdentityColumn(); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24).IsUnicode(false); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.ActionCode).HasMaxLength(64).IsUnicode(false).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.ActorUserId).HasMaxLength(450); b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired(); b.HasIndex(x => new { x.ReportCaseId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_ReportStatusHistories_ReportCaseId_OccurredAtUtc_Id"); b.HasOne<ReportCase>().WithMany().HasForeignKey(x => x.ReportCaseId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CaseWorkbenchRowConfiguration : IEntityTypeConfiguration<CaseWorkbenchRow>
{
    public void Configure(EntityTypeBuilder<CaseWorkbenchRow> b)
    {
        b.HasNoKey(); b.ToView("vw_CaseWorkbench"); b.Property(x => x.CaseType).HasMaxLength(16).IsUnicode(false); b.Property(x => x.CaseNumber).HasMaxLength(32); b.Property(x => x.Title).HasMaxLength(1000); b.Property(x => x.Status).HasMaxLength(32).IsUnicode(false); b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Property(x => x.RequesterDisplay).HasMaxLength(200); b.Property(x => x.CreatedAtUtc).HasPrecision(3); b.Property(x => x.LastActivityAtUtc).HasPrecision(3); b.Property(x => x.SlaDueAtUtc).HasPrecision(3);
    }
}
