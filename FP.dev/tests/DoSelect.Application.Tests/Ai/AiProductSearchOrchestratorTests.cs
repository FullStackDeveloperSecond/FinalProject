using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Tests.Ai;

public sealed class AiProductSearchOrchestratorTests
{
    private static readonly DateTimeOffset ResetAtUtc =
        new(2026, 8, 30, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ApprovedCandidatesAndReasons_ReturnsGroundedRecommendations()
    {
        var product = CreateProduct();
        var admission = new StubAdmission();
        var model = new StubModel(CreateIntent(),
            [new AiProductRecommendationReason(product.DefaultSkuPublicId, "符合五萬元預算，且是已核准候選。")]);
        var catalog = new StubCatalog(
            [new AiProductSearchCandidate(product, AiCompatibilityStatus.NotRequired, [])]);
        var store = new StubStore();
        var subject = new AiProductSearchOrchestrator(admission, model, catalog, store);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Recommendations, result.Status);
        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(product.DefaultSkuPublicId, recommendation.Product.DefaultSkuPublicId);
        Assert.Contains("已核准候選", recommendation.Reason);
        Assert.Equal(1, admission.ReservationCount);
        Assert.Equal(1, model.ParseCount);
        Assert.Equal(1, model.ExplainCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(29, result.RemainingDailyRequests);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedCustomBuild_ReturnsCompletePricedBuildWithGroundedReasons()
    {
        var cpu = CreateProduct() with
        {
            DefaultSkuPublicId = Guid.Parse("50000000-0000-0000-0000-000000000005"),
            Name = "懂選處理器",
            Category = new ProductCategoryRef("CPU", "處理器"),
            Price = new ProductPrice(10_000, null, "TWD"),
        };
        var intent = CreateIntent() with
        {
            Intent = AiProductSearchIntentType.CustomBuild,
            CategoryCode = null,
        };
        var model = new StubModel(
            intent,
            [new AiProductRecommendationReason(cpu.DefaultSkuPublicId, "符合用途與新購預算。")]);
        var customBuild = new AiCustomBuildCandidate(
            Components:
            [
                new AiCustomBuildComponentCandidate(
                    cpu,
                    cpu.DefaultSkuPublicId,
                    "catalogSku",
                    "CPU",
                    cpu.Name,
                    1,
                    IsExistingPart: false),
                new AiCustomBuildComponentCandidate(
                    Product: null,
                    SkuPublicId: null,
                    "structuredManual",
                    "GPU",
                    "使用者既有顯示卡",
                    1,
                    IsExistingPart: true),
            ],
            PurchaseSubtotal: 10_000,
            AssemblyFee: 300,
            PurchaseTotal: 10_300,
            Currency: "TWD",
            AiCompatibilityStatus.Compatible,
            CompatibilityMessageKeys: []);
        var catalog = new StubCatalog([], customBuild: customBuild);
        var subject = new AiProductSearchOrchestrator(
            new StubAdmission(),
            model,
            catalog,
            new StubStore());

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Recommendations, result.Status);
        Assert.Empty(result.Recommendations);
        Assert.NotNull(result.CustomBuild);
        Assert.Equal(10_300, result.CustomBuild.PurchaseTotal);
        Assert.Equal(300, result.CustomBuild.AssemblyFee);
        Assert.Equal("符合用途與新購預算。", result.CustomBuild.Components[0].Reason);
        Assert.Null(result.CustomBuild.Components[1].Reason);
        Assert.True(result.CustomBuild.Components[1].IsExistingPart);
    }

    [Fact]
    public async Task ExecuteAsync_AllExistingCustomBuild_DoesNotRequestEmptyAiExplanations()
    {
        var intent = CreateIntent() with
        {
            Intent = AiProductSearchIntentType.CustomBuild,
            CategoryCode = null,
        };
        var model = new StubModel(intent, []);
        var customBuild = new AiCustomBuildCandidate(
            Components:
            [
                new AiCustomBuildComponentCandidate(
                    Product: null,
                    SkuPublicId: null,
                    "structuredManual",
                    "CPU",
                    "使用者既有處理器",
                    1,
                    IsExistingPart: true),
            ],
            PurchaseSubtotal: 0,
            AssemblyFee: 300,
            PurchaseTotal: 300,
            Currency: "TWD",
            AiCompatibilityStatus.Compatible,
            CompatibilityMessageKeys: []);
        var subject = new AiProductSearchOrchestrator(
            new StubAdmission(),
            model,
            new StubCatalog([], customBuild: customBuild),
            new StubStore());

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Recommendations, result.Status);
        Assert.NotNull(result.CustomBuild);
        Assert.Equal(300, result.CustomBuild.PurchaseTotal);
        Assert.Equal(0, model.ExplainCount);
    }

    [Fact]
    public async Task ExecuteAsync_QuotaExhausted_DoesNotCallModelOrCatalog()
    {
        var admission = new StubAdmission(remaining: 0);
        var model = new StubModel(CreateIntent(), []);
        var catalog = new StubCatalog([]);
        var subject = new AiProductSearchOrchestrator(admission, model, catalog, new StubStore());

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.DailyQuotaExceeded, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.ParseCount);
        Assert.Equal(0, catalog.MetadataReadCount);
    }

    [Theory]
    [InlineData("Email: person@example.test", AiSafetyReason.PersonalDataDetected)]
    [InlineData("api_key=synthetic-secret", AiSafetyReason.SecretDetected)]
    public async Task ExecuteAsync_SensitiveContent_DoesNotReserveOrCallModel(
        string message,
        AiSafetyReason expectedReason)
    {
        var admission = new StubAdmission();
        var model = new StubModel(CreateIntent(), []);
        var subject = new AiProductSearchOrchestrator(
            admission,
            model,
            new StubCatalog([]),
            new StubStore());

        var result = await subject.ExecuteAsync(CreateRequest(message));

        Assert.Equal(AiProductSearchExecutionStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(0, admission.ReservationCount);
        Assert.Equal(0, model.ParseCount);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidModelOutput_UsesKeywordFallbackWithoutCandidateQuery()
    {
        var fallback = CreateProduct();
        var admission = new StubAdmission();
        var model = new StubModel(intent: null, reasons: [], AiProductSearchModelStatus.InvalidOutput);
        var catalog = new StubCatalog([], [fallback]);
        var store = new StubStore();
        var subject = new AiProductSearchOrchestrator(admission, model, catalog, store);

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Degraded, result.Status);
        Assert.Equal(AiFallback.KeywordSearch, result.Fallback);
        Assert.Equal(fallback.DefaultSkuPublicId, Assert.Single(result.FallbackProducts).DefaultSkuPublicId);
        Assert.Equal(0, catalog.CandidateReadCount);
        Assert.Equal(1, catalog.FallbackCount);
        Assert.True(store.LastWrite?.IsDegraded);
    }

    [Fact]
    public async Task ExecuteAsync_ExplanationReferencesIncompleteCandidateSet_Degrades()
    {
        var product = CreateProduct();
        var admission = new StubAdmission();
        var model = new StubModel(CreateIntent(), []);
        var catalog = new StubCatalog(
            [new AiProductSearchCandidate(product, AiCompatibilityStatus.NotRequired, [])],
            [product]);
        var subject = new AiProductSearchOrchestrator(admission, model, catalog, new StubStore());

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Degraded, result.Status);
        Assert.Empty(result.Recommendations);
        Assert.Single(result.FallbackProducts);
    }

    [Fact]
    public async Task ExecuteAsync_InteractionPersistenceFails_FailsClosed()
    {
        var product = CreateProduct();
        var model = new StubModel(
            CreateIntent(),
            [new AiProductRecommendationReason(product.DefaultSkuPublicId, "核准候選")]);
        var subject = new AiProductSearchOrchestrator(
            new StubAdmission(),
            model,
            new StubCatalog([new AiProductSearchCandidate(product, AiCompatibilityStatus.NotRequired, [])]),
            new StubStore(succeeds: false));

        var result = await subject.ExecuteAsync(CreateRequest());

        Assert.Equal(AiProductSearchExecutionStatus.Rejected, result.Status);
        Assert.Equal(AiSafetyReason.ServiceUnavailable, result.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_UnconfirmedProposedExistingPart_RequiresConfirmationBeforeCatalogQuery()
    {
        var intent = CreateIntent() with
        {
            ProposedExistingParts =
            [
                new AiProductSearchProposedPart(
                    "CPU",
                    "AM5 CPU",
                    [new AiRequiredSpec("CPU_SOCKET", "eq", "AM5", null)],
                    1),
            ],
        };
        var model = new StubModel(intent, []);
        var catalog = new StubCatalog([]);
        var store = new StubStore();
        var subject = new AiProductSearchOrchestrator(
            new StubAdmission(),
            model,
            catalog,
            store);

        var result = await subject.ExecuteAsync(CreateRequest("已有 AM5 CPU，想找主機板"));

        Assert.Equal(AiProductSearchExecutionStatus.Clarification, result.Status);
        Assert.Contains(result.Clarifications, question => question.Contains("確認", StringComparison.Ordinal));
        Assert.Equal(0, catalog.CandidateReadCount);
        Assert.Equal(0, model.ExplainCount);
        Assert.Equal(1, store.SaveCount);
    }

    private static AiProductSearchExecutionRequest CreateRequest(
        string message = "五萬元剪輯 4K 影片") =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new AiProductSearchActor(
                MemberUserId: "20000000-0000-0000-0000-000000000002",
                AnonymousSessionKeyHash: null,
                IsDemoAllowlisted: false),
            message,
            SupportedLocale.ZhTw,
            ExistingParts: []);

    private static AiProductSearchIntent CreateIntent() =>
        new(
            AiProductSearchIntentType.PrebuiltComputer,
            ["VideoEditing"],
            new AiBudgetRange(null, 50_000),
            Keyword: "剪輯",
            CategoryCode: "PREBUILT_COMPUTER",
            PreferredBrandCodes: [],
            ExcludedBrandCodes: [],
            RequiredSpecs: [],
            Preferences: ["安靜"],
            ProposedExistingParts: [],
            Clarifications: []);

    private static ProductCardDto CreateProduct() =>
        new(
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            Guid.Parse("40000000-0000-0000-0000-000000000004"),
            "PC-CREATOR",
            "PC-CREATOR-01",
            "創作者工作站",
            new ProductBrandRef("DOSELECT", "懂選"),
            new ProductCategoryRef("PREBUILT_COMPUTER", "套裝電腦"),
            new ProductPrice(49_000, null, "TWD"),
            ProductAvailabilityCodes.InStock,
            null,
            []);

    private sealed class StubAdmission(int remaining = 30) : IAiProductSearchAdmissionGate
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
                true,
                new AiProductSearchAccessState(
                    Math.Max(0, remaining - 1),
                    ResetAtUtc,
                    BudgetProtectionActive: false,
                    IsDemoAllowlisted: false)));
        }
    }

    private sealed class StubModel(
        AiProductSearchIntent? intent,
        IReadOnlyList<AiProductRecommendationReason> reasons,
        AiProductSearchModelStatus parseStatus = AiProductSearchModelStatus.Completed) : IAiProductSearchModelClient
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
                parseStatus,
                intent,
                new AiSupportModelUsage("search-model", 10, 4)));
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
                reasons,
                new AiSupportModelUsage("search-model", 20, 8)));
        }
    }

    private sealed class StubCatalog(
        IReadOnlyList<AiProductSearchCandidate> candidates,
        IReadOnlyList<ProductCardDto>? fallback = null,
        AiCustomBuildCandidate? customBuild = null) : IAiProductSearchCatalog
    {
        public int MetadataReadCount { get; private set; }
        public int CandidateReadCount { get; private set; }
        public int FallbackCount { get; private set; }

        public Task<AiProductSearchMetadata> ReadMetadataAsync(CancellationToken cancellationToken)
        {
            MetadataReadCount++;
            return Task.FromResult(new AiProductSearchMetadata(
                ["PREBUILT_COMPUTER"],
                ["DOSELECT"],
                ["MEMORY_TYPE"]));
        }

        public Task<AiProductSearchCandidateResult> FindCandidatesAsync(
            AiProductSearchIntent intent,
            IReadOnlyList<AiProductSearchExistingPart> existingParts,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            CandidateReadCount++;
            return Task.FromResult(new AiProductSearchCandidateResult(
                true,
                AiSafetyReason.None,
                candidates,
                [],
                customBuild));
        }

        public Task<IReadOnlyList<ProductCardDto>> KeywordFallbackAsync(
            string message,
            SupportedLocale locale,
            CancellationToken cancellationToken)
        {
            FallbackCount++;
            return Task.FromResult(fallback ?? []);
        }
    }

    private sealed class StubStore(bool succeeds = true) : IAiProductSearchInteractionStore
    {
        public int SaveCount { get; private set; }
        public AiProductSearchInteractionWrite? LastWrite { get; private set; }

        public Task<bool> SaveAsync(
            AiProductSearchInteractionWrite interaction,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            LastWrite = interaction;
            return Task.FromResult(succeeds);
        }
    }
}
