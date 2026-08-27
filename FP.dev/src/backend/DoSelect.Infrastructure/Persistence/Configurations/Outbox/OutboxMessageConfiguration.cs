using DoSelect.Domain.Outbox;
using DoSelect.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ConfigurePublicEntity("OutboxMessages");
        builder.Property(entity => entity.Type).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.PayloadVersion).IsRequired();
        builder.Property(entity => entity.AggregateType).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.PayloadJson)
            .HasMaxLength(8_000)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.AvailableAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.ProcessedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.LastErrorCode).HasMaxLength(64).IsUnicode(false);
        builder.Property(entity => entity.CorrelationId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsRequired();
        builder.HasIndex(entity => new { entity.Status, entity.AvailableAtUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_AvailableAtUtc");
        builder.HasIndex(entity => new
        {
            entity.AggregateType,
            entity.AggregatePublicId,
            entity.OccurredAtUtc,
        })
            .HasDatabaseName("IX_OutboxMessages_Aggregate_OccurredAtUtc");
        builder.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_PayloadVersion", "[PayloadVersion] > 0");
            table.HasCheckConstraint("CK_OutboxMessages_PayloadJson", "ISJSON([PayloadJson]) = 1");
            table.HasCheckConstraint("CK_OutboxMessages_Availability", "[AvailableAtUtc] >= [OccurredAtUtc]");
            table.HasCheckConstraint("CK_OutboxMessages_AttemptCount", "[AttemptCount] >= 0");
            table.HasCheckConstraint(
                "CK_OutboxMessages_ProcessedState",
                "([Status] = 'Processed' AND [ProcessedAtUtc] IS NOT NULL) OR ([Status] <> 'Processed' AND [ProcessedAtUtc] IS NULL)");
        });
    }
}
