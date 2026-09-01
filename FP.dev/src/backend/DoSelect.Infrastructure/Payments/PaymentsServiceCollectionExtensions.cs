using DoSelect.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Infrastructure.Payments;

public static class PaymentsServiceCollectionExtensions
{
    /// <summary>
    /// 註冊模擬付款。需先呼叫 <c>AddDoSelectPersistence</c> 取得 <c>DoSelectDbContext</c>，
    /// 以及 <c>AddDoSelectIdempotency</c> 取得共用的冪等執行器。
    /// </summary>
    public static IServiceCollection AddDoSelectPayments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPaymentAttemptReader, PaymentAttemptReader>();
        services.AddScoped<StartPaymentAttemptService>();
        services.AddScoped<IPaymentAttemptWriter, PaymentAttemptWriter>();
        services.AddScoped<CompleteSimulatedPaymentService>();
        services.AddScoped<CashOnDeliveryCompletionService>();
        services.AddScoped<ISimulatedPaymentAuthorizationReader, SimulatedPaymentAuthorizationReader>();
        services.AddScoped<ISimulatedPaymentWriter, SimulatedPaymentWriter>();

        return services;
    }
}
