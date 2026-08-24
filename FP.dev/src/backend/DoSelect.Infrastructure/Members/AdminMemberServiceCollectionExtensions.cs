using DoSelect.Application.Members;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Members;

public static class AdminMemberServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectAdminMembers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAdminMemberQueryReader, AdminMemberQueryReader>();
        services.AddScoped<IAdminMemberWriter, AdminMemberWriter>();
        services.AddScoped<IAdminMemberPasswordResetInitiator, AdminMemberPasswordResetInitiator>();
        services.AddScoped<ListAdminMembersQuery>();
        services.AddScoped<GetAdminMemberDetailQuery>();
        services.AddScoped<UpdateAdminMemberProfileCommand>();
        services.AddScoped<SetMemberAccountStatusCommand>();
        services.AddScoped<ResetMemberPasswordCommand>();

        return services;
    }
}
