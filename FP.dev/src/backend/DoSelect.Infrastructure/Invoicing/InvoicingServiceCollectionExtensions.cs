using DoSelect.Application.Orders;
using DoSelect.Infrastructure.Orders;
using DoSelect.Application.Invoicing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Invoicing;

public static class InvoicingServiceCollectionExtensions
{
    /// <summary>
    /// 註冊模擬發票折讓。需先呼叫 <c>AddDoSelectPersistence</c> 取得 <c>DoSelectDbContext</c>。
    /// </summary>
    public static IServiceCollection AddDoSelectInvoicing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IInvoiceAllowanceReader, InvoiceAllowanceReader>();
        services.AddScoped<IInvoiceAllowanceWriter, InvoiceAllowanceWriter>();
        services.AddScoped<IssueInvoiceAllowanceService>();

        // M-20 查詢：Reader 只讀 Invoicing 自己的表，訂單那半由 Orders 的埠批次補上，
        // 兩者在 InvoiceQueryService 合併（Issue #65 A1）。
        services.AddScoped<IInvoiceQueryReader, InvoiceQueryReader>();
        services.AddScoped<InvoiceQueryService>();
        services.AddScoped<IOrderInvoiceReferenceReader, OrderInvoiceReferenceReader>();

        return services;
    }
}
