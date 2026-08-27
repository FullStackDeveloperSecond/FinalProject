using DoSelect.Domain.Notifications;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations.Notifications;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ConfigurePublicEntity("Notifications");
        builder.Property(entity => entity.RecipientUserId).HasMaxLength(450).IsRequired();
        builder.Property(entity => entity.Type).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Title).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Body).HasMaxLength(1_000).IsRequired();
        builder.Property(entity => entity.ResourceType).HasMaxLength(64).IsUnicode(false);
        builder.Property(entity => entity.ReadAtUtc).HasPrecision(3);
        builder.Property(entity => entity.ExpiresAtUtc).HasPrecision(3);
        builder.HasIndex(entity => new
        {
            entity.RecipientUserId,
            entity.ReadAtUtc,
            entity.CreatedAtUtc,
        })
            .HasDatabaseName("IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Notifications", table =>
        {
            table.HasCheckConstraint(
                "CK_Notifications_Resource",
                "([ResourceType] IS NULL AND [ResourcePublicId] IS NULL) OR ([ResourceType] IS NOT NULL AND [ResourcePublicId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Notifications_ExpiresAtUtc",
                "[ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > [CreatedAtUtc]");
        });
    }
}

public sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        builder.ConfigureMutableEntity("EmailDeliveries");
        builder.Property(entity => entity.NotificationPublicId).IsRequired();
        builder.Property(entity => entity.RecipientUserId).HasMaxLength(450);
        builder.Property(entity => entity.RecipientEmailNormalized).HasMaxLength(320).IsRequired();
        builder.Property(entity => entity.TemplateCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.TemplateVersion).IsRequired();
        builder.Property(entity => entity.RecipientPurpose).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.ProviderMessageId).HasMaxLength(128);
        builder.Property(entity => entity.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.NextAttemptAtUtc).HasPrecision(3);
        builder.Property(entity => entity.SentAtUtc).HasPrecision(3);
        builder.Property(entity => entity.FailedAtUtc).HasPrecision(3);
        builder.Property(entity => entity.LastErrorCode).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(entity => entity.NotificationPublicId)
            .IsUnique()
            .HasDatabaseName("UX_EmailDeliveries_NotificationPublicId");
        builder.HasIndex(entity => new { entity.Status, entity.NextAttemptAtUtc })
            .HasDatabaseName("IX_EmailDeliveries_Status_NextAttemptAtUtc");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entity => entity.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("EmailDeliveries", table =>
        {
            table.HasCheckConstraint("CK_EmailDeliveries_TemplateVersion", "[TemplateVersion] > 0");
            table.HasCheckConstraint("CK_EmailDeliveries_AttemptCount", "[AttemptCount] >= 0");
            table.HasCheckConstraint(
                "CK_EmailDeliveries_State",
                "([Status] = 'Pending' AND [NextAttemptAtUtc] IS NOT NULL AND [SentAtUtc] IS NULL) OR " +
                "([Status] = 'Processing' AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NULL) OR " +
                "([Status] = 'Sent' AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NOT NULL) OR " +
                "([Status] IN ('Suppressed', 'Failed') AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NULL AND [FailedAtUtc] IS NOT NULL)");
        });
    }
}
