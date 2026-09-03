using DoSelect.AiEvals;
using DoSelect.Infrastructure.Ai;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class LiveEvaluationPlanTests
{
    [Fact]
    public void Load_ReleaseSplit_IsLiveReadyAfterAffectedPolicyCasesAreReapproved()
    {
        var datasetPath = FindDatasetPath();

        var plan = EvaluationPlanBuilder.Load(datasetPath, "release", trials: 3);

        Assert.Equal(36, plan.SelectedCases.Count);
        Assert.Equal(33, plan.LiveEligibleCases.Count);
        Assert.Equal(3, plan.DeterministicOnlyCases.Count);
        Assert.True(plan.AnnotationsApproved);
        Assert.False(plan.HasSupportUsageAccountingBlocker);
        Assert.True(plan.IsLiveReady);
        Assert.True(plan.PlannedModelRequests > plan.LiveEligibleCases.Count * plan.Trials);
    }

    [Fact]
    public void Load_MaximumCases_AppliesStableCaseIdOrdering()
    {
        var plan = EvaluationPlanBuilder.Load(
            FindDatasetPath(),
            "development",
            trials: 1,
            maximumCases: 2);

        Assert.Equal(
            ["SEARCH-COMPATIBILITY-001", "SEARCH-COMPATIBILITY-002"],
            plan.SelectedCases.Select(item => item.CaseId));
    }

    [Fact]
    public void Load_InvalidTrials_IsRejectedBeforeAnyLiveWork()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EvaluationPlanBuilder.Load(FindDatasetPath(), "release", trials: 0));
    }

    [Fact]
    public void Load_UnknownRequestedCaseId_IsRejectedInsteadOfSilentlyOmitted()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EvaluationPlanBuilder.Load(
                FindDatasetPath(),
                "development",
                trials: 1,
                caseIds: new HashSet<string>(
                    ["SEARCH-NOVICE-001", "UNKNOWN-CASE"],
                    StringComparer.Ordinal)));

        Assert.Contains("UNKNOWN-CASE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateLiveConfiguration_ZeroPrice_IsRejected()
    {
        var options = ValidLiveOptions();
        options.SupportOutputCostPerMillionTokens = 0m;

        var failures = LiveEvaluationConfigurationValidator.Validate(options);

        Assert.Contains(
            "OpenAI:SupportOutputCostPerMillionTokens is missing or not positive.",
            failures);
    }

    [Fact]
    public void ValidateLiveConfiguration_AllRequiredValuesPresent_HasNoFailures()
    {
        var failures = LiveEvaluationConfigurationValidator.Validate(ValidLiveOptions());

        Assert.Empty(failures);
    }

    [Fact]
    public void CreateSupportContext_ReturnPolicyContainsFactsRequiredByPolicyCases()
    {
        using var store = EvaluationFixtureStore.Load(FindFixturePath());

        var item = Assert.Single(store.CreateSupportContext(["policy.returns.v1"], string.Empty));

        Assert.Equal("v1.0.2", item.VersionOrUpdatedAt);
        Assert.Contains("訂單成立時保存的退貨政策版本快照", item.Content, StringComparison.Ordinal);
        Assert.Contains("AssemblyStarted", item.Content, StringComparison.Ordinal);
        Assert.Contains("NT$300 組裝費", item.Content, StringComparison.Ordinal);
        Assert.Contains("7 個日曆日內交寄", item.Content, StringComparison.Ordinal);
        Assert.Contains("不得核准或執行取消、退貨或退款", item.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSupportContext_PaymentShippingPolicyContainsExactFeesAndPaymentRules()
    {
        using var store = EvaluationFixtureStore.Load(FindFixturePath());

        var item = Assert.Single(store.CreateSupportContext(["policy.payment-shipping.v1"], string.Empty));

        Assert.Equal("v1.0.2", item.VersionOrUpdatedAt);
        Assert.Contains("原付款期限到期才取消訂單", item.Content, StringComparison.Ordinal);
        Assert.Contains("不可使用貨到付款（COD）", item.Content, StringComparison.Ordinal);
        Assert.Contains("一般宅配運費 NT$150", item.Content, StringComparison.Ordinal);
        Assert.Contains("滿 NT$5,000 免運", item.Content, StringComparison.Ordinal);
        Assert.Contains("組裝電腦宅配運費 NT$300", item.Content, StringComparison.Ordinal);
        Assert.Contains("滿 NT$30,000 免運", item.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ApprovedSupportCase_WritesUsageCostAndReviewArtifacts()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "development",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SUPPORT-POLICY-001"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                answer = "一般商品到貨翌日起七日內可提出申請，個案仍依訂單政策版本。",
                citations = new[]
                {
                    new
                    {
                        sourceType = "return_policy",
                        sourceId = "policy.returns.v1",
                        title = "ignored",
                        versionOrUpdatedAt = "ignored",
                    },
                },
                needsHumanSupport = false,
            }),
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                new OpenAiResponsesOptions
                {
                    ApiKey = "synthetic-key",
                    SupportModel = "gpt-5.6-terra",
                    SupportInputCostPerMillionTokens = 2m,
                    SupportOutputCostPerMillionTokens = 12m,
                    ProductSearchInputCostPerMillionTokens = 0.2m,
                    ProductSearchOutputCostPerMillionTokens = 1.2m,
                },
                new ThrowingHandler(),
                new StaticJsonHandler(responseBody));

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("PASS", summary.Verdict);
            Assert.Equal(1, summary.ExecutedCases);
            Assert.Equal(100, summary.TotalInputTokens);
            Assert.Equal(20, summary.TotalOutputTokens);
            Assert.Equal(0.000440m, summary.TotalCostUsd);
            Assert.True(File.Exists(Path.Combine(output, "case-results.jsonl")));
            Assert.True(File.Exists(Path.Combine(output, "summary.json")));
            Assert.True(File.Exists(Path.Combine(output, "human-review.md")));
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static string FindDatasetPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "evals", "ai", "v1", "dataset.zh-TW.v1.jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find the AI evaluation dataset.");
    }

    private static string FindFixturePath() =>
        Path.Combine(
            new FileInfo(FindDatasetPath()).Directory!.FullName,
            "context-fixtures.v1.json");

    private static OpenAiResponsesOptions ValidLiveOptions() =>
        new()
        {
            ApiKey = "synthetic-key",
            ProductSearchInputCostPerMillionTokens = 0.2m,
            ProductSearchOutputCostPerMillionTokens = 1.2m,
            SupportInputCostPerMillionTokens = 2m,
            SupportOutputCostPerMillionTokens = 12m,
        };

    private sealed class StaticJsonHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Product search was not expected for this test.");
    }
}
