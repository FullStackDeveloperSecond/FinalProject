using System.Text.RegularExpressions;

namespace DoSelect.Application.Ai;

public sealed record AiBudgetRange(
    decimal? Minimum,
    decimal? Maximum);

public sealed record AiRequiredSpec(
    string SemanticKey,
    string Operator,
    string Value,
    string? Unit);

public sealed record AiSearchIntentCandidate(
    AiBudgetRange? Budget,
    IReadOnlyList<AiRequiredSpec> RequiredSpecs);

public sealed record AiSearchIntentValidationResult(
    bool IsValid,
    bool MayQueryCatalog,
    AiSafetyReason Reason);

public static partial class AiSearchIntentSafetyValidator
{
    private const decimal MaximumBudget = 10_000_000m;
    private const int MaximumRequiredSpecs = 12;

    public static AiSearchIntentValidationResult Validate(
        AiSearchIntentCandidate intent,
        IReadOnlySet<string> allowedSemanticKeys)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(allowedSemanticKeys);

        if (!IsBudgetValid(intent.Budget))
        {
            return Invalid(AiSafetyReason.InvalidBudgetRange);
        }

        if (intent.RequiredSpecs.Count > MaximumRequiredSpecs)
        {
            return Invalid(AiSafetyReason.InvalidSearchIntent);
        }

        foreach (var spec in intent.RequiredSpecs)
        {
            if (!SemanticKeyPattern().IsMatch(spec.SemanticKey) ||
                !allowedSemanticKeys.Contains(spec.SemanticKey))
            {
                return Invalid(AiSafetyReason.SemanticKeyNotAllowed);
            }

            if (spec.Operator is not ("eq" or "gte" or "lte" or "in"))
            {
                return Invalid(AiSafetyReason.InvalidSearchIntent);
            }
        }

        return new AiSearchIntentValidationResult(
            IsValid: true,
            MayQueryCatalog: true,
            AiSafetyReason.None);
    }

    private static bool IsBudgetValid(AiBudgetRange? budget)
    {
        if (budget is null)
        {
            return true;
        }

        if (budget.Minimum is < 0 or > MaximumBudget ||
            budget.Maximum is < 0 or > MaximumBudget)
        {
            return false;
        }

        return budget.Minimum is null ||
            budget.Maximum is null ||
            budget.Minimum <= budget.Maximum;
    }

    private static AiSearchIntentValidationResult Invalid(AiSafetyReason reason)
    {
        return new AiSearchIntentValidationResult(
            IsValid: false,
            MayQueryCatalog: false,
            reason);
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticKeyPattern();
}

public sealed record AiFailureDecision(
    bool MayRetry,
    bool MayExecuteDownstream,
    AiFallback Fallback);

public static class AiFailurePolicy
{
    public static AiFailureDecision Decide(
        AiFeature feature,
        AiFailureKind failure,
        int priorAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priorAttempts);

        var isTransient = failure is
            AiFailureKind.Timeout or
            AiFailureKind.RateLimited or
            AiFailureKind.TemporaryServiceError;

        if (isTransient && priorAttempts == 0)
        {
            return new AiFailureDecision(
                MayRetry: true,
                MayExecuteDownstream: false,
                AiFallback.None);
        }

        return new AiFailureDecision(
            MayRetry: false,
            MayExecuteDownstream: false,
            feature == AiFeature.ProductSearch
                ? AiFallback.KeywordSearch
                : AiFallback.HumanSupport);
    }
}
