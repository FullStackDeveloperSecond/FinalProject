namespace DoSelect.Application.Payments;

/// <summary>
/// 取得一張訂單最新的一筆付款嘗試，供付款頁重新整理後恢復畫面。
/// </summary>
/// <remarks>
/// <para>
/// 擁有者比對放在這一層，所以不必啟動 HTTP 就測得到 —— 留在 Controller 的話，
/// 就只能靠整合測試覆蓋。
/// </para>
/// <para>
/// 投影沿用 <see cref="PaymentAttemptDtoMapper"/>，與建立付款嘗試、模擬付款完成
/// 走同一份對外形狀，不另建第二套 DTO。
/// </para>
/// </remarks>
public sealed class LatestPaymentAttemptService
{
    private readonly ILatestPaymentAttemptReader _reader;

    public LatestPaymentAttemptService(ILatestPaymentAttemptReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task<LatestPaymentAttemptResult> FindLatestAsync(
        PaymentAttemptViewer viewer,
        Guid orderPublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var order = await _reader.FindOrderAsync(orderPublicId, cancellationToken);
        if (order is null)
        {
            return new LatestPaymentAttemptResult.NotFound();
        }

        // 會員必須是這張訂單的擁有者。這個結果只讓呼叫端決定要不要接著檢查同一瀏覽器
        // 的 Guest cookie；兩條路都不成立時，對外仍折成 404 而不是 403 ——
        // 區分「不存在」與「不是你的」等於告訴外人這個 id 存在。
        if (viewer is PaymentAttemptViewer.Member member &&
            !string.Equals(order.MemberUserId, member.MemberUserId, StringComparison.Ordinal))
        {
            return new LatestPaymentAttemptResult.MemberAccessDenied();
        }

        var attempt = await _reader.FindLatestAsync(order.OrderId, cancellationToken);
        return attempt is null
            ? new LatestPaymentAttemptResult.NotFound()
            : new LatestPaymentAttemptResult.Found(PaymentAttemptDtoMapper.Map(attempt));
    }
}
