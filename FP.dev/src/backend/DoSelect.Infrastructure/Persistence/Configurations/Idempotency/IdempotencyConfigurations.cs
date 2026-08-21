using DoSelect.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Idempotency;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.ActorScopeHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.Operation).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Key).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.RequestHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ResponseSummary).HasMaxLength(32 * 1024);
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new { entity.ActorScopeHash, entity.Operation, entity.Key })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyRecords_ActorScopeHash_Operation_Key");
        builder.HasIndex(entity => entity.ExpiresAtUtc)
            .HasDatabaseName("IX_IdempotencyRecords_ExpiresAtUtc");
        builder.ToTable("IdempotencyRecords", table =>
            table.HasCheckConstraint(
                "CK_IdempotencyRecords_Status",
                "[Status] IN ('Processing','Succeeded','Failed')"));
    }
}
