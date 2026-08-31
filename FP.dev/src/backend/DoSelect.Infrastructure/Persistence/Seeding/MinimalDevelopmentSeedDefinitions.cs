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

    internal static readonly Guid InventoryBalancePublicId =
        Guid.Parse("fc3ad2c7-a879-408f-8794-a755efa4e0ad");

    internal static readonly Guid StorePickupMethodPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a01");

    internal static readonly Guid HomeDeliveryMethodPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a02");

    internal static readonly Guid HomeDeliveryAssemblyMethodPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a03");

    internal static readonly Guid StorePickupProviderProfilePublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a04");

    internal static readonly Guid HomeDeliveryProviderProfilePublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a05");

    internal static readonly Guid StorePickupPackageLimitPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a06");

    internal static readonly Guid HomeDeliveryPackageLimitPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a07");

    internal static readonly DateTime CreatedAtUtc =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
