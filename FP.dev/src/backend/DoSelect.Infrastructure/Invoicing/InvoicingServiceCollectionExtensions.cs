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

        return services;
    }
}
