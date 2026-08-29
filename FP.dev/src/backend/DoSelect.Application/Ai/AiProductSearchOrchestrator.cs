using System.Diagnostics;
using System.Text.Json;
using DoSelect.Domain.Members;

namespace DoSelect.Application.Ai;

public sealed class AiProductSearchOrchestrator(
    IAiProductSearchAdmissionGate admissionGate,
    IAiProductSearchModelClient modelClient,
    IAiProductSearchCatalog catalog,
    IAiProductSearchInteractionStore interactionStore)
{
    private const int MaximumMessageLength = 2_000;
    private const int MaximumExistingParts = 12;

    public async Task<AiProductSearchExecutionResult> ExecuteAsync(
        AiProductSearchExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var access = await admissionGate.ReadAsync(request.Actor, cancellationToken);
        if (access.BudgetProtectionActive && !access.IsDemoAllowlisted)
        {
            return Reject(AiSafetyReason.BudgetProtectionActive, access);
        }

        if (access.RemainingDailyRequests <= 0)
        {
            return Reject(AiSafetyReason.DailyQuotaExceeded, access);
        }

        var inspection = AiOutboundContentGuard.Inspect(
            request.ExistingParts.Select(part => part.DisplayName ?? string.Empty)
                .Prepend(request.Message)
                .ToArray());
        if (!inspection.IsAllowed)
        {
            return Reject(inspection.Reason, access);
        }

        var metadata = await catalog.ReadMetadataAsync(cancellationToken);
        var reservation = await admissionGate.TryReserveAsync(
            request.Actor,
            request.SearchPublicId,
            cancellationToken);
        if (!reservation.IsReserved)
        {
            return reservation.State.RemainingDailyRequests <= 0
                ? Reject(AiSafetyReason.DailyQuotaExceeded, reservation.State)
                : Reject(
                    reservation.State.BudgetProtectionActive
                        ? AiSafetyReason.BudgetProtectionActive
                        : AiSafetyReason.ServiceUnavailable,
                    reservation.State);
        }

        var stopwatch = Stopwatch.StartNew();
        var intentResult = await modelClient.ParseIntentAsync(
            request.Message,
            request.Locale,
            metadata,
            cancellationToken);
        if (intentResult.Status != AiProductSearchModelStatus.Completed || intentResult.Intent is null)
        {
            return await DegradeAsync(
                request,
                reservation.State,
                intentResult.Usage,
                intentResult.Status.ToString(),
                stopwatch,
                cancellationToken);
        }

        var intent = intentResult.Intent;
        if (intent.Clarifications.Count > 0 || intent.ProposedExistingParts.Count > 0)
        {
            var questions = intent.Clarifications
                .Append(CreateExistingPartConfirmationQuestion(request.Locale))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            if (!await SaveAsync(
                request,
                intent,
                string.Join("\n", questions),
                intentResult.Usage,
                isDegraded: false,
                fallbackReason: null,
                stopwatch,
                cancellationToken))
            {
                return Reject(AiSafetyReason.ServiceUnavailable, reservation.State);
            }
            return new AiProductSearchExecutionResult(
                AiProductSearchExecutionStatus.Clarification,
                AiSafetyReason.None,
                AiFallback.None,
                intent,
                questions,
                Recommendations: [],
                FallbackProducts: [],
                reservation.State.RemainingDailyRequests,
                reservation.State.ResetAtUtc);
        }

        var candidateResult = await catalog.FindCandidatesAsync(
            intent,
            request.ExistingParts,
            request.Locale,
            cancellationToken);
        if (!candidateResult.IsValid)
        {
            if (candidateResult.Clarifications.Count > 0)
            {
                var questions = candidateResult.Clarifications.Take(2).ToArray();
                if (!await SaveAsync(
                        request,
                        intent,
                        string.Join("\n", questions),
                        intentResult.Usage,
                        isDegraded: false,
                        fallbackReason: null,
                        stopwatch,
                        cancellationToken))
                {
                    return Reject(AiSafetyReason.ServiceUnavailable, reservation.State);
                }

                return Clarify(questions, reservation.State, intent);
            }

            return await DegradeAsync(
                request,
                reservation.State,
                intentResult.Usage,
                candidateResult.Reason.ToString(),
                stopwatch,
                cancellationToken,
                intent);
        }

        if (candidateResult.Candidates.Count == 0 && candidateResult.CustomBuild is null)
        {
            if (!await SaveAsync(
                request,
                intent,
                "noResults",
                intentResult.Usage,
                isDegraded: false,
                fallbackReason: null,
                stopwatch,
                cancellationToken))
            {
                return Reject(AiSafetyReason.ServiceUnavailable, reservation.State);
            }
            return new AiProductSearchExecutionResult(
                AiProductSearchExecutionStatus.NoResults,
                AiSafetyReason.None,
                AiFallback.None,
                intent,
                Clarifications: [],
                Recommendations: [],
                FallbackProducts: [],
                reservation.State.RemainingDailyRequests,
                reservation.State.ResetAtUtc);
        }

        var approvedProducts = candidateResult.CustomBuild is null
            ? candidateResult.Candidates.Select(candidate => candidate.Product).ToArray()
            : candidateResult.CustomBuild.Components
                .Where(component => !component.IsExistingPart && component.Product is not null)
                .Select(component => component.Product!)
                .ToArray();
        var explanation = approvedProducts.Length == 0
            ? new AiProductSearchExplanationResult(
                AiProductSearchModelStatus.Completed,
                Reasons: [],
                Usage: null)
            : await modelClient.ExplainAsync(
                intent,
                approvedProducts,
                request.Locale,
                cancellationToken);
        var usage = CombineUsage(intentResult.Usage, explanation.Usage);
        if (explanation.Status != AiProductSearchModelStatus.Completed)
        {
            return await DegradeAsync(
                request,
                reservation.State,
                usage,
                explanation.Status.ToString(),
                stopwatch,
                cancellationToken,
                intent);
        }

        var reasons = explanation.Reasons.ToDictionary(reason => reason.SkuPublicId);
        if (reasons.Count != approvedProducts.Length ||
            approvedProducts.Any(product => !reasons.ContainsKey(product.DefaultSkuPublicId)))
        {
            return await DegradeAsync(
                request,
                reservation.State,
                usage,
                AiProductSearchModelStatus.InvalidOutput.ToString(),
                stopwatch,
                cancellationToken,
                intent);
        }

        var recommendations = candidateResult.Candidates
            .Select(candidate => new AiProductSearchRecommendation(
                candidate.Product,
                reasons[candidate.Product.DefaultSkuPublicId].Reason,
                candidate.CompatibilityStatus,
                candidate.CompatibilityMessageKeys))
            .ToArray();
        var customBuild = candidateResult.CustomBuild is null
            ? null
            : new AiCustomBuildRecommendation(
                candidateResult.CustomBuild.Components.Select(component =>
                    new AiCustomBuildRecommendationComponent(
                        component.Product,
                        component.SkuPublicId,
                        component.SourceType,
                        component.CategoryCode,
                        component.DisplayName,
                        component.Quantity,
                        component.IsExistingPart,
                        component.IsExistingPart || component.Product is null
                            ? null
                            : reasons[component.Product.DefaultSkuPublicId].Reason)).ToArray(),
                candidateResult.CustomBuild.PurchaseSubtotal,
                candidateResult.CustomBuild.AssemblyFee,
                candidateResult.CustomBuild.PurchaseTotal,
                candidateResult.CustomBuild.Currency,
                candidateResult.CustomBuild.CompatibilityStatus,
                candidateResult.CustomBuild.CompatibilityMessageKeys);
        var assistantContent = customBuild is null
            ? JsonSerializer.Serialize(recommendations.Select(item => new
            {
                item.Product.DefaultSkuPublicId,
                item.Reason,
            }))
            : JsonSerializer.Serialize(customBuild.Components.Select(item => new
            {
                item.SkuPublicId,
                item.IsExistingPart,
                item.Reason,
            }));
        if (!await SaveAsync(
            request,
            intent,
            assistantContent,
            usage,
            isDegraded: false,
            fallbackReason: null,
            stopwatch,
            cancellationToken))
        {
            return Reject(AiSafetyReason.ServiceUnavailable, reservation.State);
        }

        return new AiProductSearchExecutionResult(
            AiProductSearchExecutionStatus.Recommendations,
            AiSafetyReason.None,
            AiFallback.None,
            intent,
            Clarifications: [],
            recommendations,
            FallbackProducts: [],
            reservation.State.RemainingDailyRequests,
            reservation.State.ResetAtUtc,
            customBuild);
    }

    private async Task<AiProductSearchExecutionResult> DegradeAsync(
        AiProductSearchExecutionRequest request,
        AiProductSearchAccessState access,
        AiSupportModelUsage? usage,
        string fallbackReason,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        AiProductSearchIntent? intent = null)
    {
        var products = await catalog.KeywordFallbackAsync(
            request.Message,
            request.Locale,
            cancellationToken);
        if (!await SaveAsync(
            request,
            intent,
            "keywordSearch",
            usage,
            isDegraded: true,
            fallbackReason,
            stopwatch,
            cancellationToken))
        {
            return Reject(AiSafetyReason.ServiceUnavailable, access);
        }
        return new AiProductSearchExecutionResult(
            AiProductSearchExecutionStatus.Degraded,
            AiSafetyReason.ServiceUnavailable,
            AiFallback.KeywordSearch,
            intent,
            Clarifications: [],
            Recommendations: [],
            products,
            access.RemainingDailyRequests,
            access.ResetAtUtc);
    }

    private Task<bool> SaveAsync(
        AiProductSearchExecutionRequest request,
        AiProductSearchIntent? intent,
        string? assistantContent,
        AiSupportModelUsage? usage,
        bool isDegraded,
        string? fallbackReason,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        return interactionStore.SaveAsync(
            new AiProductSearchInteractionWrite(
                request.SearchPublicId,
                request.Message,
                intent,
                assistantContent,
                usage,
                isDegraded,
                fallbackReason,
                (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds)),
            cancellationToken);
    }

    private static AiSupportModelUsage? CombineUsage(
        AiSupportModelUsage? first,
        AiSupportModelUsage? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return new AiSupportModelUsage(
            second.Model,
            first.InputTokens + second.InputTokens,
            first.OutputTokens + second.OutputTokens);
    }

    private static string CreateExistingPartConfirmationQuestion(SupportedLocale locale) => locale switch
    {
        SupportedLocale.ZhTw => "請確認 AI 解析出的既有零件與規格；確認前不會納入相容性計算。",
        SupportedLocale.JaJp => "AI が解析した既存パーツと仕様を確認してください。確認前は互換性判定に使用しません。",
        SupportedLocale.KoKr => "AI가 분석한 기존 부품과 사양을 확인해 주세요. 확인 전에는 호환성 검사에 사용하지 않습니다.",
        _ => throw new ArgumentOutOfRangeException(nameof(locale)),
    };

    private static AiProductSearchExecutionResult Clarify(
        IReadOnlyList<string> questions,
        AiProductSearchAccessState access,
        AiProductSearchIntent? intent = null) =>
        new(
            AiProductSearchExecutionStatus.Clarification,
            AiSafetyReason.None,
            AiFallback.None,
            intent,
            questions,
            Recommendations: [],
            FallbackProducts: [],
            access.RemainingDailyRequests,
            access.ResetAtUtc);

    private static AiProductSearchExecutionResult Reject(
        AiSafetyReason reason,
        AiProductSearchAccessState access) =>
        new(
            AiProductSearchExecutionStatus.Rejected,
            reason,
            AiFallback.KeywordSearch,
            Intent: null,
            Clarifications: [],
            Recommendations: [],
            FallbackProducts: [],
            access.RemainingDailyRequests,
            access.ResetAtUtc);

    private static void ValidateRequest(AiProductSearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);
        ArgumentNullException.ThrowIfNull(request.Actor);
        ArgumentNullException.ThrowIfNull(request.ExistingParts);
        if (request.SearchPublicId == Guid.Empty || request.Message.Length > MaximumMessageLength)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (!Enum.IsDefined(request.Locale) || request.ExistingParts.Count > MaximumExistingParts)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var hasMember = !string.IsNullOrWhiteSpace(request.Actor.MemberUserId);
        var hasAnonymous = request.Actor.AnonymousSessionKeyHash is { Length: 32 };
        if (hasMember == hasAnonymous)
        {
            throw new ArgumentException("Exactly one member or anonymous owner is required.", nameof(request));
        }

        if (request.ExistingParts.Any(part =>
                part.Quantity is < 1 or > 8 ||
                !part.ConfirmedByUser ||
                part.SourceType is not ("catalogSku" or "structuredManual") ||
                (part.SourceType == "catalogSku" &&
                 (part.SkuPublicId is null || part.CategoryCode is not null ||
                  part.DisplayName is not null || part.Specifications.Count > 0)) ||
                (part.SourceType == "structuredManual" &&
                 (part.SkuPublicId is not null || string.IsNullOrWhiteSpace(part.CategoryCode) ||
                   string.IsNullOrWhiteSpace(part.DisplayName) || part.Specifications.Count == 0))))
        {
            throw new ArgumentException("Existing parts are invalid.", nameof(request));
        }
    }
}
