using DoSelect.Domain.Promotions;

namespace DoSelect.Application.Promotions;

/// <summary>
/// 一張優惠券的規則與適用範圍，由 Infrastructure 於同一交易內讀出並以
/// <see cref="CouponRule.From(Coupon)"/> 對應。<paramref name="CouponId"/> 供使用量查詢使用。
/// </summary>
public sealed record CouponRuleSnapshot(long CouponId, CouponRule Rule, CouponScopeRules Scope);

/// <summary>
/// 優惠券試算所需的讀取埠。實作屬於 Infrastructure，不在此層存取 DbContext。
/// </summary>
public interface ICouponRuleReader
{
    Task<CouponRuleSnapshot?> FindByCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 已完成的使用量。會員以 MemberUserId 計數，訪客以 GuestUsageKeyHash 計數。
    /// </summary>
    Task<CouponUsageState> GetUsageAsync(
        long couponId,
        string? memberUserId,
        byte[]? guestUsageKeyHash,
        CancellationToken cancellationToken = default);
}

public sealed record CouponQuoteRequest(
    string CouponCode,
    IReadOnlyList<CouponCalculationLine> Lines,
    string? MemberUserId,
    byte[]? GuestUsageKeyHash,
    bool IsAssemblyDelivery);

/// <summary>
/// 優惠券試算 Use Case。負責正規化優惠碼、取規則與使用量，計算本身交給
/// <see cref="CouponCalculator"/>。本服務只做預覽，不建立或保留 CouponRedemption。
/// </summary>
public sealed class CouponQuoteService
{
    private readonly ICouponRuleReader _ruleReader;
    private readonly TimeProvider _timeProvider;

    public CouponQuoteService(ICouponRuleReader ruleReader, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ruleReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _ruleReader = ruleReader;
        _timeProvider = timeProvider;
    }

    public async Task<CouponCalculationResult> QuoteAsync(
        CouponQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        if (!CouponCode.TryNormalize(request.CouponCode, out var normalizedCode))
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponInvalid);
        }

        var snapshot = await _ruleReader.FindByCodeAsync(normalizedCode, cancellationToken);
        if (snapshot is null)
        {
            return CouponCalculationResult.Failure(CouponCalculationErrorCodes.CouponInvalid);
        }

        var usage = await _ruleReader.GetUsageAsync(
            snapshot.CouponId,
            request.MemberUserId,
            request.GuestUsageKeyHash,
            cancellationToken);

        return CouponCalculator.Calculate(new CouponCalculationRequest(
            snapshot.Rule,
            snapshot.Scope,
            usage,
            request.Lines,
            IsAuthenticatedMember: !string.IsNullOrWhiteSpace(request.MemberUserId),
            request.IsAssemblyDelivery,
            _timeProvider.GetUtcNow().UtcDateTime));
    }
}
