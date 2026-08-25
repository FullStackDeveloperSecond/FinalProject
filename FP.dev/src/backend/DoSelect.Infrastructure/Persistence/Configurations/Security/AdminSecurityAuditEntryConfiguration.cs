using DoSelect.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Security;

public sealed class AdminSecurityAuditEntryConfiguration : IEntityTypeConfiguration<AdminSecurityAuditEntry>
{
    public void Configure(EntityTypeBuilder<AdminSecurityAuditEntry> b)
    {
        b.ToTable("AdminSecurityAuditEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(48).IsUnicode(false).IsRequired();
        b.Property(x => x.AdminUserId).HasMaxLength(450);
        b.Property(x => x.IpAddress).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.Detail).HasMaxLength(500);
        b.Property(x => x.OccurredAtUtc).HasPrecision(3).IsRequired();
        b.HasIndex(x => new { x.AdminUserId, x.OccurredAtUtc })
            .HasDatabaseName("IX_AdminSecurityAuditEntries_AdminUserId_OccurredAtUtc");
    }
}
