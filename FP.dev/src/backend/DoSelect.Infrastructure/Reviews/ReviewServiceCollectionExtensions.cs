using DoSelect.Application.Reviews;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Reviews;

public static class ReviewServiceCollectionExtensions
{
    public static IServiceCollection AddDoSelectReviews(this IServiceCollection services)
    {
        services.AddScoped<IReviewService, EfReviewService>();
        return services;
    }
}
