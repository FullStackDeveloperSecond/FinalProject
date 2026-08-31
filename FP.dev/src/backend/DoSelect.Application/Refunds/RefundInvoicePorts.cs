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
