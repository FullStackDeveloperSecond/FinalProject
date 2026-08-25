using DoSelect.Application.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StorageOptions = DoSelect.Application.Storage.StorageOptions;

namespace DoSelect.Infrastructure.Files;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectFileStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFileScanner, MicrosoftDefenderFileScanner>();
        services.AddSingleton<IPrivateFileStorage>(serviceProvider =>
            new LocalPrivateFileStorage(
                serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.DataRoot,
                serviceProvider.GetRequiredService<IFileScanner>()));
        services.AddSingleton<IImageStorage>(serviceProvider =>
            new LocalImageStorage(
                serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.DataRoot,
                serviceProvider.GetRequiredService<IFileScanner>()));

        return services;
    }
}
