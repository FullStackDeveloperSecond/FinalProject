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

    // H-R03 的 admin-chromium Browser E2E 在 CI 對整套測試共用一顆資料庫（見
    // scripts/test-customer-e2e.ps1 與 CI 設定），若沿用上面唯一的主要管理員帳號，誰先
    // 完成 TOTP 綁定，其餘測試看到的畫面就從「/admin/login/enroll」變成
    // 「/admin/login/verify」，斷言直接失敗（alex PR #117 review P1）。這兩個帳號在種子時
    // 就直接寫入「已知秘鑰、已完成綁定」狀態（見 MinimalDevelopmentDataSeeder.
    // EnsurePreEnrolledAdminAsync），讓 H-R03 兩支測試完全跳過 enroll 流程、各自用固定秘鑰
    // 算 TOTP code；一支測試一個帳號是為了避免 fullyParallel 下兩支測試互搶同一顆帳號的
    // 登入/操作狀態。H-R02 仍是唯一實際操作 enroll 流程本身的測試，兩邊互不干擾。
    internal const string AdminHr03PrimaryEmail = "admin-h-r03-primary@doselect.local";
    internal const string AdminHr03SecondaryEmail = "admin-h-r03-secondary@doselect.local";

    internal static readonly Guid AdminHr03PrimaryPublicId =
        Guid.Parse("eb73d60d-7def-4609-8129-fff09551d014");

    internal static readonly Guid AdminHr03SecondaryPublicId =
        Guid.Parse("6e897e04-67ae-4578-bb8c-ddeae6f76316");

    // 固定的 Base32 TOTP 秘鑰，僅供本機／CI 種子帳號使用（非正式環境資料，兩個帳號共用同一
    // 組即可——各自的帳號列互相獨立，不構成安全疑慮）。
    internal const string AdminHr03TotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

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

    internal const string CoreTransactionGuestCartKey =
        "e2e-core-transaction-guest-cart-key-0001";

    internal static readonly Guid CoreTransactionGuestCartPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a08");

    internal static readonly Guid CoreTransactionAssemblyGroupKey =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a09");

    internal static readonly Guid Creator10CouponPublicId =
        Guid.Parse("3f6a0c1e-3b7e-4c1a-9f4d-5b6d9e2f1a10");

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
