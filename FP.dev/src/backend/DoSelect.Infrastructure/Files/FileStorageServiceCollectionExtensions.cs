using DoSelect.Application.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Files;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var dataRoot = configuration["Storage:DataRoot"];
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = Path.Combine(Path.GetTempPath(), "DoSelectData");
        }

        services.AddSingleton<IFileScanner, MicrosoftDefenderFileScanner>();
        services.AddSingleton<IPrivateFileStorage>(serviceProvider =>
            new LocalPrivateFileStorage(
                dataRoot,
                serviceProvider.GetRequiredService<IFileScanner>()));
        services.AddSingleton<IImageStorage>(serviceProvider =>
            new LocalImageStorage(
                dataRoot,
                serviceProvider.GetRequiredService<IFileScanner>()));

        return services;
    }
}
