namespace DoSelect.Application.Ai;

public sealed record AiSupportAccessState(
    AiConsentState ConsentState,
    int RemainingDailyMessages,
    DateTimeOffset ResetAtUtc);

public interface IAiSupportAccessReader
{
    Task<AiSupportAccessState> ReadAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}

public sealed record AiSupportModelAnswer(string Answer);

public interface IAiSupportModelClient
{
    Task<AiSupportModelAnswer> GenerateAsync(
        AiPromptEnvelope envelope,
        CancellationToken cancellationToken);
}

public sealed record AiSupportExecutionRequest(
    Guid MemberId,
    string Message,
    IReadOnlyList<string> DataItems);

public enum AiSupportExecutionStatus
{
    Answered = 0,
    Rejected = 1,
}

public sealed record AiSupportExecutionResult(
    AiSupportExecutionStatus Status,
    string? Answer,
    AiSafetyReason Reason,
    AiFallback Fallback,
    int RemainingDailyMessages,
    DateTimeOffset ResetAtUtc);

public sealed class AiSupportOrchestrator
{
    private readonly IAiSupportAccessReader _accessReader;
    private readonly IAiSupportModelClient _modelClient;

    public AiSupportOrchestrator(
        IAiSupportAccessReader accessReader,
        IAiSupportModelClient modelClient)
    {
        _accessReader = accessReader ?? throw new ArgumentNullException(nameof(accessReader));
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
    }

    public async Task<AiSupportExecutionResult> ExecuteAsync(
        AiSupportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);
        ArgumentNullException.ThrowIfNull(request.DataItems);

        if (request.MemberId == Guid.Empty)
        {
            throw new ArgumentException("A trusted member identifier is required.", nameof(request));
        }

        var access = await _accessReader.ReadAsync(request.MemberId, cancellationToken);
        var gate = AiSupportRequestGate.Evaluate(
            new AiSupportRequestContext(
                AiActorType.Member,
                IsAuthenticated: true,
                access.ConsentState,
                access.RemainingDailyMessages));

        if (!gate.MayCallModel)
        {
            return Reject(gate.Reason, gate.Fallback, access);
        }

        var preparation = AiPromptEnvelopeFactory.TryCreateSupport(
            request.Message,
            request.DataItems);
        if (preparation.Envelope is null)
        {
            return Reject(
                preparation.Reason,
                AiFallback.HumanSupport,
                access);
        }

        var modelAnswer = await _modelClient.GenerateAsync(
            preparation.Envelope,
            cancellationToken);

        return new AiSupportExecutionResult(
            AiSupportExecutionStatus.Answered,
            modelAnswer.Answer,
            AiSafetyReason.None,
            AiFallback.None,
            Math.Max(0, access.RemainingDailyMessages - 1),
            access.ResetAtUtc);
    }

    private static AiSupportExecutionResult Reject(
        AiSafetyReason reason,
        AiFallback fallback,
        AiSupportAccessState access) =>
        new(
            AiSupportExecutionStatus.Rejected,
            Answer: null,
            reason,
            fallback,
            access.RemainingDailyMessages,
            access.ResetAtUtc);
}
