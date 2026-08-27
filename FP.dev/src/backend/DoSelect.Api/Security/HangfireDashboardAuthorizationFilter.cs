using Hangfire.Dashboard;

namespace DoSelect.Api.Security;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true &&
            user.HasClaim(DoSelectClaimTypes.AccountType, DoSelectClaimValues.Admin) &&
            user.HasClaim(DoSelectClaimTypes.AuthenticationMethod, DoSelectClaimValues.MultiFactor) &&
            user.IsInRole(DoSelectRoles.SuperAdmin);
    }
}
