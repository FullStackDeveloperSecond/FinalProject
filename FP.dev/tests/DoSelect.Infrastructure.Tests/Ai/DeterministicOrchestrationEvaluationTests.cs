using System.Text.Json;
using DoSelect.AiEvals;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Builds;
using DoSelect.Domain.Members;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class DeterministicOrchestrationEvaluationTests
{
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 9, 4, 16, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> ReleaseCases()
    {
        var data = new TheoryData<string>();
        foreach (var item in LoadReleasePlan().DeterministicOnlyCases)
        {
            data.Add(item.CaseId);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReleaseCases))]
    public async Task ReleaseDeterministicOnlyCase_MatchesApplicationOrDomainContract(string caseId)
    {
        var item = Assert.Single(
            LoadReleasePlan().DeterministicOnlyCases,
            candidate => candidate.CaseId == caseId);

        if (item.PrimaryGroup == "SEARCH-COMPATIBILITY")
        {
            VerifyCompatibility(item);
            return;
        }

        if (item.Feature == "ai_support")
        {
            VerifySupportQuota(item);
            return;
        }

        await VerifyProductSearchAsync(item);
    }

    private static void VerifyCompatibility(EvaluationCasePlan item)
    {
        var result = CompatibilityEvaluator.Evaluate(
            CreateCompatibilityComponents(item.CaseId),
            new CompatibilityWarningSettings(20m, 10m, 35m, 0, 0),
            new CompatibilityRuleCatalog(
            [
                new CpuChipsetCompatibility("B650", "RYZEN_7000", RequiresBiosUpdate: false),
            ]));
        var expected = item.Expected.GetProperty("compatibility");
        var expectedStatus = expected.GetProperty("status").GetString();
        var actualStatus = result.Overall switch
        {
            CompatibilityOverall.Compatible => "Compatible",
            CompatibilityOverall.Blocked => "Incompatible",
            CompatibilityOverall.InsufficientData => "InsufficientData",
            CompatibilityOverall.Warning => "Warning",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

        Assert.Equal(expectedStatus, actualStatus);
        foreach (var expectedRule in ReadStrings(expected, "violatedRules"))
        {
            var domainRule = expectedRule switch
            {
                "psu_wattage_insufficient" or "psu_tier_unavailable" => CompatibilityRuleCodes.PsuCapacity,
                "psu_connector_missing" => CompatibilityRuleCodes.PsuConnectors,
                _ => throw new InvalidOperationException(
                    $"{item.CaseId} has no deterministic mapping for rule '{expectedRule}'."),
            };
            Assert.Contains(result.Results, finding => finding.RuleCode == domainRule);
        }

        if (item.CaseId == "SEARCH-COMPATIBILITY-013")
        {
            Assert.Equal(650m, result.RequiredPsuWatts);
            Assert.Empty(result.Results);
        }
    }

    private static async Task VerifyProductSearchAsync(EvaluationCasePlan item)
    {
        var isQuotaExceeded = item.ServiceCondition == "quota_exceeded";
        var admission = new StubAdmission(isQuotaExceeded ? 0 : 10);
        var intent = CreateIntent(item);
        var model = new StubModel(
            item.ServiceCondition == "refusal" ? null : intent,
            item.ServiceCondition == "refusal"
                ? AiProductSearchModelStatus.InvalidOutput
                : AiProductSearchModelStatus.Completed);
        var catalog = new StubCatalog();
        var store = new StubStore();
        var subject = new AiProductSearchOrchestrator(admission, model, catalog, store);

        var result = await subject.ExecuteAsync(new AiProductSearchExecutionRequest(
            StableGuid(item.CaseId, 90),
            new AiProductSearchActor(
                MemberUserId: null,
                AnonymousSessionKeyHash: Enumerable.Repeat((byte)7, 32).ToArray(),
                IsDemoAllowlisted: false),
            item.Message,
            SupportedLocale.ZhTw,
            ExistingParts: []));

        switch (item.ExpectedOutcome)
        {
            case "no_result":
                Assert.Equal(AiProductSearchExecutionStatus.NoResults, result.Status);
                Assert.Equal(AiFallback.None, result.Fallback);
                Assert.Empty(result.Recommendations);
                Assert.Empty(result.FallbackProducts);
                Assert.Equal(1, model.ParseCount);
                Assert.Equal(0, model.ExplainCount);
                Assert.Equal(1, catalog.CandidateReadCount);
                Assert.Equal(0, catalog.FallbackCount);
                Assert.Equal(1, store.SaveCount);
                AssertIntentConstraints(item, result.Intent);
                break;

            case "clarify":
                Assert.Equal(AiProductSearchExecutionStatus.Clarification, result.Status);
                Assert.NotEmpty(result.Clarifications);
                Assert.Equal(1, model.ParseCount);
                Assert.Equal(0, model.ExplainCount);
                Assert.Equal(0, catalog.CandidateReadCount);
                Assert.Equal(0, catalog.FallbackCount);
                Assert.Equal(1, store.SaveCount);
                break;

            case "fallback_keyword_search":
                Assert.Equal(AiProductSearchExecutionStatus.Degraded, result.Status);
                Assert.Equal(AiFallback.KeywordSearch, result.Fallback);
                Assert.Equal(AiSafetyReason.ServiceUnavailable, result.Reason);
                Assert.Equal(1, model.ParseCount);
                Assert.Equal(0, model.ExplainCount);
                Assert.Equal(0, catalog.CandidateReadCount);
                Assert.Equal(1, catalog.FallbackCount);
                Assert.Equal(1, store.SaveCount);
                Assert.True(store.LastWrite?.IsDegraded);
                break;

            case "reject_before_model":
                Assert.Equal(AiProductSearchExecutionStatus.Rejected, result.Status);
                Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
                Assert.Equal(AiFallback.KeywordSearch, result.Fallback);
                Assert.Equal(0, model.ParseCount);
                Assert.Equal(0, admission.ReservationCount);
                Assert.Equal(0, catalog.MetadataReadCount);
                Assert.Equal(0, catalog.CandidateReadCount);
                Assert.Equal(0, catalog.FallbackCount);
                Assert.Equal(0, store.SaveCount);
                break;

            default:
                throw new InvalidOperationException(
                    $"{item.CaseId} has unsupported deterministic outcome '{item.ExpectedOutcome}'.");
        }
    }

    private static void VerifySupportQuota(EvaluationCasePlan item)
    {
        Assert.Equal("SUPPORT-SECURITY-018", item.CaseId);
        Assert.Equal("quota_exceeded", item.ServiceCondition);
        Assert.Equal("human_support", item.ExpectedOutcome);
        Assert.Equal("forbidden", item.ModelCall);

        var result = AiSupportRequestGate.Evaluate(new AiSupportRequestContext(
            AiActorType.Member,
            IsAuthenticated: true,
            AiConsentState.Granted,
            RemainingDailyMessages: 0));

        Assert.False(result.MayCallModel);
        Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
        Assert.Equal(AiFallback.HumanSupport, result.Fallback);
    }

    private static AiProductSearchIntent CreateIntent(EvaluationCasePlan item)
    {
        var expected = item.Expected.GetProperty("intentFields");
        var type = Enum.Parse<AiProductSearchIntentType>(
            expected.GetProperty("intent").GetString()!,
            ignoreCase: false);
        var purposes = ReadStrings(expected, "purposes");
        var maximum = expected.TryGetProperty("budget.maxTwd", out var rawMaximum) &&
                      rawMaximum.ValueKind == JsonValueKind.Number
            ? rawMaximum.GetDecimal()
            : (decimal?)null;
        IReadOnlyList<AiRequiredSpec> requiredSpecs = item.CaseId switch
        {
            "SEARCH-CREATOR-016" =>
                [new AiRequiredSpec("gpu.count", "gte", "2", null)],
            "SEARCH-CREATOR-017" =>
                [new AiRequiredSpec("memory.capacity_gb", "gte", "128", "GB")],
            "SEARCH-NO-RESULT-DEGRADED-013" =>
                [new AiRequiredSpec("memory.module_capacity_gb", "eq", "24", "GB")],
            "SEARCH-NO-RESULT-DEGRADED-014" =>
            [
                new AiRequiredSpec("memory.type", "eq", "DDR4", null),
                new AiRequiredSpec("memory.type", "eq", "DDR5", null),
            ],
            _ => [],
        };
        var clarifications = item.CaseId == "SEARCH-NO-RESULT-DEGRADED-014"
            ? ["DDR4 與 DDR5 是互斥的硬性規格，請選擇其中一種。"]
            : Array.Empty<string>();

        return new AiProductSearchIntent(
            type,
            purposes,
            maximum is null ? null : new AiBudgetRange(null, maximum),
            Keyword: item.Message,
            CategoryCode: type == AiProductSearchIntentType.CustomBuild ? null : "PREBUILT_COMPUTER",
            PreferredBrandCodes: [],
            ExcludedBrandCodes: [],
            requiredSpecs,
            Preferences: [],
            ProposedExistingParts: [],
            clarifications);
    }

    private static void AssertIntentConstraints(
        EvaluationCasePlan item,
        AiProductSearchIntent? actual)
    {
        Assert.NotNull(actual);
        var expectedSpecs = item.Expected.GetProperty("intentFields")
            .TryGetProperty("requiredSpecs", out var specs)
            ? specs.EnumerateArray().Select(value => value.GetString()).ToArray()
            : [];
        if (expectedSpecs.Length > 0)
        {
            Assert.Equal(expectedSpecs.Length, actual.RequiredSpecs.Count);
        }
    }

    private static IReadOnlyList<CompatibilityComponent> CreateCompatibilityComponents(string caseId)
    {
        decimal? cpuPower = caseId == "SEARCH-COMPATIBILITY-016" ? null : 100m;
        var gpuPower = caseId switch
        {
            "SEARCH-COMPATIBILITY-014" => 325m,
            "SEARCH-COMPATIBILITY-015" => 1_024m,
            _ => 300m,
        };
        decimal? gpuRecommended = caseId == "SEARCH-COMPATIBILITY-017" ? null : 650m;
        var psuWatts = caseId == "SEARCH-COMPATIBILITY-015" ? 1_500m : 650m;
        var psu12Vhpwr = caseId == "SEARCH-COMPATIBILITY-018" ? 0m : 1m;

        return
        [
            Component(1, "CPU", Specs(
                Option("CPU_SOCKET", "AM5"),
                Option("CPU_GENERATION", "RYZEN_7000"),
                OptionalDecimal("POWER_DRAW_WATTS", cpuPower))),
            Component(2, "MOTHERBOARD", Specs(
                Option("CPU_SOCKET", "AM5"),
                Option("MOTHERBOARD_CHIPSET", "B650"),
                Option("MEMORY_TYPE", "DDR5"),
                Decimal("MEMORY_SLOT_COUNT", 4m),
                Decimal("MEMORY_MAX_CAPACITY_GB", 128m),
                Option("MOTHERBOARD_FORM_FACTOR", "ATX"),
                Decimal("M2_SLOT_COUNT", 2m),
                Decimal("SATA_PORT_COUNT", 4m),
                Decimal("MOTHERBOARD_CPU_EPS_8PIN_REQUIRED_COUNT", 1m),
                Decimal("POWER_DRAW_WATTS", 40m))),
            Component(3, "MEMORY", Specs(
                Option("MEMORY_TYPE", "DDR5"),
                Decimal("MEMORY_MODULE_COUNT", 2m),
                Decimal("MEMORY_KIT_CAPACITY_GB", 32m),
                Decimal("POWER_DRAW_WATTS", 8m))),
            Component(4, "GPU", Specs(
                Decimal("GPU_LENGTH_MM", 300m),
                OptionalDecimal("GPU_RECOMMENDED_PSU_WATTS", gpuRecommended),
                Decimal("GPU_PCIE_6_2PIN_REQUIRED_COUNT", 0m),
                Decimal("GPU_12VHPWR_REQUIRED_COUNT", 1m),
                Decimal("POWER_DRAW_WATTS", gpuPower))),
            Component(5, "STORAGE", Specs(
                Option("STORAGE_INTERFACE", "M2_NVME"),
                Decimal("POWER_DRAW_WATTS", 8m))),
            Component(6, "PSU", Specs(
                Decimal("PSU_RATED_WATTS", psuWatts),
                Decimal("PSU_PCIE_6_2PIN_COUNT", 4m),
                Decimal("PSU_12VHPWR_COUNT", psu12Vhpwr),
                Decimal("PSU_CPU_EPS_8PIN_COUNT", 2m),
                Option("PSU_FORM_FACTOR", "ATX"))),
            Component(7, "CASE", Specs(
                Options("CASE_SUPPORTED_MOTHERBOARD_FORM_FACTOR", "ATX", "MATX", "ITX"),
                Decimal("CASE_GPU_MAX_LENGTH_MM", 350m),
                Decimal("CASE_COOLER_MAX_HEIGHT_MM", 170m),
                Options("CASE_SUPPORTED_PSU_FORM_FACTOR", "ATX", "SFX"))),
            Component(8, "CPU_COOLER", Specs(
                Options("CPU_SOCKET", "AM4", "AM5"),
                Decimal("COOLER_HEIGHT_MM", 150m),
                Decimal("POWER_DRAW_WATTS", 20m))),
        ];
    }

    private static CompatibilityComponent Component(
        int stableId,
        string category,
        IReadOnlyDictionary<string, CompatibilitySpecification> specifications) =>
        new(StableGuid(category, stableId), category, 1, specifications);

    private static KeyValuePair<string, CompatibilitySpecification>? OptionalDecimal(
        string key,
        decimal? value) =>
        value.HasValue ? Decimal(key, value.Value) : null;

    private static KeyValuePair<string, CompatibilitySpecification> Decimal(string key, decimal value) =>
        new(key, CompatibilitySpecification.FromDecimal(value));

    private static KeyValuePair<string, CompatibilitySpecification> Option(string key, string value) =>
        new(key, CompatibilitySpecification.FromOption(value));

    private static KeyValuePair<string, CompatibilitySpecification> Options(
        string key,
        params string[] values) =>
        new(key, CompatibilitySpecification.FromOptions(values));

    private static IReadOnlyDictionary<string, CompatibilitySpecification> Specs(
        params KeyValuePair<string, CompatibilitySpecification>?[] values) =>
        values.Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static Guid StableGuid(string value, int suffix)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{value}:{suffix}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement owner, string propertyName) =>
        owner.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(item => item.GetString() ?? throw new JsonException())
                .ToArray()
            : [];

    private static EvaluationPlan LoadReleasePlan() =>
        EvaluationPlanBuilder.Load(FindDatasetPath(), "release", trials: 3);

    private static string FindDatasetPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "evals",
                "ai",
                "v1",
                "dataset.zh-TW.v1.jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find the AI evaluation dataset.");
    }

    private sealed class StubAdmission(int remaining) : IAiProductSearchAdmissionGate
    {
        public int ReservationCount { get; private set; }

        public Task<AiProductSearchAccessState> ReadAsync(
            AiProductSearchActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiProductSearchAccessState(
                remaining,
                ResetAtUtc,
                BudgetProtectionActive: false,
                IsDemoAllowlisted: false));

        public Task<AiProductSearchReservationResult> TryReserveAsync(
            AiProductSearchActor actor,
            Guid requestPublicId,
            CancellationToken cancellationToken)
        {
            ReservationCount++;
            return Task.FromResult(new AiProductSearchReservationResult(
                IsReserved: true,
                new AiProductSearchAccessState(
                    Math.Max(0, remaining - 1),
                    ResetAtUtc,
                    BudgetProtectionActive: false,
                    IsDemoAllowlisted: false)));
        }
    }

    private sealed class StubModel(
        AiProductSearchIntent? intent,
        AiProductSearchModelStatus status) : IAiProductSearchModelClient
    {
        public int ParseCount { get; private set; }
        public int ExplainCount { get; private set; }

        public Task<AiProductSearchIntentResult> ParseIntentAsync(
            string message,
            SupportedLocale locale,
            AiProductSearchMetadata metadata,
            CancellationToken cancellationToken)
        {
            ParseCount++;
            return Task.FromResult(new AiProductSearchIntentResult(
                status,
                intent,
                new AiSupportModelUsage("deterministic-fixture", 0, 0)));
        }

        public Task<AiProductSearchExplanationResult> ExplainAsync(
            AiProductSearchIntent parsedIntent,
            IReadOnlyList<ProductCardDto> approvedCandidates,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            ExplainCount++;
            return Task.FromResult(new AiProductSearchExplanationResult(
                AiProductSearchModelStatus.Completed,
                Reasons: [],
                Usage: null));
        }
    }

    private sealed class StubCatalog : IAiProductSearchCatalog
    {
        public int MetadataReadCount { get; private set; }
        public int CandidateReadCount { get; private set; }
        public int FallbackCount { get; private set; }

        public Task<AiProductSearchMetadata> ReadMetadataAsync(CancellationToken cancellationToken)
        {
            MetadataReadCount++;
            return Task.FromResult(new AiProductSearchMetadata(
                ["PREBUILT_COMPUTER", "CUSTOM_BUILD"],
                ["DOSELECT"],
                ["gpu.count", "memory.capacity_gb", "memory.module_capacity_gb", "memory.type"]));
        }

        public Task<AiProductSearchCandidateResult> FindCandidatesAsync(
            AiProductSearchIntent intent,
            IReadOnlyList<AiProductSearchExistingPart> existingParts,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            CandidateReadCount++;
            return Task.FromResult(new AiProductSearchCandidateResult(
                IsValid: true,
                AiSafetyReason.None,
                Candidates: [],
                Clarifications: []));
        }

        public Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
            string message,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            FallbackCount++;
            return Task.FromResult<IReadOnlyList<ProductCardDto>>([]);
        }
    }

    private sealed class StubStore : IAiProductSearchInteractionStore
    {
        public int SaveCount { get; private set; }
        public AiProductSearchInteractionWrite? LastWrite { get; private set; }

        public Task<bool> SaveAsync(
            AiProductSearchInteractionWrite interaction,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            LastWrite = interaction;
            return Task.FromResult(true);
        }
    }
}
