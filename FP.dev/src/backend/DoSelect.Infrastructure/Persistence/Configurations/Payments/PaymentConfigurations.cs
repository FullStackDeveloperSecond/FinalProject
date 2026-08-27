using DoSelect.Domain.Orders;
using DoSelect.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Payments;

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ConfigureMutablePublicEntity("PaymentAttempts");
        Enum(builder.Property(x => x.Method), 24);
        Enum(builder.Property(x => x.Status), 24, PaymentAttemptStatus.Pending);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ProviderCode).HasMaxLength(64);
        builder.Property(x => x.ExternalReference).HasMaxLength(128);
        builder.Property(x => x.InstructionExpiresAtUtc).HasPrecision(3);
        builder.Property(x => x.PaidAtUtc).HasPrecision(3);
        builder.Property(x => x.FailedAtUtc).HasPrecision(3);
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.ExternalReference).IsUnique().HasFilter("[ExternalReference] IS NOT NULL").HasDatabaseName("UX_PaymentAttempts_ExternalReference");
        builder.HasIndex(x => x.IdempotencyKey).HasDatabaseName("IX_PaymentAttempts_IdempotencyKey");
        builder.HasIndex(x => new { x.OrderId, x.CreatedAtUtc }).HasDatabaseName("IX_PaymentAttempts_OrderId_CreatedAtUtc");
        builder.HasIndex(x => x.Method).HasDatabaseName("IX_PaymentAttempts_Method");
        builder.HasIndex(x => x.Status).HasDatabaseName("IX_PaymentAttempts_Status");
        builder.HasIndex(x => x.ProviderCode).HasDatabaseName("IX_PaymentAttempts_ProviderCode");
        builder.HasIndex(x => x.InstructionExpiresAtUtc).HasDatabaseName("IX_PaymentAttempts_InstructionExpiresAtUtc");
        builder.HasIndex(x => x.FailureCode).HasDatabaseName("IX_PaymentAttempts_FailureCode");
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("PaymentAttempts", table => table.HasCheckConstraint("CK_PaymentAttempts_Amount", "[Amount] > 0"));
    }

    private static void Enum<T>(PropertyBuilder<T> property, int length, T? defaultValue = null) where T : struct, Enum
    {
        property.HasConversion<string>().HasMaxLength(length).IsUnicode(false).IsRequired();
        if (defaultValue.HasValue) property.HasDefaultValue(defaultValue.Value);
    }
}

public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ConfigurePublicEntity("PaymentEvents");
        builder.Property(x => x.ExternalEventId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.ExternalEventId).IsUnique().HasDatabaseName("UX_PaymentEvents_ExternalEventId");
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OccurredAt).HasPrecision(3).IsRequired();
        builder.Property(x => x.ReceivedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.PayloadSummaryJson).HasMaxLength(4000);
        builder.Property(x => x.ProcessingStatus).HasConversion<string>().HasMaxLength(24).IsUnicode(false).HasDefaultValue(PaymentEventProcessingStatus.Received).IsRequired();
        builder.HasIndex(x => x.PaymentAttemptId).HasDatabaseName("IX_PaymentEvents_PaymentAttemptId");
        builder.HasIndex(x => x.EventType).HasDatabaseName("IX_PaymentEvents_EventType");
        builder.HasIndex(x => x.OccurredAt).HasDatabaseName("IX_PaymentEvents_OccurredAt");
        builder.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("IX_PaymentEvents_ReceivedAtUtc");
        builder.HasIndex(x => x.PayloadHash).HasDatabaseName("IX_PaymentEvents_PayloadHash");
        builder.HasIndex(x => x.ProcessingStatus).HasDatabaseName("IX_PaymentEvents_ProcessingStatus");
        builder.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
