using DoSelect.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Configuration;

namespace DoSelect.Infrastructure.Tests;

public sealed class MinimalDevelopmentSeedTests
{
    [Fact]
    public void RoleNames_DefineTenDistinctFormalRoles()
    {
        Assert.Equal(10, MinimalDevelopmentSeedDefinitions.RoleNames.Count);
        Assert.Equal(
            MinimalDevelopmentSeedDefinitions.RoleNames.Count,
            MinimalDevelopmentSeedDefinitions.RoleNames.Distinct().Count());
        Assert.Contains("SuperAdmin", MinimalDevelopmentSeedDefinitions.RoleNames);
        Assert.Contains("PrivacyAdmin", MinimalDevelopmentSeedDefinitions.RoleNames);
        Assert.Contains("SecurityAdmin", MinimalDevelopmentSeedDefinitions.RoleNames);
    }

    [Fact]
    public void GetPasswords_WhenSecretsAreMissing_ReportsKeysWithoutValues()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MinimalDevelopmentSeedDefinitions.GetPasswords(configuration));

        Assert.Contains("Seed:AdminPassword", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Seed:MemberPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPasswords_WhenSecretsExist_ReturnsBothValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminPassword"] = "admin-secret",
                ["Seed:MemberPassword"] = "member-secret",
            })
            .Build();

        var result = MinimalDevelopmentSeedDefinitions.GetPasswords(configuration);

        Assert.Equal("admin-secret", result.AdminPassword);
        Assert.Equal("member-secret", result.MemberPassword);
    }
}
