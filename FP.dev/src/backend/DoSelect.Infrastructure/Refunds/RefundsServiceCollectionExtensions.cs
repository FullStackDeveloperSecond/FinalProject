using DoSelect.Application.Refunds;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Refunds;

public static class RefundsServiceCollectionExtensions
{
    /// <summary>
    /// 註冊退款執行。需先呼叫 <c>AddDoSelectPersistence</c> 取得 <c>DoSelectDbContext</c>。
    /// </summary>
    public static IServiceCollection AddDoSelectRefunds(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRefundExecutionReader, RefundExecutionReader>();
        services.AddScoped<ExecuteRefundService>();

        return services;
    }
}
