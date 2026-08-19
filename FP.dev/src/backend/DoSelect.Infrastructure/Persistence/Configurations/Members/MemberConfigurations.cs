using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence.Configurations;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DoSelect.Infrastructure.Persistence.Configurations.Members;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.PublicId).IsRequired();
        builder.HasIndex(user => user.PublicId)
            .IsUnique()
            .HasDatabaseName("UX_AspNetUsers_PublicId");
        builder.Property(user => user.AccountType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(user => user.AccountStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(user => user.PreferredLocale)
            .HasConversion(new ValueConverter<SupportedLocale, string>(
                locale => ToLocaleCode(locale),
                code => FromLocaleCode(code)))
            .HasMaxLength(10)
            .IsUnicode(false)
            .HasDefaultValue(SupportedLocale.ZhTw)
            .IsRequired();
        builder.Property(user => user.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(user => user.UpdatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(user => user.AnonymizedAtUtc).HasPrecision(3);
        builder.Property(user => user.RowVersion).IsRowVersion().IsRequired();
        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL")
            .HasDatabaseName("UX_AspNetUsers_NormalizedEmail");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_AspNetUsers_AccountType",
                "[AccountType] IN ('Member','Admin')");
            table.HasCheckConstraint(
                "CK_AspNetUsers_AccountStatus",
                "[AccountStatus] IN ('PendingEmailVerification','Active','Suspended','Anonymized','Disabled')");
            table.HasCheckConstraint(
                "CK_AspNetUsers_PreferredLocale",
                "[PreferredLocale] IN ('zh-TW','ja-JP','ko-KR')");
        });
    }

    private static string ToLocaleCode(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "zh-TW",
        SupportedLocale.JaJp => "ja-JP",
        SupportedLocale.KoKr => "ko-KR",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static SupportedLocale FromLocaleCode(string code) => code switch
    {
        "zh-TW" => SupportedLocale.ZhTw,
        "ja-JP" => SupportedLocale.JaJp,
        "ko-KR" => SupportedLocale.KoKr,
        _ => throw new InvalidOperationException("Unsupported locale code."),
    };
}

public sealed class MemberProfileConfiguration : IEntityTypeConfiguration<MemberProfile>
{
    public void Configure(EntityTypeBuilder<MemberProfile> builder)
    {
        builder.ToTable("MemberProfiles");
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).HasMaxLength(450);
        builder.Property(profile => profile.PublicId).IsRequired();
        builder.HasIndex(profile => profile.PublicId)
            .IsUnique()
            .HasDatabaseName("UX_MemberProfiles_PublicId");
        builder.Property(profile => profile.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.BirthDate).HasColumnType("date");
        builder.Property(profile => profile.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(profile => profile.RowVersion).IsRowVersion().IsRequired();
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<MemberProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdminProfileConfiguration : IEntityTypeConfiguration<AdminProfile>
{
    public void Configure(EntityTypeBuilder<AdminProfile> builder)
    {
        builder.ToTable("AdminProfiles");
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).HasMaxLength(450);
        builder.Property(profile => profile.PublicId).IsRequired();
        builder.HasIndex(profile => profile.PublicId)
            .IsUnique()
            .HasDatabaseName("UX_AdminProfiles_PublicId");
        builder.Property(profile => profile.EmployeeCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(profile => profile.EmployeeCode)
            .IsUnique()
            .HasDatabaseName("UX_AdminProfiles_EmployeeCode");
        builder.Property(profile => profile.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(profile => profile.IsActive).HasDefaultValue(true).IsRequired();
        builder.HasIndex(profile => profile.IsActive)
            .HasDatabaseName("IX_AdminProfiles_IsActive");
        builder.Property(profile => profile.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(profile => profile.RowVersion).IsRowVersion().IsRequired();
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<AdminProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MemberAddressConfiguration : IEntityTypeConfiguration<MemberAddress>
{
    public void Configure(EntityTypeBuilder<MemberAddress> builder)
    {
        builder.ConfigureMutablePublicEntity("MemberAddresses");
        builder.Property(address => address.MemberUserId).HasMaxLength(450).IsRequired();
        builder.Property(address => address.Label).HasMaxLength(50).IsRequired();
        builder.Property(address => address.RecipientName).HasMaxLength(100).IsRequired();
        builder.Property(address => address.Phone).HasMaxLength(32).IsRequired();
        builder.Property(address => address.PostalCode).HasMaxLength(16).IsRequired();
        builder.Property(address => address.City).HasMaxLength(50).IsRequired();
        builder.Property(address => address.District).HasMaxLength(50).IsRequired();
        builder.Property(address => address.AddressLine1).HasMaxLength(300).IsRequired();
        builder.Property(address => address.AddressLine2).HasMaxLength(300);
        builder.Property(address => address.IsDefault).HasDefaultValue(false).IsRequired();
        builder.Property(address => address.DeletedAtUtc).HasPrecision(3);
        builder.HasIndex(address => address.MemberUserId)
            .HasDatabaseName("IX_MemberAddresses_MemberUserId");
        builder.HasIndex(address => new { address.MemberUserId, address.IsDefault })
            .IsUnique()
            .HasFilter("[DeletedAtUtc] IS NULL AND [IsDefault] = 1")
            .HasDatabaseName("UX_MemberAddresses_MemberUserId_Default");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(address => address.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");
        builder.HasKey(favorite => new { favorite.MemberUserId, favorite.ProductId });
        builder.Property(favorite => favorite.MemberUserId).HasMaxLength(450);
        builder.Property(favorite => favorite.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.HasIndex(favorite => favorite.ProductId)
            .HasDatabaseName("IX_Favorites_ProductId");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(favorite => favorite.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(favorite => favorite.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
