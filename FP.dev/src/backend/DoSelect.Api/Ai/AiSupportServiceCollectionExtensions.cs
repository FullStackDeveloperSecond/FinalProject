using DoSelect.Application.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.Ai;

public static class AiSupportServiceCollectionExtensions
{
    public static IServiceCollection AddAiSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AiSupportOrchestrator>();
        services.TryAddScoped<IAiSupportAccessReader, FailClosedAiSupportAccessReader>();
        services.TryAddScoped<IAiSupportModelClient, DisabledAiSupportModelClient>();
        return services;
    }

    private sealed class FailClosedAiSupportAccessReader : IAiSupportAccessReader
    {
        public Task<AiSupportAccessState> ReadAsync(
            Guid memberId,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var resetAtUtc = new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                0,
                0,
                0,
                TimeSpan.Zero).AddDays(1);

            return Task.FromResult(
                new AiSupportAccessState(
                    AiConsentState.Missing,
                    RemainingDailyMessages: 0,
                    resetAtUtc));
        }
    }

    private sealed class DisabledAiSupportModelClient : IAiSupportModelClient
    {
        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "No AI support model adapter has been registered.");
    }
}
