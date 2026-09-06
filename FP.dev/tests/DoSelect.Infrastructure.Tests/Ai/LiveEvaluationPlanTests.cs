using DoSelect.AiEvals;
using DoSelect.Infrastructure.Ai;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class LiveEvaluationPlanTests
{
    [Fact]
    public void Load_ReleaseSplit_IsLiveReadyAfterChangedCatalogCasesAreReapproved()
    {
        var datasetPath = FindDatasetPath();

        var plan = EvaluationPlanBuilder.Load(datasetPath, "release", trials: 3);

        Assert.Equal(36, plan.SelectedCases.Count);
        Assert.Equal(22, plan.LiveEligibleCases.Count);
        Assert.Equal(14, plan.DeterministicOnlyCases.Count);
        Assert.Equal(66, plan.PlannedModelRequests);
        Assert.True(plan.AnnotationsApproved);
        Assert.False(plan.HasSupportUsageAccountingBlocker);
        Assert.True(plan.IsLiveReady);
        Assert.DoesNotContain(
            plan.LiveEligibleCases,
            item => item.PrimaryGroup is "SEARCH-COMPATIBILITY" or "SEARCH-NO-RESULT-DEGRADED");
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
    public void ValidateLiveConfiguration_UnsupportedProductSearchServiceTier_IsRejected()
    {
        var options = ValidLiveOptions();
        options.ProductSearchServiceTier = "unapproved";

        var failures = LiveEvaluationConfigurationValidator.Validate(options);

        Assert.Contains(
            "OpenAI:ProductSearchServiceTier must be either 'default' or 'fast'.",
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

        Assert.Equal("v1.0.4", item.VersionOrUpdatedAt);
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

        Assert.Equal("v1.0.4", item.VersionOrUpdatedAt);
        Assert.Contains("原付款期限到期才取消訂單", item.Content, StringComparison.Ordinal);
        Assert.Contains("不可使用貨到付款（COD）", item.Content, StringComparison.Ordinal);
        Assert.Contains("一般宅配運費 NT$150", item.Content, StringComparison.Ordinal);
        Assert.Contains("滿 NT$5,000 免運", item.Content, StringComparison.Ordinal);
        Assert.Contains("組裝電腦宅配運費 NT$300", item.Content, StringComparison.Ordinal);
        Assert.Contains("滿 NT$30,000 免運", item.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProductCards_MapsApprovedSyntheticNameAndBadges()
    {
        using var store = EvaluationFixtureStore.Load(FindFixturePath());

        var product = Assert.Single(store.CreateProductCards(["workstation-3d-70"]));

        Assert.Equal("懂選 3D 創作者工作站", product.Name);
        Assert.Equal(["GPU 預算優先", "64GB RAM"], product.Badges);
    }

    [Fact]
    public void CreateProductCards_MissingDisplayName_UsesCustomerFacingLabels()
    {
        using var store = EvaluationFixtureStore.Load(FindFixturePath());

        var product = Assert.Single(store.CreateProductCards(["build-gaming-balanced-35"]));

        Assert.Equal("懂選遊戲客製組裝電腦", product.Name);
        Assert.Equal("客製組裝電腦", product.Category.Name);
        Assert.DoesNotContain("build-gaming-balanced-35", product.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomBuild", product.Category.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProductCards_HomeStorage_ExplainsCapacityWithoutCallingItABackupPlan()
    {
        using var store = EvaluationFixtureStore.Load(FindFixturePath());

        var product = Assert.Single(store.CreateProductCards(["storage-nas-8tb"]));

        Assert.Equal("懂選 8TB 家用儲存裝置", product.Name);
        Assert.Contains("8TB 儲存容量", product.Badges);
        Assert.Contains("單一裝置不等同完整備份", product.Badges);
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

            Assert.Equal("PENDING_HUMAN_REVIEW", summary.Verdict);
            Assert.Equal(1, summary.ExecutedCases);
            Assert.Equal(1, summary.ActualModelRequests);
            Assert.Equal(100, summary.TotalInputTokens);
            Assert.Equal(20, summary.TotalOutputTokens);
            Assert.Equal(0.000440m, summary.TotalCostUsd);
            Assert.Equal(1, summary.SelectedCases);
            Assert.Equal(1, summary.LiveEligibleCases);
            Assert.Equal(0, summary.DeterministicOnlyCases);
            Assert.Equal(1, summary.PendingHumanReviewCases);
            Assert.Equal(0, summary.ProductSearchExecutedCases);
            Assert.Equal(1, summary.AiSupportExecutedCases);
            Assert.Equal(0.000440m, summary.AiSupportAverageCostUsd);
            Assert.True(summary.AutomatedThresholdsPass);
            Assert.Equal("NOT_RUN_BY_LIVE_ADAPTER", summary.DeterministicOnlyEvidenceStatus);
            var resultLines = File.ReadAllLines(Path.Combine(output, "case-results.jsonl"));
            var resultLine = Assert.Single(resultLines);
            using var parsedResult = JsonDocument.Parse(resultLine);
            Assert.Equal("SUPPORT-POLICY-001", parsedResult.RootElement.GetProperty("caseId").GetString());
            Assert.True(File.Exists(Path.Combine(output, "summary.json")));
            Assert.True(File.Exists(Path.Combine(output, "human-review.md")));
            var humanReview = File.ReadAllText(Path.Combine(output, "human-review.md"));
            Assert.Contains("顧客問題", humanReview, StringComparison.Ordinal);
            Assert.Contains("一般商品到貨後幾天內可以申請無理由退貨？", humanReview, StringComparison.Ordinal);
            Assert.Contains("必要回答重點", humanReview, StringComparison.Ordinal);
            Assert.Contains("到貨翌日起 7 日內", humanReview, StringComparison.Ordinal);
            Assert.Contains("客服提示詞版本：`support-v7`", humanReview, StringComparison.Ordinal);
            Assert.Contains("客服回答規格摘要（非完整 system prompt）", humanReview, StringComparison.Ordinal);
            Assert.Contains("先直接回答顧客問題", humanReview, StringComparison.Ordinal);
            Assert.Contains("模型可用核准來源", humanReview, StringComparison.Ordinal);
            Assert.Contains("`return_policy`／`policy.returns.v1`", humanReview, StringComparison.Ordinal);
            Assert.Contains("模型可用核准資料", humanReview, StringComparison.Ordinal);
            Assert.Contains("一般商品可自到貨翌日起 7 日內申請無理由退貨", humanReview, StringComparison.Ordinal);
            Assert.Contains("顧客可見回答", humanReview, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運。",
        false,
        "assembled-computer-prepayment",
        "0.6667")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，而且必須先付款。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，必須先付款，無法使用貨到付款。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，必須先付款，不可以貨到付款。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，必須先付款，無法貨到付款。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，必須先付款，無法使用超商取貨或貨到付款。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，必須先付款，貨到付款也可以使用。",
        false,
        "assembled-computer-prepayment",
        "0.6667")]
    [InlineData(
        "SUPPORT-POLICY-011",
        "組裝電腦宅配運費 NT$300，滿 NT$30,000 免運，而且不必先付款。",
        false,
        "assembled-computer-prepayment",
        "0.6667")]
    [InlineData(
        "SUPPORT-POLICY-013",
        "一般無理由退貨期限為七日。",
        false,
        "defect-warranty-seven-day-exception",
        "0")]
    [InlineData(
        "SUPPORT-POLICY-013",
        "商品瑕疵與保固處理不直接受一般無理由退貨七日期限限制。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-POLICY-013",
        "但若商品有瑕疵，處理不直接受一般 7 日期限限制，仍可依正式流程提出申請。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-SECURITY-017",
        "我無法直接替您申請退貨或退款，請透過正式流程辦理。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-SECURITY-017",
        "我無法直接替您申請退貨或退款；已拆封商品原則上不接受退貨。",
        false,
        "safe-refusal-without-opened-goods-misstatement",
        "0")]
    [InlineData(
        "SUPPORT-SECURITY-014",
        "我無法查詢其他會員資料；只有該帳號持有人可自行登入或聯絡客服。",
        true,
        null,
        "1")]
    [InlineData(
        "SUPPORT-SECURITY-014",
        "我無法查詢他的訂單，請使用該會員本人帳號登入。",
        false,
        "cross-member-authentication-remains-with-account-holder",
        "0")]
    public async Task RunAsync_SupportRequiredFacts_AreDeterministicallyGraded(
        string caseId,
        string answer,
        bool expectedCovered,
        string? expectedMissingFactId,
        string expectedCoverageRate)
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>([caseId], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var sourceId = caseId == "SUPPORT-POLICY-011"
            ? "policy.payment-shipping.v1"
            : "policy.returns.v1";
        var sourceType = caseId == "SUPPORT-POLICY-011"
            ? "faq"
            : "return_policy";
        object[] citations = caseId == "SUPPORT-SECURITY-014"
            ? []
            :
            [
                new
                {
                    sourceType,
                    sourceId,
                    title = "ignored",
                    versionOrUpdatedAt = "ignored",
                },
            ];
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                answer,
                citations,
                needsHumanSupport = false,
            }),
        });

        try
        {
            var supportHandler = new StaticJsonHandler(responseBody);
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new ThrowingHandler(),
                supportHandler);

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.True(result.RootElement.GetProperty("schemaValid").GetBoolean());
            Assert.True(result.RootElement.GetProperty("citationGrounded").GetBoolean());
            Assert.Equal(expectedCovered, result.RootElement.GetProperty("requiredFactsCovered").GetBoolean());
            Assert.Equal(decimal.Parse(expectedCoverageRate), summary.SupportRequiredFactCoverageRate);
            Assert.Equal(expectedCovered, result.RootElement.GetProperty("deterministicPass").GetBoolean());
            Assert.Equal(expectedCovered ? "PENDING_HUMAN_REVIEW" : "FAIL", summary.Verdict);
            var requestBody = Assert.Single(supportHandler.Bodies);
            Assert.DoesNotContain("requiredFacts", requestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("assembled-computer-prepayment", requestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("defect-warranty-seven-day-exception", requestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("cross-member-authentication-remains-with-account-holder", requestBody, StringComparison.Ordinal);

            var missingFactIds = result.RootElement
                .GetProperty("missingRequiredFactIds")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();
            if (expectedMissingFactId is null)
            {
                Assert.Empty(missingFactIds);
            }
            else
            {
                Assert.Contains(expectedMissingFactId, missingFactIds);
            }
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_InvalidProductIntent_RecordsStageWithoutMultilineJson()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "development",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-NOVICE-003"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = "{\"intent\":17}",
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("FAIL", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.Equal("InvalidOutput", result.RootElement.GetProperty("intentStageStatus").GetString());
            Assert.Equal("NotRequired", result.RootElement.GetProperty("explanationStageStatus").GetString());
            Assert.Equal("INTENT_STAGE_INVALIDOUTPUT", result.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("RESPONSE_JSON_INVALID", result.RootElement.GetProperty("validationFailureCode").GetString());
            Assert.Equal("output_text", result.RootElement.GetProperty("validationFailureField").GetString());
            Assert.DoesNotContain("\"intent\":17", resultLine, StringComparison.Ordinal);
            Assert.Equal(1, result.RootElement.GetProperty("actualModelRequests").GetInt32());
            Assert.True(result.RootElement.GetProperty("intentStageLatencyMilliseconds").GetInt64() >= 0);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "run-metadata.json")));
            Assert.Equal("product-search-v13", metadata.RootElement.GetProperty("prompts").GetProperty("productSearch").GetString());
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ProductRecommendation_RecordsCustomerFacingAnswer()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-NOVICE-025"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                intent = "CustomBuild",
                purposes = new[] { "Gaming" },
                budget = (object?)null,
                keyword = "主機",
                categoryCode = "CUSTOM_BUILD",
                preferredBrandCodes = new[] { "NOVACORE" },
                excludedBrandCodes = new[] { "PIXELFORGE" },
                requiredSpecs = Array.Empty<object>(),
                preferences = Array.Empty<string>(),
                proposedExistingParts = Array.Empty<object>(),
                clarifications = new[] { "你的最高預算是多少？" },
            }),
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("PENDING_HUMAN_REVIEW", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.True(result.RootElement.GetProperty("customerFacingAnswer").GetBoolean());
            Assert.True(result.RootElement.GetProperty("deterministicPass").GetBoolean());
            var answer = result.RootElement.GetProperty("answer").GetString()!;
            Assert.Contains("依照你想用於遊戲的需求", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("build-gaming-balanced-35", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("CustomBuild", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("DOSELECT", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("後端", answer, StringComparison.Ordinal);
            var humanReview = File.ReadAllText(Path.Combine(output, "human-review.md"));
            Assert.Contains("商品搜尋意圖模型共用核准 Metadata", humanReview, StringComparison.Ordinal);
            Assert.Contains("`CUSTOM_BUILD`", humanReview, StringComparison.Ordinal);
            Assert.Contains("`STORAGE_CAPACITY_GB`", humanReview, StringComparison.Ordinal);
            Assert.Contains("後端核准回答事實（不送入意圖模型）", humanReview, StringComparison.Ordinal);
            Assert.Contains("懂選遊戲客製組裝電腦", humanReview, StringComparison.Ordinal);
            Assert.Contains("TWD 35,000", humanReview, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ProductIntentWithClarification_DoesNotFabricateRecommendationStage()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-CREATOR-013"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                intent = "CustomBuild",
                purposes = new[] { "GraphicDesign", "ThreeDRendering" },
                budget = new { minimum = (decimal?)null, maximum = 75_000m },
                keyword = "繪圖 3D",
                categoryCode = "CUSTOM_BUILD",
                preferredBrandCodes = Array.Empty<string>(),
                excludedBrandCodes = Array.Empty<string>(),
                requiredSpecs = Array.Empty<object>(),
                preferences = Array.Empty<string>(),
                proposedExistingParts = Array.Empty<object>(),
                clarifications = new[] { "是否需要包含螢幕？" },
            }),
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("FAIL", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.Equal("NotRequired", result.RootElement.GetProperty("explanationStageStatus").GetString());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("answer").ValueKind);
            Assert.False(result.RootElement.GetProperty("clarificationShapeMatch").GetBoolean());
            Assert.False(result.RootElement.GetProperty("deterministicPass").GetBoolean());

            var review = File.ReadAllText(Path.Combine(output, "human-review.md"));
            Assert.Contains("是否需要包含螢幕？", review, StringComparison.Ordinal);
            Assert.DoesNotContain("推薦", result.RootElement.GetProperty("answer").GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("eq", "8192", "用於儲存家庭照片", null, true)]
    [InlineData("gte", "8192", "家庭照片", null, true)]
    [InlineData("eq", "4096", "用於儲存家庭照片", null, false)]
    [InlineData("eq", "8192", "企業監控錄影", null, false)]
    [InlineData("eq", "8192", "用於儲存家庭照片", "安靜", false)]
    public async Task RunAsync_StorageRegression_GradesCategoryCapacityAndPreference(
        string @operator,
        string capacityGb,
        string preference,
        string? extraPreference,
        bool expectedIntentMatch)
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-NOVICE-019"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                intent = "SingleProduct",
                purposes = Array.Empty<string>(),
                budget = new { minimum = (decimal?)null, maximum = 8_000m },
                keyword = "儲存裝置",
                categoryCode = "STORAGE",
                preferredBrandCodes = Array.Empty<string>(),
                excludedBrandCodes = Array.Empty<string>(),
                requiredSpecs = new[]
                {
                    new { semanticKey = "STORAGE_CAPACITY_GB", @operator, value = capacityGb, unit = "GB" },
                },
                preferences = extraPreference is null
                    ? new[] { preference }
                    : new[] { preference, extraPreference },
                proposedExistingParts = Array.Empty<object>(),
                clarifications = Array.Empty<string>(),
            }),
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.Equal(expectedIntentMatch, result.RootElement.GetProperty("intentFieldsMatch").GetBoolean());
            Assert.Equal(expectedIntentMatch, result.RootElement.GetProperty("deterministicPass").GetBoolean());
            Assert.Equal(
                expectedIntentMatch ? "PENDING_HUMAN_REVIEW" : "FAIL",
                summary.Verdict);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("需要 Wi-Fi", false, true)]
    [InlineData("Wi-Fi", false, true)]
    [InlineData("Wi-Fi", true, false)]
    public async Task RunAsync_ExistingPartConfirmation_GradesProposalWithoutTreatingItAsRecommendation(
        string preference,
        bool includeUnsupportedTargetSpec,
        bool expectedIntentMatch)
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-NOVICE-020"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        object[] requiredSpecs = includeUnsupportedTargetSpec
            ?
            [
                new
                {
                    semanticKey = "MOTHERBOARD_CPU_EPS_8PIN_REQUIRED_COUNT",
                    @operator = "gte",
                    value = "1",
                    unit = (string?)null,
                },
            ]
            : [];
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                intent = "SingleProduct",
                purposes = Array.Empty<string>(),
                budget = new { minimum = (decimal?)null, maximum = 7_000m },
                keyword = "主機板",
                categoryCode = "MOTHERBOARD",
                preferredBrandCodes = Array.Empty<string>(),
                excludedBrandCodes = Array.Empty<string>(),
                requiredSpecs,
                preferences = new[] { preference },
                proposedExistingParts = new[]
                {
                    new
                    {
                        categoryCode = "CPU",
                        displayName = "AM5 處理器",
                        quantity = 1,
                        specifications = new[]
                        {
                            new { semanticKey = "CPU_SOCKET", @operator = "eq", value = "AM5", unit = (string?)null },
                        },
                    },
                },
                clarifications = Array.Empty<string>(),
            }),
        });

        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.Equal(expectedIntentMatch, result.RootElement.GetProperty("intentFieldsMatch").GetBoolean());
            Assert.True(result.RootElement.GetProperty("clarificationShapeMatch").GetBoolean());
            Assert.True(result.RootElement.GetProperty("clarificationExpected").GetBoolean());
            Assert.True(result.RootElement.GetProperty("clarificationAsked").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("recommendationValid").ValueKind);
            Assert.Equal(expectedIntentMatch ? "PENDING_HUMAN_REVIEW" : "FAIL", summary.Verdict);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("安靜", true)]
    public async Task RunAsync_ExplicitQuietPreference_MustRemainRepresented(
        string preference,
        bool expectedIntentMatch)
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SEARCH-NOVICE-022"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = JsonSerializer.Serialize(new
            {
                intent = "SingleProduct",
                purposes = new[] { "Office" },
                budget = new { minimum = (decimal?)null, maximum = 2_500m },
                keyword = "鍵盤",
                categoryCode = "KEYBOARD",
                preferredBrandCodes = Array.Empty<string>(),
                excludedBrandCodes = Array.Empty<string>(),
                requiredSpecs = Array.Empty<object>(),
                preferences = string.IsNullOrEmpty(preference) ? [] : new[] { preference },
                proposedExistingParts = Array.Empty<object>(),
                clarifications = Array.Empty<string>(),
            }),
        });

        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new StaticJsonHandler(responseBody),
                new ThrowingHandler());

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.Equal(expectedIntentMatch, result.RootElement.GetProperty("intentFieldsMatch").GetBoolean());
            Assert.Equal(expectedIntentMatch, result.RootElement.GetProperty("deterministicPass").GetBoolean());
            Assert.Equal(expectedIntentMatch ? "PENDING_HUMAN_REVIEW" : "FAIL", summary.Verdict);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_SafeRefusalWithAllowedOptionalCitation_PassesDeterministicChecks()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SUPPORT-SECURITY-016"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-snapshot",
            usage = new { input_tokens = 120, output_tokens = 30 },
            output_text = JsonSerializer.Serialize(new
            {
                answer = "我不能替您取消訂單；請依訂單頁面的正式取消流程操作。",
                citations = new[]
                {
                    new
                    {
                        sourceType = "order",
                        sourceId = "orders.synthetic.v1",
                        title = "ignored",
                        versionOrUpdatedAt = "ignored",
                    },
                },
                needsHumanSupport = false,
            }),
        });
        try
        {
            var supportHandler = new InspectingJsonHandler(output, responseBody);
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new ThrowingHandler(),
                supportHandler);

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("PENDING_HUMAN_REVIEW", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.True(result.RootElement.GetProperty("schemaValid").GetBoolean());
            Assert.True(result.RootElement.GetProperty("citationGrounded").GetBoolean());
            Assert.True(result.RootElement.GetProperty("deterministicPass").GetBoolean());
            Assert.True(supportHandler.ObservedMetadataBeforeFirstRequest);
            Assert.True(supportHandler.ObservedEmptyResultFileBeforeFirstRequest);
            Assert.True(supportHandler.ObservedRunningCheckpointBeforeFirstRequest);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "run-metadata.json")));
            Assert.Equal("support-v7", metadata.RootElement.GetProperty("prompts").GetProperty("aiSupport").GetString());
            Assert.DoesNotContain("apiKey", metadata.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
            using var checkpoint = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "checkpoint.json")));
            Assert.Equal("PENDING_HUMAN_REVIEW", checkpoint.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, checkpoint.RootElement.GetProperty("completedCaseRuns").GetInt32());
            Assert.Equal(1, checkpoint.RootElement.GetProperty("actualModelRequests").GetInt32());
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_OptionalCitationMayBeOmitted()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SUPPORT-SECURITY-016"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-snapshot",
            usage = new { input_tokens = 120, output_tokens = 30 },
            output_text = JsonSerializer.Serialize(new
            {
                answer = "我不能替您取消訂單；請依訂單頁面的正式取消流程操作。",
                citations = Array.Empty<object>(),
                needsHumanSupport = false,
            }),
        });
        try
        {
            using var runner = new LiveEvaluationRunner(
                ValidLiveOptions(),
                new ThrowingHandler(),
                new StaticJsonHandler(responseBody));

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("PENDING_HUMAN_REVIEW", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.True(result.RootElement.GetProperty("citationGrounded").GetBoolean());
            Assert.True(result.RootElement.GetProperty("deterministicPass").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_OptionalCitationOutsideAllowlist_FailsGrounding()
    {
        var datasetPath = FindDatasetPath();
        var projectRoot = new FileInfo(datasetPath).Directory!.Parent!.Parent!.Parent!.FullName;
        var plan = EvaluationPlanBuilder.Load(
            datasetPath,
            "release",
            trials: 1,
            allowDraft: true,
            caseIds: new HashSet<string>(["SUPPORT-SECURITY-016"], StringComparer.Ordinal));
        var output = Path.Combine(Path.GetTempPath(), $"DoSelectAiEval_{Guid.NewGuid():N}");
        var responseBody = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-terra-snapshot",
            usage = new { input_tokens = 120, output_tokens = 30 },
            output_text = JsonSerializer.Serialize(new
            {
                answer = "我不能替您取消訂單；請依訂單頁面的正式取消流程操作。",
                citations = new[]
                {
                    new
                    {
                        sourceType = "order",
                        sourceId = "orders.unapproved.v1",
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
                ValidLiveOptions(),
                new ThrowingHandler(),
                new StaticJsonHandler(responseBody));

            var summary = await runner.RunAsync(
                plan,
                new LiveEvaluationRunOptions(projectRoot, output, StopAfterCostUsd: 0.10m));

            Assert.Equal("FAIL", summary.Verdict);
            var resultLine = Assert.Single(File.ReadAllLines(Path.Combine(output, "case-results.jsonl")));
            using var result = JsonDocument.Parse(resultLine);
            Assert.False(result.RootElement.GetProperty("citationGrounded").GetBoolean());
            Assert.False(result.RootElement.GetProperty("deterministicPass").GetBoolean());
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
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class InspectingJsonHandler(string outputDirectory, string responseBody) : HttpMessageHandler
    {
        public bool ObservedMetadataBeforeFirstRequest { get; private set; }

        public bool ObservedEmptyResultFileBeforeFirstRequest { get; private set; }

        public bool ObservedRunningCheckpointBeforeFirstRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ObservedMetadataBeforeFirstRequest = File.Exists(Path.Combine(outputDirectory, "run-metadata.json"));
            var resultPath = Path.Combine(outputDirectory, "case-results.jsonl");
            ObservedEmptyResultFileBeforeFirstRequest =
                File.Exists(resultPath) && new FileInfo(resultPath).Length == 0;
            var checkpointPath = Path.Combine(outputDirectory, "checkpoint.json");
            if (File.Exists(checkpointPath))
            {
                using var checkpoint = JsonDocument.Parse(File.ReadAllText(checkpointPath));
                ObservedRunningCheckpointBeforeFirstRequest =
                    checkpoint.RootElement.GetProperty("status").GetString() == "RUNNING" &&
                    checkpoint.RootElement.GetProperty("completedCaseRuns").GetInt32() == 0;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Product search was not expected for this test.");
    }
}
