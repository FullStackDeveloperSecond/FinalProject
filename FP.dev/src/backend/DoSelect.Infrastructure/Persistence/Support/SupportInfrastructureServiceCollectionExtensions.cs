using DoSelect.Application.Storage;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Admin;
using DoSelect.Infrastructure.Persistence.Support.Admin;
using DoSelect.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Persistence.Support;

public static class SupportInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSupportInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ISupportTicketStore, SupportTicketStore>();
        services.AddScoped<IOrderOwnershipLookup, OrderOwnershipLookup>();
        services.AddScoped<IAdminSupportTicketStore, AdminSupportTicketStore>();
        services.AddScoped<ISupportSlaQueueStore, SupportSlaQueueStore>();
        services.AddScoped<ICaseWorkbenchStore, CaseWorkbenchStore>();
        services.AddScoped<ISupportAttachmentReadStore, SupportAttachmentReadStore>();
        services.AddSingleton<IPrivateFileStorage, LocalPrivateFileStorage>();
        return services;
    }
}
