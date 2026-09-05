namespace DoSelect.Application.Refunds;

/// <summary>
/// 發票查詢將折讓保存的內部退款識別換回對外識別時使用的 Refunds-owned port。
/// </summary>
public interface IRefundInvoiceReferenceReader
{
    Task<IReadOnlyDictionary<long, Guid>> FindManyAsync(
        IReadOnlyCollection<long> refundIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 發票作廢前由 Refunds 回答同一訂單是否已有成功退款；Invoicing 不直接讀 Refunds 資料表。
/// </summary>
public interface IRefundInvoiceVoidReader
{
    Task<bool> HasSucceededRefundAsync(
        long orderId,
        CancellationToken cancellationToken = default);
}
