using DoSelect.Domain.Idempotency;
using DoSelect.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Idempotency;

public sealed class IdempotencyRecordConfiguration
    : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ConfigureMutableEntity("IdempotencyRecords");
        builder.Property(entity => entity.ActorScopeHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.Operation).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Key).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RequestHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ResponseHeadersJson).HasMaxLength(8000);
        builder.Property(entity => entity.ResponseSummary).HasMaxLength(32768);
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.ActorScopeHash,
            entity.Operation,
            entity.Key,
        })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyRecords_ActorScope_Operation_Key");
        builder.HasIndex(entity => entity.ExpiresAtUtc)
            .HasDatabaseName("IX_IdempotencyRecords_ExpiresAtUtc");
        builder.ToTable("IdempotencyRecords", table =>
        {
            table.HasCheckConstraint(
                "CK_IdempotencyRecords_ResponseStatusCode",
                "[ResponseStatusCode] IS NULL OR ([ResponseStatusCode] >= 100 AND [ResponseStatusCode] <= 599)");
            table.HasCheckConstraint(
                "CK_IdempotencyRecords_CompletedResponse",
                "([Status] = 'Processing' AND [ResponseStatusCode] IS NULL AND [ResponseHeadersJson] IS NULL AND [ResponseSummary] IS NULL) OR ([Status] IN ('Succeeded', 'Failed') AND [ResponseStatusCode] IS NOT NULL AND [ResponseHeadersJson] IS NOT NULL AND [ResponseSummary] IS NOT NULL)");
        });
    }
}
