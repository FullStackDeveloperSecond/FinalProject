using DoSelect.Domain.Members;

namespace DoSelect.Application.Ai;

public sealed record AiSupportAccessState(
    AiConsentState ConsentState,
    int RemainingDailyMessages,
    DateTimeOffset ResetAtUtc);

public sealed record AiSupportReservationResult(
    bool IsReserved,
    AiSupportAccessState State);

public interface IAiSupportAdmissionGate
{
    Task<AiSupportAccessState> ReadAsync(
        Guid memberId,
        CancellationToken cancellationToken);

    Task<AiSupportReservationResult> TryReserveAsync(
        Guid memberId,
        Guid requestPublicId,
        CancellationToken cancellationToken);
}

public enum AiSupportContextStatus
{
    Allowed = 0,
    ResourceNotFound = 1,
    Unavailable = 2,
}

public sealed record AiSupportContextItem(
    string SourceType,
    string SourceId,
    string Title,
    string VersionOrUpdatedAt,
    string Content);

public sealed record AiSupportContextReadResult(
    AiSupportContextStatus Status,
    IReadOnlyList<AiSupportContextItem> DataItems);

public interface IAiSupportContextReader
{
    Task<AiSupportContextReadResult> ReadAsync(
        Guid memberId,
        IReadOnlyList<Guid> referencedOrderPublicIds,
        CancellationToken cancellationToken);
}

public enum AiSupportModelAnswerStatus
{
    Answered = 0,
    Unavailable = 1,
}

public sealed record AiSupportCitation(
    string SourceType,
    string SourceId,
    string Title,
    string VersionOrUpdatedAt);

public sealed record AiSupportModelUsage(
    string Model,
    int InputTokens,
    int OutputTokens);

public sealed record AiSupportModelAnswer(
    string? Answer,
    AiSupportModelAnswerStatus Status = AiSupportModelAnswerStatus.Answered,
    IReadOnlyList<AiSupportCitation>? ModelCitations = null,
    AiSupportModelUsage? Usage = null)
{
    public IReadOnlyList<AiSupportCitation> Citations { get; } = ModelCitations ?? [];
}

public interface IAiSupportModelClient
{
    Task<AiSupportModelAnswer> GenerateAsync(
        AiPromptEnvelope envelope,
        CancellationToken cancellationToken);
}

public sealed record AiSupportExecutionRequest(
    Guid MemberId,
    Guid RequestPublicId,
    string Message,
    SupportedLocale Locale,
    IReadOnlyList<Guid> ReferencedOrderPublicIds);

public enum AiSupportExecutionStatus
{
    Answered = 0,
    Rejected = 1,
}

public sealed record AiSupportExecutionResult(
    AiSupportExecutionStatus Status,
    string? Answer,
    IReadOnlyList<AiSupportCitation> Citations,
    AiSafetyReason Reason,
    AiFallback Fallback,
    int RemainingDailyMessages,
    DateTimeOffset ResetAtUtc,
    AiSupportModelUsage? ModelUsage = null);

public sealed class AiSupportOrchestrator
{
    private readonly IAiSupportAdmissionGate _admissionGate;
    private readonly IAiSupportContextReader _contextReader;
    private readonly IAiSupportModelClient _modelClient;

    public AiSupportOrchestrator(
        IAiSupportAdmissionGate admissionGate,
        IAiSupportContextReader contextReader,
        IAiSupportModelClient modelClient)
    {
        _admissionGate = admissionGate ?? throw new ArgumentNullException(nameof(admissionGate));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
    }

    public async Task<AiSupportExecutionResult> ExecuteAsync(
        AiSupportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var access = await _admissionGate.ReadAsync(request.MemberId, cancellationToken);
        var initialGate = EvaluateAccess(access);
        if (!initialGate.MayCallModel)
        {
            return Reject(initialGate.Reason, initialGate.Fallback, access);
        }

        var context = await _contextReader.ReadAsync(
            request.MemberId,
            request.ReferencedOrderPublicIds,
            cancellationToken);
        if (context.Status == AiSupportContextStatus.ResourceNotFound)
        {
            return Reject(
                AiSafetyReason.ResourceOwnershipMismatch,
                AiFallback.HumanSupport,
                access);
        }

        if (context.Status != AiSupportContextStatus.Allowed)
        {
            return Reject(
                AiSafetyReason.ServiceUnavailable,
                AiFallback.HumanSupport,
                access);
        }

        var preparation = AiPromptEnvelopeFactory.TryCreateSupport(
            request.Locale,
            request.Message,
            context.DataItems);
        if (preparation.Envelope is null)
        {
            return Reject(
                preparation.Reason,
                AiFallback.HumanSupport,
                access);
        }

        var reservation = await _admissionGate.TryReserveAsync(
            request.MemberId,
            request.RequestPublicId,
            cancellationToken);
        if (!reservation.IsReserved)
        {
            var reservedGate = EvaluateAccess(reservation.State);
            var reason = reservedGate.MayCallModel
                ? AiSafetyReason.ServiceUnavailable
                : reservedGate.Reason;
            return Reject(reason, AiFallback.HumanSupport, reservation.State);
        }

        var modelAnswer = await _modelClient.GenerateAsync(
            preparation.Envelope,
            cancellationToken);
        if (modelAnswer.Status != AiSupportModelAnswerStatus.Answered)
        {
            return Reject(
                AiSafetyReason.ServiceUnavailable,
                AiFallback.HumanSupport,
                reservation.State);
        }

        return new AiSupportExecutionResult(
            AiSupportExecutionStatus.Answered,
            modelAnswer.Answer,
            modelAnswer.Citations,
            AiSafetyReason.None,
            AiFallback.None,
            reservation.State.RemainingDailyMessages,
            reservation.State.ResetAtUtc,
            modelAnswer.Usage);
    }

    private static AiSupportRequestDecision EvaluateAccess(AiSupportAccessState access) =>
        AiSupportRequestGate.Evaluate(
            new AiSupportRequestContext(
                AiActorType.Member,
                IsAuthenticated: true,
                access.ConsentState,
                access.RemainingDailyMessages));

    private static void ValidateRequest(AiSupportExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);
        ArgumentNullException.ThrowIfNull(request.ReferencedOrderPublicIds);

        if (request.MemberId == Guid.Empty)
        {
            throw new ArgumentException("A trusted member identifier is required.", nameof(request));
        }

        if (request.RequestPublicId == Guid.Empty)
        {
            throw new ArgumentException("A request identifier is required.", nameof(request));
        }

        if (!Enum.IsDefined(request.Locale))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static AiSupportExecutionResult Reject(
        AiSafetyReason reason,
        AiFallback fallback,
        AiSupportAccessState access) =>
        new(
            AiSupportExecutionStatus.Rejected,
            Answer: null,
            Citations: [],
            reason,
            fallback,
            access.RemainingDailyMessages,
            access.ResetAtUtc);
}
