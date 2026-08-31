using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.Ai;

public static class AiSupportServiceCollectionExtensions
{
    public static IServiceCollection AddAiSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AiSupportOrchestrator>();
        services.AddScoped<AiProductSearchOrchestrator>();
        services.TryAddScoped<IAiSupportAdmissionGate, FailClosedAiSupportAdmissionGate>();
        services.TryAddScoped<IAiSupportContextReader, FailClosedAiSupportContextReader>();
        services.TryAddScoped<IAiSupportModelClient, DisabledAiSupportModelClient>();
        services.TryAddScoped<IAiSupportInteractionStore, DisabledAiSupportInteractionStore>();
        services.TryAddScoped<IAiProductSearchAdmissionGate, DisabledAiProductSearchAdmissionGate>();
        services.TryAddScoped<IAiProductSearchModelClient, DisabledAiProductSearchModelClient>();
        services.TryAddScoped<IAiProductSearchCatalog, DisabledAiProductSearchCatalog>();
        services.TryAddScoped<IAiProductSearchInteractionStore, DisabledAiProductSearchInteractionStore>();
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

    private sealed class DisabledAiProductSearchAdmissionGate : IAiProductSearchAdmissionGate
    {
        private static AiProductSearchAccessState State =>
            new(0, DateTimeOffset.UtcNow.AddDays(1), BudgetProtectionActive: true, IsDemoAllowlisted: false);

        public Task<AiProductSearchAccessState> ReadAsync(
            AiProductSearchActor actor,
            CancellationToken cancellationToken) => Task.FromResult(State);

        public Task<AiProductSearchReservationResult> TryReserveAsync(
            AiProductSearchActor actor,
            Guid requestPublicId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchReservationResult(false, State));
    }

    private sealed class DisabledAiProductSearchModelClient : IAiProductSearchModelClient
    {
        public Task<AiProductSearchIntentResult> ParseIntentAsync(
            string message,
            SupportedLocale locale,
            AiProductSearchMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchIntentResult(
                AiProductSearchModelStatus.Unavailable,
                null,
                null));

        public Task<AiProductSearchExplanationResult> ExplainAsync(
            AiProductSearchIntent intent,
            IReadOnlyList<ProductCardDto> approvedCandidates,
            SupportedLocale locale,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchExplanationResult(
                AiProductSearchModelStatus.Unavailable,
                [],
                null));
    }

    private sealed class DisabledAiProductSearchCatalog : IAiProductSearchCatalog
    {
        public Task<AiProductSearchMetadata> ReadMetadataAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchMetadata([], [], []));

        public Task<AiProductSearchCandidateResult> FindCandidatesAsync(
            AiProductSearchIntent intent,
            IReadOnlyList<AiProductSearchExistingPart> existingParts,
            SupportedLocale locale,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchCandidateResult(
                false,
                AiSafetyReason.ServiceUnavailable,
                [],
                []));

        public Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
            string message,
            SupportedLocale locale,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCardDto>>([]);
    }

    private sealed class DisabledAiProductSearchInteractionStore : IAiProductSearchInteractionStore
    {
        public Task<bool> SaveAsync(
            AiProductSearchInteractionWrite interaction,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
