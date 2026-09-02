namespace DoSelect.Application.Orders;

/// <summary>
/// M-10 逾時取消的排程入口（庫存規則.md：「背景排程自動取消逾時訂單並釋放保留庫存」）。
///
/// 組長 PR #85 round-1 review [P1]：先前的排程只釋放庫存保留，訂單本身留在 PendingPayment，
/// 優惠券座位與待處理組裝資源也沒有回收。除了讓訂單永遠停在待付款之外，付款成功
/// 與逾時掃描在期限邊界還可以同時成立：付款先讀到 PendingPayment，排程另行釋放 Reservation，付款
/// 再提交 Confirmed——最後是一筆已付款卻沒有有效庫存保留的訂單。
///
/// 所以逾時處理的單位是「訂單」而不是「保留」：在同一個交易裡把訂單轉成 Cancelled 並透過既有的
/// 共用取消流程回收全部資源。訂單列本身被寫入，RowVersion 就是併發仲裁者——付款與排程誰後提交誰
/// 拿到 DbUpdateConcurrencyException，不會出現兩邊都成功的中間狀態。
/// </summary>
public interface IOrderTimeoutCancellationService
{
    /// <summary>
    /// 取消付款期限已過且仍停在 PendingPayment 的訂單，一次最多 <paramref name="batchSize"/> 筆，
    /// 依 (PaymentDueAtUtc, Id) 穩定排序。回傳實際取消的筆數；呼叫端重複呼叫直到回傳值小於
    /// <paramref name="batchSize"/>，即可把停機累積的 backlog 逐批清完。
    ///
    /// 冪等：已經不是 PendingPayment 的訂單會被跳過，輸給付款的那一筆也只是跳過，不會拋出。
    /// </summary>
    Task<int> CancelOverduePendingPaymentOrdersAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken);
}
