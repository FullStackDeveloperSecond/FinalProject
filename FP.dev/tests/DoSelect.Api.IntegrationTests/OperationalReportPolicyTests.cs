using DoSelect.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

public sealed class OperationalReportPolicyTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(DoSelectPolicies.OperationalReportView)]
    [InlineData(DoSelectPolicies.OperationalReportFinanceView)]
    public async Task OperationalReportPoliciesUseTheAdminMfaBaseline(string policyName)
    {
        var policy = await GetPolicyAsync(policyName);

        Assert.NotNull(policy);
        Assert.Equal([DoSelectAuthenticationSchemes.Admin], policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);

        var claims = policy.Requirements.OfType<ClaimsAuthorizationRequirement>().ToArray();
        Assert.Contains(claims, requirement =>
            requirement.ClaimType == DoSelectClaimTypes.AccountType &&
            requirement.AllowedValues!.Contains(DoSelectClaimValues.Admin));
        Assert.Contains(claims, requirement =>
            requirement.ClaimType == DoSelectClaimTypes.AuthenticationMethod &&
            requirement.AllowedValues!.Contains(DoSelectClaimValues.MultiFactor));
    }

    [Fact]
    public async Task GeneralOperationalReportsAllowMarketingFinanceAndSuperAdmin()
    {
        Assert.Equal(
            [DoSelectRoles.FinanceManager, DoSelectRoles.MarketingAnalyst, DoSelectRoles.SuperAdmin],
            (await GetAllowedRolesAsync(DoSelectPolicies.OperationalReportView)).Order());
    }

    [Fact]
    public async Task FinancialOperationalReportsAllowFinanceAndSuperAdminOnly()
    {
        Assert.Equal(
            [DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin],
            (await GetAllowedRolesAsync(DoSelectPolicies.OperationalReportFinanceView)).Order());
    }

    private async Task<IEnumerable<string>> GetAllowedRolesAsync(string policyName) =>
        (await GetPolicyAsync(policyName))!.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(requirement => requirement.AllowedRoles);

    private async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        return await provider.GetPolicyAsync(policyName);
    }
}
