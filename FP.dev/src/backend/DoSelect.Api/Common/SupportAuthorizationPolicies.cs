namespace DoSelect.Api.Common;

/// <summary>
/// Central policy names for the Support admin HTTP surface (team-lead 客服Policy裁定回覆,
/// 2026-08-20). Handle covers the day-to-day operational actions that already exist (claim) and
/// the ones documented to come later (public reply, internal note, ordinary priority/status
/// change, cancel, reopen). Supervise is reserved for assign/transfer and supervisor priority
/// overrides, which have no production use case yet — the policy is registered now so those
/// endpoints only need the attribute when they ship. SuperAdmin alone never satisfies Handle: a
/// SuperAdmin must also hold CustomerService or CustomerServiceSupervisor to act as a handling
/// agent, though role unions (e.g. SuperAdmin + CustomerService) do satisfy it.
/// </summary>
public static class SupportAuthorizationPolicies
{
    public const string Handle = "SupportTicket.Handle";
    public const string Supervise = "SupportTicket.Supervise";

    private const string CustomerServiceRole = "CustomerService";
    private const string CustomerServiceSupervisorRole = "CustomerServiceSupervisor";
    private const string SuperAdminRole = "SuperAdmin";

    public static IServiceCollection AddSupportAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Handle, policy => policy.RequireRole(
                CustomerServiceRole,
                CustomerServiceSupervisorRole));

            options.AddPolicy(Supervise, policy => policy.RequireRole(
                CustomerServiceSupervisorRole,
                SuperAdminRole));
        });

        return services;
    }
}
