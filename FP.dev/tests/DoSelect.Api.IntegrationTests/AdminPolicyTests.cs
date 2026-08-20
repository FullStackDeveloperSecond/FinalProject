using DoSelect.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// `Invoice.Manage` 與 `Coupon.Manage` 的授權契約（DEC-BATCH-016／DEC-P283～P284）。
/// Policy 只負責授權；狀態、冪等、RowVersion、金額與 Audit 由各 Use Case 負責。
/// </summary>
public sealed class AdminPolicyTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(DoSelectPolicies.InvoiceManage)]
    [InlineData(DoSelectPolicies.CouponManage)]
    public async Task TheManagementPoliciesAreRegistered(string policyName) =>
        Assert.NotNull(await GetPolicyAsync(policyName));

    [Theory]
    [InlineData(DoSelectPolicies.InvoiceManage)]
    [InlineData(DoSelectPolicies.CouponManage)]
    public async Task TheyUseTheAdministratorSchemeAndRequireAuthentication(string policyName)
    {
        var policy = await GetPolicyAsync(policyName);

        Assert.Equal([DoSelectAuthenticationSchemes.Admin], policy!.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData(DoSelectPolicies.InvoiceManage)]
    [InlineData(DoSelectPolicies.CouponManage)]
    public async Task TheyRequireTheAdministratorAccountTypeAndMultiFactor(string policyName)
    {
        var claims = (await GetPolicyAsync(policyName))!.Requirements
            .OfType<ClaimsAuthorizationRequirement>()
            .ToArray();

        Assert.Contains(claims, requirement =>
            requirement.ClaimType == DoSelectClaimTypes.AccountType &&
            requirement.AllowedValues!.Contains(DoSelectClaimValues.Admin));
        Assert.Contains(claims, requirement =>
            requirement.ClaimType == DoSelectClaimTypes.AuthenticationMethod &&
            requirement.AllowedValues!.Contains(DoSelectClaimValues.MultiFactor));
    }

    [Fact]
    public async Task InvoiceManageAllowsFinanceManagerAndSuperAdminOnly()
    {
        var roles = await GetAllowedRolesAsync(DoSelectPolicies.InvoiceManage);

        Assert.Equal(
            [DoSelectRoles.FinanceManager, DoSelectRoles.SuperAdmin],
            roles.Order());
    }

    [Fact]
    public async Task CouponManageAlsoAllowsMarketingAnalyst()
    {
        var roles = await GetAllowedRolesAsync(DoSelectPolicies.CouponManage);

        Assert.Equal(
            [DoSelectRoles.FinanceManager, DoSelectRoles.MarketingAnalyst, DoSelectRoles.SuperAdmin],
            roles.Order());
    }

    [Fact]
    public async Task SuperAdminIsCarriedByEachPolicyRatherThanADedicatedOne()
    {
        // DEC-P283／P284：SuperAdmin 不另設專屬 Policy。
        Assert.Contains(
            DoSelectRoles.SuperAdmin,
            await GetAllowedRolesAsync(DoSelectPolicies.InvoiceManage));
        Assert.Contains(
            DoSelectRoles.SuperAdmin,
            await GetAllowedRolesAsync(DoSelectPolicies.CouponManage));
    }

    [Fact]
    public async Task CouponManageDoesNotGrantTheStorefrontCartPolicy()
    {
        // DEC-P284：Coupon.Manage 不涵蓋前台購物車套券。
        var couponManage = await GetPolicyAsync(DoSelectPolicies.CouponManage);
        var member = await GetPolicyAsync(DoSelectPolicies.Member);

        Assert.DoesNotContain(DoSelectAuthenticationSchemes.Member, couponManage!.AuthenticationSchemes);
        Assert.DoesNotContain(DoSelectAuthenticationSchemes.Admin, member!.AuthenticationSchemes);
    }

    [Fact]
    public async Task TheManagementPoliciesMatchTheRefundBaselineApartFromRoles()
    {
        // 兩者沿用既有 AddAdminPolicy 基線，與 Refund.Execute 只差在允許角色。
        var refundExecute = await GetPolicyAsync(DoSelectPolicies.RefundExecute);
        var invoiceManage = await GetPolicyAsync(DoSelectPolicies.InvoiceManage);

        Assert.Equal(refundExecute!.AuthenticationSchemes, invoiceManage!.AuthenticationSchemes);
        Assert.Equal(
            refundExecute.Requirements.Select(requirement => requirement.GetType()).Order(
                Comparer<Type>.Create((left, right) =>
                    string.CompareOrdinal(left.FullName, right.FullName))),
            invoiceManage.Requirements.Select(requirement => requirement.GetType()).Order(
                Comparer<Type>.Create((left, right) =>
                    string.CompareOrdinal(left.FullName, right.FullName))));
    }

    private async Task<IEnumerable<string>> GetAllowedRolesAsync(string policyName)
    {
        var policy = await GetPolicyAsync(policyName);

        return policy!.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(requirement => requirement.AllowedRoles);
    }

    private async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        return await provider.GetPolicyAsync(policyName);
    }
}
