using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Security;
using DoSelect.Infrastructure.Tests.Idempotency;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

/// <summary>
/// alex review P1#2：管理員 30 分鐘鎖定必須用真正的 UserManager／SQL Server 驗證，不能只測
/// fake gateway——原本的 bug（讀 AccessFailedCount 判斷是否達門檻）只有在真正的 Identity Store
/// 上才會重現：Identity 的 AccessFailedAsync 命中全域門檻時，會在同一次呼叫內把
/// AccessFailedCount 重設回 0、同時套用全域 DefaultLockoutTimeSpan（15 分鐘）——用 fake gateway
/// 模擬「命中門檻」時，測試自己控制回傳值，看不出這個重設會讓「讀 count 判斷」的邏輯永遠讀到
/// 重設後的 0，30 分鐘覆蓋動作永遠不會執行。
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public sealed class IdentityAdminAuthGatewaySqlServerTests
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelect;Trusted_Connection=True;Encrypt=False;";
    private const string Password = "correct-horse-battery-staple";

    [SqlServerFact]
    public async Task RegisterFailedAttemptAsync_WhenTheFifthFailureLands_LocksTheAdminForTheGivenDurationNotIdentitysGlobalDefault()
    {
        await RunWithRealIdentityAsync(async (gateway, userManager, user) =>
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var lockoutEnd = await gateway.RegisterFailedAttemptAsync(user.Id, TimeSpan.FromMinutes(30));
                Assert.Null(lockoutEnd);
            }

            var fifthLockoutEnd = await gateway.RegisterFailedAttemptAsync(user.Id, TimeSpan.FromMinutes(30));
            Assert.NotNull(fifthLockoutEnd);

            // 從真正的 Identity Store 讀回持久化的值，不是只看回傳值——確認 RegisterFailedAttemptAsync
            // 內部覆蓋 Identity 自己套用的 15 分鐘全域鎖定這件事，真的落地在資料庫裡。重新
            // FindByIdAsync 拿一個反映最新狀態的 entity——gateway 內部每次呼叫都是自己重新
            // 查詢，原本這裡持有的 user 物件記憶體內容不會自動跟著更新。
            var reloadedUser = await userManager.FindByIdAsync(user.Id);
            var persistedLockoutEnd = await userManager.GetLockoutEndDateAsync(reloadedUser!);
            Assert.NotNull(persistedLockoutEnd);

            // 差距必須落在 30 分鐘附近，而不是 Identity 全域 DefaultLockoutTimeSpan 的 15 分鐘。
            var minutesUntilLockout = (persistedLockoutEnd!.Value - DateTimeOffset.UtcNow).TotalMinutes;
            Assert.InRange(minutesUntilLockout, 25, 35);
        });
    }

    [SqlServerFact]
    public async Task RegisterFailedAttemptAsync_BelowThreshold_DoesNotLockTheAccount()
    {
        await RunWithRealIdentityAsync(async (gateway, userManager, user) =>
        {
            var lockoutEnd = await gateway.RegisterFailedAttemptAsync(user.Id, TimeSpan.FromMinutes(30));

            Assert.Null(lockoutEnd);
            var reloadedUser = await userManager.FindByIdAsync(user.Id);
            Assert.Null(await userManager.GetLockoutEndDateAsync(reloadedUser!));
        });
    }

    private static async Task RunWithRealIdentityAsync(
        Func<IdentityAdminAuthGateway, UserManager<ApplicationUser>, ApplicationUser, Task> test)
    {
        var connectionString = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            LocalConnectionString)
        {
            InitialCatalog = $"DoSelectAdminLockout_{Guid.NewGuid():N}",
        }.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", connectionString),
            ])
            .Build();
        var services = new ServiceCollection();
        services.AddDoSelectPersistence(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        try
        {
            await dbContext.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var now = DateTime.UtcNow;
            var user = ApplicationUser.CreateAdmin(Guid.NewGuid(), $"admin-lockout-{Guid.NewGuid():N}@example.com", now);
            user.ConfirmEmail(now);
            var createResult = await userManager.CreateAsync(user, Password);
            Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));

            var gateway = new IdentityAdminAuthGateway(userManager, dbContext, TimeProvider.System);

            await test(gateway, userManager, user);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
            await dbContext.Database.EnsureDeletedAsync();
        }
    }
}
