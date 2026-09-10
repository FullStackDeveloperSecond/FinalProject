using DoSelect.Application.Orders;
using DoSelect.Infrastructure.Orders;
using DoSelect.Application.Invoicing;
using DoSelect.Application.Refunds;
using DoSelect.Infrastructure.Refunds;
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
        services.AddScoped<IOrderInvoiceIssuanceReader, OrderInvoiceIssuanceReader>();
        services.AddScoped<IInvoiceExistenceReader, InvoiceExistenceReader>();
        services.AddScoped<IInvoiceNumberSequence, InvoiceNumberSequence>();
        services.AddScoped<IssueInvoiceService>();
        services.AddScoped<IInvoiceAllowanceReader, InvoiceAllowanceReader>();
        services.AddScoped<IInvoiceAllowanceWriter, InvoiceAllowanceWriter>();
        services.AddScoped<IAdminInvoiceWriter, AdminInvoiceWriter>();
        services.AddScoped<InvoiceIssuanceOrderQueryService>();
        services.AddScoped<IssueInvoiceAllowanceService>();

        // M-20 一般查詢：Reader 只讀 Invoicing 自己的表，訂單那半由 Orders 的埠批次補上。
        // 只有依訂單號碼排序需在分頁前跨表，才走窄唯讀投影。
        services.AddScoped<IInvoiceQueryReader, InvoiceQueryReader>();
        services.AddScoped<IOrderNumberSortedAdminInvoiceReader, OrderNumberSortedAdminInvoiceReader>();
        services.AddScoped<InvoiceQueryService>();
        services.AddScoped<IOrderInvoiceReferenceReader, OrderInvoiceReferenceReader>();
        services.AddScoped<IOrderInvoiceVoidReader, OrderInvoiceVoidReader>();
        services.AddScoped<IRefundInvoiceVoidReader, RefundInvoiceVoidReader>();

        return services;
    }
}
