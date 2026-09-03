using DoSelect.Application.Ai;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests.Ai;

public sealed class AiSupportOrchestratorTests
{
    private static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RequestPublicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrderPublicId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AiConsentState.Missing, AiSafetyReason.ConsentRequired)]
    [InlineData(AiConsentState.Denied, AiSafetyReason.ConsentDenied)]
    [InlineData(AiConsentState.Unavailable, AiSafetyReason.ServiceUnavailable)]
    public async Task ExecuteAsync_WithoutGrantedConsent_DoesNotReserveOrCallModel(
        AiConsentState consentState,
        AiSafetyReason expectedReason)
    {
        var model = new RecordingAiSupportModelClient();
        var admission = new StubAiSupportAdmissionGate(
            new AiSupportAccessState(consentState, 20, ResetAtUtc));
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(AiFallback.HumanSupport, result.Fallback);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithExhaustedQuota_DoesNotReserveOrCallModel()
    {
        var model = new RecordingAiSupportModelClient();
        var admission = new StubAiSupportAdmissionGate(
            new AiSupportAccessState(AiConsentState.Granted, 0, ResetAtUtc));
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBudgetProtectionIsActiveForNonDemo_DoesNotCallModel()
    {
        var model = new RecordingAiSupportModelClient();
        var admission = new StubAiSupportAdmissionGate(
            new AiSupportAccessState(
                AiConsentState.Granted,
                20,
                ResetAtUtc,
                BudgetProtectionActive: true,
                IsDemoAllowlisted: false));
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.BudgetProtectionActive, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Theory]
    [InlineData("請使用 access_token: [[SYNTHETIC_ACCESS_TOKEN]] 查訂單", AiSafetyReason.SecretDetected)]
    [InlineData("我的 Email: synthetic.customer@example.test", AiSafetyReason.PersonalDataDetected)]
    public async Task ExecuteAsync_WithUnsafeOutboundContent_DoesNotReserveOrCallModel(
        string message,
        AiSafetyReason expectedReason)
    {
        var model = new RecordingAiSupportModelClient();
        var admission = GrantedAdmission();
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest(message));

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
        Assert.DoesNotContain(message, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnownedReferencedOrder_DoesNotReserveOrCallModel()
    {
        var model = new RecordingAiSupportModelClient();
        var admission = GrantedAdmission();
        var context = new StubAiSupportContextReader(
            new AiSupportContextReadResult(
                AiSupportContextStatus.ResourceNotFound,
                DataItems: []));
        var subject = new AiSupportOrchestrator(admission, context, model, new StubInteractionStore());

        var result = await subject.ExecuteAsync(
            CreateRequest(referencedOrderPublicIds: [OrderPublicId]));

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.ResourceOwnershipMismatch, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllSafetyGatesPass_ReservesOnceBeforeCallingModel()
    {
        var modelUsage = new AiSupportModelUsage("gpt-5.6-terra-snapshot", 128, 42);
        var model = new RecordingAiSupportModelClient(
            "這是安全的測試回答。",
            usage: modelUsage);
        var admission = GrantedAdmission();
        var context = new StubAiSupportContextReader(
            new AiSupportContextReadResult(
                AiSupportContextStatus.Allowed,
                [
                    new AiSupportContextItem(
                        "order",
                        OrderPublicId.ToString("D"),
                        "ORD-TEST",
                        "2026-08-28T00:00:00.0000000Z",
                        "已授權且去識別化的訂單摘要"),
                ]));
        var subject = new AiSupportOrchestrator(admission, context, model, new StubInteractionStore());
        var request = CreateRequest(
            "請說明退貨流程",
            SupportedLocale.JaJp,
            [OrderPublicId]);

        var result = await subject.ExecuteAsync(request);

        Assert.Equal(AiSupportExecutionStatus.Answered, result.Status);
        Assert.Equal("這是安全的測試回答。", result.Answer);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(RequestPublicId, admission.LastRequestPublicId);
        Assert.Equal(1, model.CallCount);
        Assert.NotNull(model.LastEnvelope);
        Assert.Equal(SupportedLocale.JaJp, model.LastEnvelope.ResponseLocale);
        Assert.Equal(request.Message, model.LastEnvelope.UserMessage.Content);
        Assert.Equal("已授權且去識別化的訂單摘要", Assert.Single(model.LastEnvelope.DataItems).Content);
        Assert.Equal(MemberId, context.LastMemberId);
        Assert.Equal([OrderPublicId], context.LastReferencedOrderPublicIds);
        Assert.Equal(19, result.RemainingDailyMessages);
        Assert.Equal(modelUsage, result.ModelUsage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenModelIsUnavailable_DoesNotRefundReservation()
    {
        var usage = new AiSupportModelUsage("gpt-5.6-terra-snapshot", 96, 18);
        var model = new RecordingAiSupportModelClient(
            answer: null,
            AiSupportModelAnswerStatus.Unavailable,
            usage);
        var admission = GrantedAdmission();
        var interactionStore = new RecordingInteractionStore();
        var subject = new AiSupportOrchestrator(
            admission,
            new StubAiSupportContextReader(
                new AiSupportContextReadResult(AiSupportContextStatus.Allowed, [])),
            model,
            interactionStore);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.ServiceUnavailable, result.Reason);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(1, model.CallCount);
        Assert.Equal(19, result.RemainingDailyMessages);
        Assert.Equal(usage, interactionStore.LastWrite?.ModelUsage);
        Assert.True(interactionStore.LastWrite?.IsDegraded);
    }

    [Fact]
    public async Task ExecuteAsync_WithLastRemainingMessage_CallsModelAfterReservation()
    {
        var model = new RecordingAiSupportModelClient("最後一則安全回答");
        var admission = new StubAiSupportAdmissionGate(
            new AiSupportAccessState(AiConsentState.Granted, 1, ResetAtUtc));
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Answered, result.Status);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(1, model.CallCount);
        Assert.Equal(0, result.RemainingDailyMessages);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentReservationExhaustsQuota_DoesNotCallModel()
    {
        var model = new RecordingAiSupportModelClient();
        var initialState = new AiSupportAccessState(AiConsentState.Granted, 1, ResetAtUtc);
        var admission = new StubAiSupportAdmissionGate(
            initialState,
            new AiSupportReservationResult(
                IsReserved: false,
                initialState with { RemainingDailyMessages = 0 }));
        var subject = CreateSubject(admission, model);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(0, model.CallCount);
    }

    private static AiSupportOrchestrator CreateSubject(
        StubAiSupportAdmissionGate admission,
        RecordingAiSupportModelClient model) =>
        new(
            admission,
            new StubAiSupportContextReader(
                new AiSupportContextReadResult(AiSupportContextStatus.Allowed, DataItems: [])),
            model,
            new StubInteractionStore());

    private static StubAiSupportAdmissionGate GrantedAdmission() =>
        new(new AiSupportAccessState(AiConsentState.Granted, 20, ResetAtUtc));

    private static AiSupportExecutionRequest CreateRequest(
        string message = "請說明退貨流程",
        SupportedLocale locale = SupportedLocale.ZhTw,
        IReadOnlyList<Guid>? referencedOrderPublicIds = null) =>
        new(
            MemberId,
            RequestPublicId,
            ConversationPublicId: null,
            message,
            locale,
            referencedOrderPublicIds ?? [],
            ReferencedSupportTicketPublicIds: []);

    private sealed class StubAiSupportAdmissionGate : IAiSupportAdmissionGate
    {
        private readonly AiSupportAccessState _state;
        private readonly AiSupportReservationResult? _reservationResult;

        public StubAiSupportAdmissionGate(
            AiSupportAccessState state,
            AiSupportReservationResult? reservationResult = null)
        {
            _state = state;
            _reservationResult = reservationResult;
        }

        public int ReservationCount { get; private set; }

        public Guid? LastRequestPublicId { get; private set; }

        public Task<AiSupportAccessState> ReadAsync(
            Guid memberId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_state);

        public Task<AiSupportReservationResult> TryReserveAsync(
            Guid memberId,
            Guid requestPublicId,
            CancellationToken cancellationToken)
        {
            ReservationCount++;
            LastRequestPublicId = requestPublicId;
            if (_reservationResult is not null)
            {
                return Task.FromResult(_reservationResult);
            }

            var reserved = _state.ConsentState == AiConsentState.Granted &&
                _state.RemainingDailyMessages > 0;
            var state = reserved
                ? _state with { RemainingDailyMessages = _state.RemainingDailyMessages - 1 }
                : _state;
            return Task.FromResult(new AiSupportReservationResult(reserved, state));
        }
    }

    private sealed class StubAiSupportContextReader : IAiSupportContextReader
    {
        private readonly AiSupportContextReadResult _result;

        public StubAiSupportContextReader(AiSupportContextReadResult result)
        {
            _result = result;
        }

        public Guid? LastMemberId { get; private set; }

        public IReadOnlyList<Guid>? LastReferencedOrderPublicIds { get; private set; }

        public Task<AiSupportContextReadResult> ReadAsync(
            Guid memberId,
            Guid? conversationPublicId,
            IReadOnlyList<Guid> referencedOrderPublicIds,
            IReadOnlyList<Guid> referencedSupportTicketPublicIds,
            CancellationToken cancellationToken)
        {
            LastMemberId = memberId;
            LastReferencedOrderPublicIds = referencedOrderPublicIds;
            return Task.FromResult(_result);
        }
    }


    private sealed class StubInteractionStore : IAiSupportInteractionStore
    {
        public Task<AiSupportInteractionWriteResult> SaveAsync(
            AiSupportInteractionWrite interaction,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiSupportInteractionWriteResult(
                true,
                interaction.ConversationPublicId ?? Guid.Parse("44444444-4444-4444-4444-444444444444")));
    }

    private sealed class RecordingInteractionStore : IAiSupportInteractionStore
    {
        public AiSupportInteractionWrite? LastWrite { get; private set; }

        public Task<AiSupportInteractionWriteResult> SaveAsync(
            AiSupportInteractionWrite interaction,
            CancellationToken cancellationToken)
        {
            LastWrite = interaction;
            return Task.FromResult(new AiSupportInteractionWriteResult(
                true,
                interaction.ConversationPublicId ?? Guid.Parse("44444444-4444-4444-4444-444444444444")));
        }
    }

    private sealed class RecordingAiSupportModelClient : IAiSupportModelClient
    {
        private readonly string? _answer;
        private readonly AiSupportModelAnswerStatus _status;
        private readonly AiSupportModelUsage? _usage;

        public RecordingAiSupportModelClient(
            string? answer = "unused",
            AiSupportModelAnswerStatus status = AiSupportModelAnswerStatus.Answered,
            AiSupportModelUsage? usage = null)
        {
            _answer = answer;
            _status = status;
            _usage = usage;
        }

        public int CallCount { get; private set; }

        public AiPromptEnvelope? LastEnvelope { get; private set; }

        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastEnvelope = envelope;
            return Task.FromResult(new AiSupportModelAnswer(
                _answer,
                _status,
                Usage: _usage));
        }
    }
}
