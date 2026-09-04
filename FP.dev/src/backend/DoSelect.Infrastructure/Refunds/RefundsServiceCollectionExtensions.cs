using DoSelect.Application.Refunds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Refunds;

public static class RefundsServiceCollectionExtensions
{
    /// <summary>
    /// 註冊退款執行。需先呼叫 <c>AddDoSelectPersistence</c> 取得 <c>DoSelectDbContext</c>。
    /// </summary>
    public static IServiceCollection AddDoSelectRefunds(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRefundExecutionReader, RefundExecutionReader>();
        services.AddScoped<IRefundReader, RefundReader>();
        services.AddScoped<IRefundInvoiceReferenceReader, RefundInvoiceReferenceReader>();
        services.AddScoped<IRefundExecutor, RefundExecutor>();
        services.AddScoped<IRefundApprover, RefundApprover>();
        services.AddScoped<IReturnRefundCreationPort, ReturnRefundCreationPort>();
        services.AddScoped<ExecuteRefundService>();

        return services;
    }
}
