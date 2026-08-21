using DoSelect.Application.Members;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterMemberService>();
        services.AddScoped<ConfirmEmailVerificationService>();
        services.AddScoped<RequestEmailVerificationService>();
        services.AddScoped<LoginMemberService>();

        return services;
    }
}
