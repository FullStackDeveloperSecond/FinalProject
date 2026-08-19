using DoSelect.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DoSelect.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public static void ConfigurePublicEntity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string tableName)
        where TEntity : PublicEntity
    {
        builder.ToTable(tableName);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.PublicId).IsRequired();
        builder.HasIndex(entity => entity.PublicId)
            .IsUnique()
            .HasDatabaseName($"UX_{tableName}_PublicId");
        builder.Property(entity => entity.CreatedAtUtc)
            .HasPrecision(3)
            .IsRequired();
    }

    public static void ConfigureMutablePublicEntity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string tableName)
        where TEntity : MutablePublicEntity
    {
        builder.ConfigurePublicEntity(tableName);
        builder.Property(entity => entity.UpdatedAtUtc)
            .HasPrecision(3)
            .IsRequired();
        builder.Property(entity => entity.RowVersion)
            .IsRowVersion()
            .IsRequired();
    }

    public static void ConfigureMutableEntity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string tableName)
        where TEntity : MutableEntity
    {
        builder.ToTable(tableName);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).UseIdentityColumn();
        builder.Property(entity => entity.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsRequired();
    }
}
