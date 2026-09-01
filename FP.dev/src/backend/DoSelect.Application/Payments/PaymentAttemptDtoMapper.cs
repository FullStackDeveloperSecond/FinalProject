using DoSelect.Domain.Payments;

namespace DoSelect.Application.Payments;

/// <summary>Single customer-safe projection for payment-attempt creation, reads, and replay.</summary>
public static class PaymentAttemptDtoMapper
{
    private const string OrderCurrency = "TWD";

    public static PaymentAttemptDto Map(PaymentAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new PaymentAttemptDto(
            attempt.PublicId,
            attempt.Method,
            attempt.Status,
            attempt.Amount,
            OrderCurrency,
            ToInstruction(attempt),
            attempt.CreatedAtUtc,
            attempt.PaidAtUtc,
            attempt.RowVersion);
    }

    /// <remarks>
    /// Realtime methods have no customer-entered instruction. ATM and convenience-code attempts
    /// expose only the simulated code and its expiry; provider/internal identifiers stay omitted.
    /// </remarks>
    private static PaymentInstructionDto? ToInstruction(PaymentAttempt attempt)
    {
        if (PaymentMethodPolicy.KindOf(attempt.Method) != PaymentSettlementKind.Deferred)
        {
            return null;
        }

        return new PaymentInstructionDto(
            attempt.Method.ToString(),
            MaskedAccount: null,
            attempt.ExternalReference,
            attempt.InstructionExpiresAtUtc);
    }
}
