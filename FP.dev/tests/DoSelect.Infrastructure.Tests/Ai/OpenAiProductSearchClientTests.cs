using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DoSelect.Application.Ai;
using DoSelect.Application.Catalog;
using DoSelect.Domain.Catalog;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Tests.Ai;

public sealed class OpenAiProductSearchClientTests
{
    [Fact]
    public async Task ParseIntentAsync_CompletedStrictOutput_UsesStatelessWhitelistContract()
    {
        var handler = new RecordingHandler(_ => JsonResponse(IntentResponse()));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Equal(AiProductSearchIntentType.PrebuiltComputer, result.Intent?.Intent);
        Assert.Equal(50_000, result.Intent?.Budget?.Maximum);
        Assert.Equal("gpt-5.6-luna-snapshot", result.Usage?.Model);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("none", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.Equal("fast", body.RootElement.GetProperty("service_tier").GetString());
        var instructions = body.RootElement.GetProperty("instructions").GetString();
        Assert.Contains("Preserve every explicitly stated budget boundary", instructions, StringComparison.Ordinal);
        Assert.Contains("single unambiguous colloquial amount", instructions, StringComparison.Ordinal);
        Assert.Contains("Add only purposes explicitly requested", instructions, StringComparison.Ordinal);
        Assert.Contains("named component or accessory", instructions, StringComparison.Ordinal);
        Assert.Contains("product label explicitly describes its intended use", instructions, StringComparison.Ordinal);
        Assert.Contains("Preserve every explicitly stated qualitative preference", instructions, StringComparison.Ordinal);
        Assert.Contains("quiet operation remains a preference", instructions, StringComparison.Ordinal);
        Assert.Contains("separate application confirmation", instructions, StringComparison.Ordinal);
        Assert.Contains("ready-made, prebuilt, branded package", instructions, StringComparison.Ordinal);
        Assert.Contains("budget-based gaming 主機", instructions, StringComparison.Ordinal);
        Assert.Contains("generic 主機", instructions, StringComparison.Ordinal);
        Assert.Contains("遊戲美術", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not ask about optional preferences", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not ask whether peripherals", instructions, StringComparison.Ordinal);
        Assert.Contains("STORAGE_CAPACITY_GB is storage capacity", instructions, StringComparison.Ordinal);
        Assert.Contains("1 TB = 1024 GB", instructions, StringComparison.Ordinal);
        Assert.Contains("without minimum or maximum wording", instructions, StringComparison.Ordinal);
        Assert.Contains("specifications of an existing part only in proposedExistingParts", instructions, StringComparison.Ordinal);
        Assert.Contains("never repeat that part's category", instructions, StringComparison.Ordinal);
        Assert.Contains("Never derive target-product requiredSpecs from an unconfirmed", instructions, StringComparison.Ordinal);
        Assert.Contains("only after the user confirms that part", instructions, StringComparison.Ordinal);
        Assert.Contains("set minimum to null", instructions, StringComparison.Ordinal);
        Assert.Contains("Example: at least 30,000 but at most 20,000 for a computer", instructions, StringComparison.Ordinal);
        Assert.Contains("Example: a 40,000 video-editing computer", instructions, StringComparison.Ordinal);
        Assert.Equal("product-search-v13", OpenAiProductSearchClient.PromptVersion);
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(
            "json_schema",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.DoesNotContain(
            "\"uniqueItems\"",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetRawText(),
            StringComparison.Ordinal);
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("untrusted_user_input", input.RootElement.GetProperty("userMessage").GetProperty("trust").GetString());
        Assert.Equal("untrusted_data", input.RootElement.GetProperty("allowedCatalog").GetProperty("trust").GetString());
        Assert.True(input.RootElement.GetProperty("allowedCatalog").TryGetProperty("semanticKeysByCategory", out _));
    }

    [Fact]
    public async Task ParseIntentAsync_DefaultServiceTier_NormalizesRollbackPayload()
    {
        var handler = new RecordingHandler(_ => JsonResponse(IntentResponse()));
        var subject = CreateSubject(handler, serviceTier: "DEFAULT");

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("default", body.RootElement.GetProperty("service_tier").GetString());
    }

    [Fact]
    public async Task ParseIntentAsync_ConflictingBudgetUsesSafeMaximumAndClarification()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = Array.Empty<string>(),
            budget = (object?)null,
            keyword = "主機",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "我要兩萬元以上的主機，但最多只能花一萬五。",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget?.Minimum);
        Assert.Equal(15_000m, result.Intent?.Budget?.Maximum);
        var clarification = Assert.Single(result.Intent!.Clarifications);
        Assert.Contains("20,000", clarification, StringComparison.Ordinal);
        Assert.Contains("15,000", clarification, StringComparison.Ordinal);
        Assert.Contains("預算範圍", clarification, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_ClarificationPurposeCodesUseCustomerFacingNames()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = Array.Empty<string>(),
            budget = new { minimum = (decimal?)null, maximum = 30_000m },
            keyword = (string?)null,
            categoryCode = "CUSTOM_BUILD",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "請選擇 Gaming、Office、Programming 或 VideoEditing。" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "三萬元幫我組電腦，主要用途我不想說。",
            SupportedLocale.ZhTw,
            SearchNovice025Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var clarification = Assert.Single(result.Intent!.Clarifications);
        Assert.Equal("請選擇 遊戲、文書處理、程式開發 或 影片剪輯。", clarification);
        Assert.DoesNotContain("Gaming", clarification, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoEditing", clarification, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_HardwareSpecificationNumberIsNotTreatedAsBudgetMinimum()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = new[] { "GraphicDesign" },
            budget = (object?)null,
            keyword = "繪圖主機",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "預算六萬元內，RAM 至少 64GB。",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget?.Minimum);
        Assert.Equal(60_000m, result.Intent?.Budget?.Maximum);
        Assert.Empty(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_SearchNovice025_RestoresMaximumAndRemovesBudgetQuestion()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = new[] { "Gaming" },
            budget = (object?)null,
            keyword = "遊戲主機",
            categoryCode = "CUSTOM_BUILD",
            preferredBrandCodes = new[] { "NOVACORE" },
            excludedBrandCodes = new[] { "PIXELFORGE" },
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "你的最高預算是多少？" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "偏好 NovaCore，但不要 PixelForge，三萬五遊戲主機。",
            SupportedLocale.ZhTw,
            SearchNovice025Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget?.Minimum);
        Assert.Equal(35_000m, result.Intent?.Budget?.Maximum);
        Assert.Equal(["NOVACORE"], result.Intent?.PreferredBrandCodes);
        Assert.Equal(["PIXELFORGE"], result.Intent?.ExcludedBrandCodes);
        Assert.Empty(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("想玩動作遊戲，三萬五左右的主機。")]
    [InlineData("想玩動作遊戲，兩三萬的主機。")]
    public async Task ParseIntentAsync_AmbiguousChineseAmountDoesNotOverrideModel(string message)
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = new[] { "Gaming" },
            budget = (object?)null,
            keyword = "遊戲主機",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "請確認可接受的最高預算。" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            message,
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget);
        Assert.Single(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_NonZhTw_DoesNotApplyChineseBudgetGuard()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = new[] { "Gaming" },
            budget = (object?)null,
            keyword = "遊戲主機",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "請確認可接受的最高預算。" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "想玩動作遊戲，三萬五的主機。",
            SupportedLocale.JaJp,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget);
        Assert.Single(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_DuplicateBrandCode_FailsClosedWithoutSynchronousRetry()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = new[] { "VideoEditing" },
            budget = new { minimum = (decimal?)null, maximum = 50_000m },
            keyword = "剪輯",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = new[] { "DOSELECT", "DOSELECT" },
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal("INTENT_DUPLICATE_VALUE", result.ValidationFailureCode);
        Assert.Equal("preferredBrandCodes", result.ValidationFailureField);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_InvalidSchema_FailsClosedWithoutSynchronousRetry()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"intent\":17}"));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal("RESPONSE_JSON_INVALID", result.ValidationFailureCode);
        Assert.Equal("output_text", result.ValidationFailureField);
        Assert.Equal(100, result.Usage?.InputTokens);
        Assert.Equal(20, result.Usage?.OutputTokens);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_NonTransientHttpFailure_DoesNotRetry()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_TransientHttpFailure_DegradesWithoutSynchronousRetry()
    {
        var handler = new RecordingHandler(attempt =>
            attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(IntentResponse()));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_Timeout_DegradesWithoutSynchronousRetry()
    {
        var handler = new CancellationAwareHandler();
        var subject = CreateSubject(handler, timeoutMilliseconds: 10);

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_SearchNovice023_RestoresBareThousandsMaximum()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "SingleProduct",
            purposes = new[] { "Gaming" },
            budget = (object?)null,
            keyword = "滑鼠",
            categoryCode = "MOUSE",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = new[] { "不要太複雜" },
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "遊戲滑鼠兩千內，不要太複雜。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(["MOUSE"], ["DOSELECT"], []),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget?.Minimum);
        Assert.Equal(2_000m, result.Intent?.Budget?.Maximum);
        Assert.Equal(["不要太複雜"], result.Intent?.Preferences);
        Assert.Empty(result.Intent!.Clarifications);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_BareThousandsHardwareThresholdDoesNotBecomeBudget()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "SingleProduct",
            purposes = new[] { "Gaming" },
            budget = (object?)null,
            keyword = "滑鼠",
            categoryCode = "MOUSE",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = new[] { "DPI 兩千以下" },
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "滑鼠 DPI 兩千以下，不限預算。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(["MOUSE"], ["DOSELECT"], []),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Null(result.Intent?.Budget);
        Assert.Equal(["DPI 兩千以下"], result.Intent?.Preferences);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_UnsupportedServiceTier_FailsClosedWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No HTTP call expected."));
        var subject = CreateSubject(handler, serviceTier: "unapproved");

        var result = await subject.ParseIntentAsync(
            "五萬元剪輯電腦",
            SupportedLocale.ZhTw,
            Metadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Unavailable, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ParseIntentAsync_NaturalLanguageExistingPart_ReturnsUnconfirmedProposalOnly()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "SingleProduct",
            purposes = Array.Empty<string>(),
            budget = (object?)null,
            keyword = "主機板",
            categoryCode = "MOTHERBOARD",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = new[]
            {
                new { semanticKey = "CPU_SOCKET", @operator = "eq", value = "AM5", unit = (string?)null },
            },
            preferences = new[] { "需要 Wi-Fi", "支援 AM5 CPU", "AM5 CPU 供電穩定" },
            proposedExistingParts = new[]
            {
                new
                {
                    categoryCode = "CPU",
                    displayName = "AM5 CPU",
                    quantity = 1,
                    specifications = new[]
                    {
                        new { semanticKey = "CPU_SOCKET", @operator = "eq", value = "AM5", unit = (string?)null },
                    },
                },
            },
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "已有 AM5 CPU，想找主機板",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["CPU", "MOTHERBOARD"],
                ["DOSELECT"],
                ["CPU_SOCKET"]),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var proposal = Assert.Single(result.Intent!.ProposedExistingParts);
        Assert.Equal("CPU", proposal.CategoryCode);
        Assert.Equal("AM5", Assert.Single(proposal.Specifications).Value);
        Assert.Empty(result.Intent.RequiredSpecs);
        Assert.Equal(["需要 Wi-Fi", "AM5 CPU 供電穩定"], result.Intent.Preferences);
    }

    [Fact]
    public async Task ParseIntentAsync_StorageCategoryWithMemoryCapacitySpec_FailsClosed()
    {
        var output = JsonSerializer.Serialize(new
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
                new { semanticKey = "MEMORY_KIT_CAPACITY_GB", @operator = "gte", value = "8192", unit = "GB" },
            },
            preferences = new[] { "家庭照片" },
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "想存家庭照片，請推薦 8TB 儲存裝置，預算八千。",
            SupportedLocale.ZhTw,
            StorageMetadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Null(result.Intent);
        Assert.Equal("INTENT_SPECIFICATION_CATEGORY_MISMATCH", result.ValidationFailureCode);
        Assert.Equal("requiredSpecs", result.ValidationFailureField);
    }

    [Fact]
    public async Task ParseIntentAsync_StorageCapacityInTb_NormalizesToGb()
    {
        var output = JsonSerializer.Serialize(new
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
                new { semanticKey = "STORAGE_CAPACITY_GB", @operator = "gte", value = "8", unit = "TB" },
            },
            preferences = new[] { "家庭照片" },
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "想存家庭照片，請推薦 8TB 儲存裝置，預算八千。",
            SupportedLocale.ZhTw,
            StorageMetadata(),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var spec = Assert.Single(result.Intent!.RequiredSpecs);
        Assert.Equal("STORAGE_CAPACITY_GB", spec.SemanticKey);
        Assert.Equal("eq", spec.Operator);
        Assert.Equal("8192", spec.Value);
        Assert.Equal("GB", spec.Unit);
    }

    [Fact]
    public async Task ParseIntentAsync_ExplicitSsdAndExactCapacity_RestoresBothRequirements()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = new[] { "VideoEditing" },
            budget = new { minimum = (decimal?)null, maximum = 50_000m },
            keyword = "剪輯電腦",
            categoryCode = "CUSTOM_BUILD",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = new[]
            {
                new { semanticKey = "STORAGE_CAPACITY_GB", @operator = "gte", value = "2", unit = "TB" },
            },
            preferences = new[] { "需要 2TB SSD" },
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "剪輯素材很多，另外要 2TB SSD，整體五萬元。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["CUSTOM_BUILD", "STORAGE"],
                ["DOSELECT"],
                ["STORAGE_CAPACITY_GB", "STORAGE_INTERFACE"],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["STORAGE"] = ["STORAGE_CAPACITY_GB", "STORAGE_INTERFACE"],
                }),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Contains(
            result.Intent!.RequiredSpecs,
            spec => spec is { SemanticKey: "STORAGE_CAPACITY_GB", Operator: "eq", Value: "2048", Unit: "GB" });
        Assert.Contains(
            result.Intent.RequiredSpecs,
            spec => spec is { SemanticKey: "STORAGE_INTERFACE", Operator: "eq", Value: "SSD", Unit: null });
    }

    [Fact]
    public async Task ParseIntentAsync_ExactStorageAndMinimumRam_NormalizesEachCapacityIndependently()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "CustomBuild",
            purposes = new[] { "VideoEditing" },
            budget = new { minimum = (decimal?)null, maximum = 50_000m },
            keyword = "剪輯電腦",
            categoryCode = "CUSTOM_BUILD",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = new[]
            {
                new { semanticKey = "STORAGE_CAPACITY_GB", @operator = "gte", value = "2", unit = "TB" },
                new { semanticKey = "MEMORY_KIT_CAPACITY_GB", @operator = "gte", value = "64", unit = "GB" },
            },
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "剪輯電腦要 2TB SSD，RAM 至少 64GB，整體五萬元。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["CUSTOM_BUILD", "STORAGE", "MEMORY"],
                ["DOSELECT"],
                ["STORAGE_CAPACITY_GB", "STORAGE_INTERFACE", "MEMORY_KIT_CAPACITY_GB"]),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Contains(
            result.Intent!.RequiredSpecs,
            spec => spec is { SemanticKey: "STORAGE_CAPACITY_GB", Operator: "eq", Value: "2048", Unit: "GB" });
        Assert.Contains(
            result.Intent.RequiredSpecs,
            spec => spec is { SemanticKey: "MEMORY_KIT_CAPACITY_GB", Operator: "gte", Value: "64", Unit: "GB" });
    }

    [Fact]
    public async Task ParseIntentAsync_ExplicitMinimumStorageCapacity_PreservesGte()
    {
        var output = JsonSerializer.Serialize(new
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
                new { semanticKey = "STORAGE_CAPACITY_GB", @operator = "gte", value = "8", unit = "TB" },
            },
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "至少 8TB 的儲存裝置，預算八千。",
            SupportedLocale.ZhTw,
            StorageMetadata(),
            default);

        Assert.Equal("gte", Assert.Single(result.Intent!.RequiredSpecs).Operator);
    }

    [Fact]
    public async Task ParseIntentAsync_SsdMentionOutsideStorageRequest_DoesNotInjectStorageSpecification()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "SingleProduct",
            purposes = Array.Empty<string>(),
            budget = new { minimum = (decimal?)null, maximum = 2_000m },
            keyword = "滑鼠",
            categoryCode = "MOUSE",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = Array.Empty<string>(),
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "SSD 外接盒旁邊要放一顆兩千元內的滑鼠。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["MOUSE", "STORAGE"],
                ["DOSELECT"],
                ["STORAGE_INTERFACE", "MOUSE_DPI"],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["MOUSE"] = ["MOUSE_DPI"],
                    ["STORAGE"] = ["STORAGE_INTERFACE"],
                }),
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        Assert.Empty(result.Intent!.RequiredSpecs);
    }

    [Fact]
    public async Task ParseIntentAsync_ExplicitAssemblyWording_OverridesPrebuiltClassification()
    {
        var output = JsonSerializer.Serialize(new
        {
            intent = "PrebuiltComputer",
            purposes = Array.Empty<string>(),
            budget = new { minimum = (decimal?)null, maximum = 30_000m },
            keyword = "電腦",
            categoryCode = "PREBUILT_COMPUTER",
            preferredBrandCodes = Array.Empty<string>(),
            excludedBrandCodes = Array.Empty<string>(),
            requiredSpecs = Array.Empty<object>(),
            preferences = Array.Empty<string>(),
            proposedExistingParts = Array.Empty<object>(),
            clarifications = new[] { "請問主要用途是什麼？" },
        });
        var handler = new RecordingHandler(_ => JsonResponse(output));
        var subject = CreateSubject(handler);

        var result = await subject.ParseIntentAsync(
            "三萬元幫我組電腦，主要用途我不想說。",
            SupportedLocale.ZhTw,
            new AiProductSearchMetadata(
                ["PREBUILT_COMPUTER", "CUSTOM_BUILD"],
                ["DOSELECT"],
                ["MEMORY_TYPE"]),
            default);

        Assert.Equal(AiProductSearchIntentType.CustomBuild, result.Intent!.Intent);
        Assert.Equal("CUSTOM_BUILD", result.Intent.CategoryCode);
        Assert.Single(result.Intent.Clarifications);
    }

    [Fact]
    public async Task ExplainAsync_ApprovedCandidate_ReturnsGroundedReasonWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            [Product()],
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.Completed, result.Status);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(Product().DefaultSkuPublicId, reason.SkuPublicId);
        Assert.Contains("創作者工作站", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("NT$49,000", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("最高預算 NT$50,000", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("GPU 預算優先", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("64GB RAM", reason.Reason, StringComparison.Ordinal);
        Assert.Contains("取捨", reason.Reason, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ExplainAsync_BrandPreferenceAndExclusion_UsesCustomerFacingLanguage()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);
        var intent = Intent() with
        {
            PreferredBrandCodes = ["NOVACORE"],
            ExcludedBrandCodes = ["PIXELFORGE"],
        };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.Contains("不是你指定的偏好品牌", reason, StringComparison.Ordinal);
        Assert.Contains("未包含你排除的品牌", reason, StringComparison.Ordinal);
        Assert.Contains("品牌偏好只會影響推薦順序", reason, StringComparison.Ordinal);
        Assert.Contains("不會放寬必要條件", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("NOVACORE", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("PIXELFORGE", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("DOSELECT", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("候選", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("後端", reason, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsync_ExcludedBrandWithoutPreference_DoesNotRenderEmptyPreference()
    {
        var subject = CreateSubject(new RecordingHandler(_ =>
            throw new InvalidOperationException("No explanation HTTP call expected.")));
        var intent = Intent() with { ExcludedBrandCodes = ["PIXELFORGE"] };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.DoesNotContain("偏好品牌 ，", reason, StringComparison.Ordinal);
        Assert.Contains("未包含你排除的品牌", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("PIXELFORGE", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("DOSELECT", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_UserNeeds_AreExplainedToTheCustomerWithoutInternalKeys()
    {
        var subject = CreateSubject(new RecordingHandler(_ =>
            throw new InvalidOperationException("No explanation HTTP call expected.")));
        var intent = Intent() with
        {
            RequiredSpecs = [new AiRequiredSpec("MEMORY_KIT_CAPACITY_GB", "gte", "64", "GB")],
            Preferences = ["安靜"],
        };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.Contains("影片剪輯", reason, StringComparison.Ordinal);
        Assert.Contains("記憶體至少 64 GB", reason, StringComparison.Ordinal);
        Assert.Contains("不可放寬", reason, StringComparison.Ordinal);
        Assert.Contains("「安靜」會作為排序偏好", reason, StringComparison.Ordinal);
        Assert.Contains("現有資料不能確認此商品符合該偏好", reason, StringComparison.Ordinal);
        Assert.Contains("不會因此放寬必要規格或相容性條件", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoEditing", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("MEMORY_KIT_CAPACITY_GB", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_ComponentCategory_StatesCompatibilityEvidenceLimitWithoutGuessingInterface()
    {
        var subject = CreateSubject(new RecordingHandler(_ =>
            throw new InvalidOperationException("No explanation HTTP call expected.")));
        var intent = Intent() with
        {
            Intent = AiProductSearchIntentType.SingleProduct,
            CategoryCode = CompatibilityCatalogContract.Categories.Storage,
            RequiredSpecs =
            [
                new AiRequiredSpec(
                    CompatibilityCatalogContract.SemanticKeys.StorageCapacityGb,
                    "eq",
                    "2048",
                    "GB"),
            ],
            Preferences = ["速度比舊硬碟快"],
        };
        var product = Product() with
        {
            Category = new ProductCategoryRef(
                CompatibilityCatalogContract.Categories.Storage,
                "儲存裝置"),
            Badges = [],
        };

        var result = await subject.ExplainAsync(
            intent,
            [product],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.Contains("需核對與現有設備的規格相容性", reason, StringComparison.Ordinal);
        Assert.Contains("現有資料不足以直接確認", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("M.2", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SATA", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsync_WithoutMaximumBudget_DoesNotMentionBackendCandidateRules()
    {
        var subject = CreateSubject(new RecordingHandler(_ =>
            throw new InvalidOperationException("No explanation HTTP call expected.")));
        var intent = Intent() with { Budget = null };

        var result = await subject.ExplainAsync(
            intent,
            [Product()],
            SupportedLocale.ZhTw,
            default);

        var reason = Assert.Single(result.Reasons).Reason;
        Assert.Contains("符合你目前提供的購買條件", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("後端", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("候選", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_TooManyCandidates_FailsClosedWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No explanation HTTP call expected."));
        var subject = CreateSubject(handler);

        var result = await subject.ExplainAsync(
            Intent(),
            Enumerable.Range(0, 7)
                .Select(index => Product() with
                {
                    DefaultSkuPublicId = Guid.Parse($"22222222-2222-2222-2222-{index + 1:000000000000}"),
                })
                .ToArray(),
            SupportedLocale.ZhTw,
            default);

        Assert.Equal(AiProductSearchModelStatus.InvalidOutput, result.Status);
        Assert.Empty(result.Reasons);
        Assert.Equal(0, handler.CallCount);
    }

    private static OpenAiProductSearchClient CreateSubject(
        HttpMessageHandler handler,
        int timeoutMilliseconds = 5_000,
        string serviceTier = "fast") =>
        new(
            new HttpClient(handler),
            Options.Create(new OpenAiResponsesOptions
            {
                ApiKey = "synthetic-key",
                ProductSearchModel = "gpt-5.6-luna",
                ProductSearchServiceTier = serviceTier,
                ProductSearchTimeoutMilliseconds = timeoutMilliseconds,
            }));

    private static AiProductSearchMetadata Metadata() =>
        new(["PREBUILT_COMPUTER"], ["DOSELECT"], ["MEMORY_TYPE"]);

    private static AiProductSearchMetadata StorageMetadata() =>
        new(
            ["STORAGE", "MEMORY"],
            ["DOSELECT"],
            ["STORAGE_CAPACITY_GB", "STORAGE_INTERFACE", "MEMORY_KIT_CAPACITY_GB"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["STORAGE"] = ["STORAGE_CAPACITY_GB", "STORAGE_INTERFACE"],
                ["MEMORY"] = ["MEMORY_KIT_CAPACITY_GB"],
            });

    private static AiProductSearchMetadata SearchNovice025Metadata() =>
        new(["CUSTOM_BUILD"], ["NOVACORE", "PIXELFORGE"], ["MEMORY_TYPE"]);

    private static AiProductSearchIntent Intent() =>
        new(
            AiProductSearchIntentType.PrebuiltComputer,
            ["VideoEditing"],
            new AiBudgetRange(null, 50_000),
            "剪輯",
            "PREBUILT_COMPUTER",
            [],
            [],
            [],
            ["安靜"],
            [],
            []);

    private static ProductCardDto Product() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "PC-CREATOR",
            "PC-CREATOR-01",
            "創作者工作站",
            new ProductBrandRef("DOSELECT", "懂選"),
            new ProductCategoryRef("PREBUILT_COMPUTER", "套裝電腦"),
            new ProductPrice(49_000, null, "TWD"),
            ProductAvailabilityCodes.InStock,
            null,
            ["GPU 預算優先", "64GB RAM"]);

    private static string IntentResponse() => JsonSerializer.Serialize(new
    {
        intent = "PrebuiltComputer",
        purposes = new[] { "VideoEditing" },
        budget = new { minimum = (decimal?)null, maximum = 50_000m },
        keyword = "剪輯",
        categoryCode = "PREBUILT_COMPUTER",
        preferredBrandCodes = Array.Empty<string>(),
        excludedBrandCodes = Array.Empty<string>(),
        requiredSpecs = Array.Empty<object>(),
        preferences = new[] { "安靜" },
        proposedExistingParts = Array.Empty<object>(),
        clarifications = Array.Empty<string>(),
    });

    private static HttpResponseMessage JsonResponse(string outputText)
    {
        var body = JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.6-luna-snapshot",
            usage = new { input_tokens = 100, output_tokens = 20 },
            output_text = outputText,
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler(
        Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization;
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responseFactory(CallCount);
        }
    }

    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The timeout token should cancel the request.");
        }
    }
}
