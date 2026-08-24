using Microsoft.Extensions.Configuration;

namespace DoSelect.Infrastructure.Persistence.Seeding;

internal static class MinimalDevelopmentSeedDefinitions
{
    internal const string AdminEmail = "admin@doselect.local";
    internal const string MemberEmail = "member@doselect.local";
    internal const string AdminPasswordKey = "Seed:AdminPassword";
    internal const string MemberPasswordKey = "Seed:MemberPassword";

    internal static readonly Guid AdminPublicId =
        Guid.Parse("0f269121-89a5-43a4-97f5-b95278bc0cf6");

    internal static readonly Guid MemberPublicId =
        Guid.Parse("f84625a0-f32a-44bb-a801-5f69fed2cb12");

    internal static readonly Guid BrandPublicId =
        Guid.Parse("d6406598-d990-4bbf-8997-014605c0a89e");

    internal static readonly Guid CategoryPublicId =
        Guid.Parse("059386de-5978-4fb5-a531-0154fdb21edc");

    internal static readonly Guid ProductPublicId =
        Guid.Parse("5940b1db-3c83-4db0-b285-9777616d11b1");

    internal static readonly Guid SkuPublicId =
        Guid.Parse("719dfd4a-77f0-4887-b3bf-239263d4ee1f");

    internal static readonly DateTime CreatedAtUtc =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static readonly Guid CvsPickupMethodPublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f01");

    internal static readonly Guid HomeDeliveryMethodPublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f02");

    internal static readonly Guid HomeDeliveryAssemblyMethodPublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f03");

    internal static readonly Guid ConvenienceStoreProviderProfilePublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f11");

    internal static readonly Guid ConvenienceStorePackageLimitPublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f12");

    internal static readonly Guid HomeDeliveryProviderProfilePublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f21");

    internal static readonly Guid HomeDeliveryPackageLimitPublicId =
        Guid.Parse("2a9e4a2d-3f7a-4d2a-9a2e-6f6a8f8e2f22");

    internal static IReadOnlyList<string> RoleNames { get; } =
    [
        "SuperAdmin",
        "CatalogManager",
        "InventoryManager",
        "OrderManager",
        "FinanceManager",
        "CustomerService",
        "CustomerServiceSupervisor",
        "MarketingAnalyst",
        "PrivacyAdmin",
        "SecurityAdmin",
    ];

    internal static (string AdminPassword, string MemberPassword) GetPasswords(
        IConfiguration configuration)
    {
        var missingKeys = new List<string>();
        var adminPassword = configuration[AdminPasswordKey];
        var memberPassword = configuration[MemberPasswordKey];

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            missingKeys.Add(AdminPasswordKey);
        }

        if (string.IsNullOrWhiteSpace(memberPassword))
        {
            missingKeys.Add(MemberPasswordKey);
        }

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required User Secrets are missing: {string.Join(", ", missingKeys)}.");
        }

        return (adminPassword!, memberPassword!);
    }
}
