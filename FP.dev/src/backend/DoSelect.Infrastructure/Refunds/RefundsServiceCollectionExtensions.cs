using DoSelect.Application.Refunds;
using DoSelect.Infrastructure.Orders;
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
        // AddDoSelectRefunds 保持可獨立解析：既有契約只要求先註冊 Persistence。
        // Order 模組也用 TryAdd 註冊同一實作，兩種呼叫順序都不會產生重複 descriptor。
        services.TryAddScoped<IRefundOrderProjectionPort, EfRefundOrderProjectionPort>();
        services.AddScoped<ExecuteRefundService>();

        return services;
    }
}
