using DoSelect.Application.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.Ai;

public static class AiSupportServiceCollectionExtensions
{
    public static IServiceCollection AddAiSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AiSupportOrchestrator>();
        services.TryAddScoped<IAiSupportAdmissionGate, FailClosedAiSupportAdmissionGate>();
        services.TryAddScoped<IAiSupportContextReader, FailClosedAiSupportContextReader>();
        services.TryAddScoped<IAiSupportModelClient, DisabledAiSupportModelClient>();
        services.TryAddScoped<IAiSupportInteractionStore, DisabledAiSupportInteractionStore>();
        return services;
    }

    private sealed class FailClosedAiSupportAdmissionGate : IAiSupportAdmissionGate
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

        public async Task<AiSupportReservationResult> TryReserveAsync(
            Guid memberId,
            Guid requestPublicId,
            CancellationToken cancellationToken)
        {
            var state = await ReadAsync(memberId, cancellationToken);
            return new AiSupportReservationResult(IsReserved: false, state);
        }
    }

    private sealed class FailClosedAiSupportContextReader : IAiSupportContextReader
    {
        public Task<AiSupportContextReadResult> ReadAsync(
            Guid memberId,
            Guid? conversationPublicId,
            IReadOnlyList<Guid> referencedOrderPublicIds,
            IReadOnlyList<Guid> referencedSupportTicketPublicIds,
            CancellationToken cancellationToken)
        {
            var result = referencedOrderPublicIds.Count == 0 &&
                referencedSupportTicketPublicIds.Count == 0
                ? new AiSupportContextReadResult(AiSupportContextStatus.Allowed, DataItems: [])
                : new AiSupportContextReadResult(AiSupportContextStatus.Unavailable, DataItems: []);

            return Task.FromResult(result);
        }
    }

    private sealed class DisabledAiSupportModelClient : IAiSupportModelClient
    {
        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new AiSupportModelAnswer(
                    Answer: null,
                    AiSupportModelAnswerStatus.Unavailable));
    }


    private sealed class DisabledAiSupportInteractionStore : IAiSupportInteractionStore
    {
        public Task<AiSupportInteractionWriteResult> SaveAsync(
            AiSupportInteractionWrite interaction,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiSupportInteractionWriteResult(
                Succeeded: false,
                interaction.ConversationPublicId ?? Guid.Empty));
    }
}
