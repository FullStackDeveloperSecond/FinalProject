using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectApplication(this IServiceCollection services)
    {
        services.AddSingleton<IEmailRequestThrottle, EmailRequestThrottle>();
        services.AddSingleton<IGuestOrderAccessThrottle, GuestOrderAccessThrottle>();

        services.AddScoped<RegisterMemberService>();
        services.AddScoped<ConfirmEmailVerificationService>();
        services.AddScoped<RequestEmailVerificationService>();
        services.AddScoped<LoginMemberService>();
        services.AddScoped<RequestPasswordResetService>();
        services.AddScoped<ResetPasswordService>();
        services.AddScoped<PurgeStaleUnverifiedMembersService>();
        services.AddScoped<GuestOrderAccessUseCase>();
        services.AddScoped<GuestOrderAccessScopeAuthorizer>();

        return services;
    }
}
