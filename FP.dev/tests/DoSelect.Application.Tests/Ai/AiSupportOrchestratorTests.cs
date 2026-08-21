using DoSelect.Application.Ai;

namespace DoSelect.Application.Tests.Ai;

public sealed class AiSupportOrchestratorTests
{
    private static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AiConsentState.Missing, AiSafetyReason.ConsentRequired)]
    [InlineData(AiConsentState.Denied, AiSafetyReason.ConsentDenied)]
    public async Task ExecuteAsync_WithoutGrantedConsent_DoesNotCallModel(
        AiConsentState consentState,
        AiSafetyReason expectedReason)
    {
        var model = new RecordingAiSupportModelClient();
        var subject = CreateSubject(model, consentState, remainingDailyMessages: 20);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(AiFallback.HumanSupport, result.Fallback);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithExhaustedQuota_DoesNotCallModel()
    {
        var model = new RecordingAiSupportModelClient();
        var subject = CreateSubject(model, AiConsentState.Granted, remainingDailyMessages: 0);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
        Assert.Equal(AiFallback.HumanSupport, result.Fallback);
        Assert.Equal(0, model.CallCount);
    }

    [Theory]
    [InlineData("請使用 access_token: [[SYNTHETIC_ACCESS_TOKEN]] 查訂單", AiSafetyReason.SecretDetected)]
    [InlineData("我的 Email: synthetic.customer@example.test", AiSafetyReason.PersonalDataDetected)]
    public async Task ExecuteAsync_WithUnsafeOutboundContent_DoesNotCallModel(
        string message,
        AiSafetyReason expectedReason)
    {
        var model = new RecordingAiSupportModelClient();
        var subject = CreateSubject(model, AiConsentState.Granted, remainingDailyMessages: 20);

        var result = await subject.ExecuteAsync(CreateRequest(message));

        Assert.Equal(AiSupportExecutionStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(AiFallback.HumanSupport, result.Fallback);
        Assert.Equal(0, model.CallCount);
        Assert.DoesNotContain(message, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllSafetyGatesPass_CallsModelOnceWithTrustedEnvelope()
    {
        var model = new RecordingAiSupportModelClient("這是安全的測試回答。");
        var access = new StubAiSupportAccessReader(
            new AiSupportAccessState(AiConsentState.Granted, 20, ResetAtUtc));
        var subject = new AiSupportOrchestrator(access, model);
        var request = CreateRequest("請說明退貨流程");

        var result = await subject.ExecuteAsync(request);

        Assert.Equal(AiSupportExecutionStatus.Answered, result.Status);
        Assert.Equal("這是安全的測試回答。", result.Answer);
        Assert.Equal(1, model.CallCount);
        Assert.Equal(MemberId, access.LastMemberId);
        Assert.NotNull(model.LastEnvelope);
        Assert.Equal(request.Message, model.LastEnvelope.UserMessage.Content);
        Assert.All(model.LastEnvelope.AllowedToolNames, toolName =>
            Assert.Contains(AiToolCatalog.Definitions, definition => definition.Name == toolName));
        Assert.Equal(19, result.RemainingDailyMessages);
        Assert.Equal(ResetAtUtc, result.ResetAtUtc);
    }

    private static AiSupportOrchestrator CreateSubject(
        RecordingAiSupportModelClient model,
        AiConsentState consentState,
        int remainingDailyMessages)
    {
        var access = new StubAiSupportAccessReader(
            new AiSupportAccessState(consentState, remainingDailyMessages, ResetAtUtc));
        return new AiSupportOrchestrator(access, model);
    }

    private static AiSupportExecutionRequest CreateRequest(
        string message = "請說明退貨流程") =>
        new(MemberId, message, DataItems: []);

    private sealed class StubAiSupportAccessReader : IAiSupportAccessReader
    {
        private readonly AiSupportAccessState _state;

        public StubAiSupportAccessReader(AiSupportAccessState state)
        {
            _state = state;
        }

        public Guid? LastMemberId { get; private set; }

        public Task<AiSupportAccessState> ReadAsync(
            Guid memberId,
            CancellationToken cancellationToken)
        {
            LastMemberId = memberId;
            return Task.FromResult(_state);
        }
    }

    private sealed class RecordingAiSupportModelClient : IAiSupportModelClient
    {
        private readonly string _answer;

        public RecordingAiSupportModelClient(string answer = "unused")
        {
            _answer = answer;
        }

        public int CallCount { get; private set; }

        public AiPromptEnvelope? LastEnvelope { get; private set; }

        public Task<AiSupportModelAnswer> GenerateAsync(
            AiPromptEnvelope envelope,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastEnvelope = envelope;
            return Task.FromResult(new AiSupportModelAnswer(_answer));
        }
    }
}
