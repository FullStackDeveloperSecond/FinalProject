using DoSelect.Application.Favorites;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Favorites;

public static class FavoriteServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectFavorites(this IServiceCollection services)
    {
        services.AddScoped<IFavoriteService, EfFavoriteService>();
        return services;
    }
}
