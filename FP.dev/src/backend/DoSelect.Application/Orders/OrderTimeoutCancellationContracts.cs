namespace DoSelect.Application.Orders;

/// <summary>
/// 一批掃描停在哪裡。組長 PR #85 round-3 review [P2]：持續失敗的資料若不往後推進，每一輪都會重新
/// 選到最舊的同一批，排在它們後面的健康逾時訂單永遠等不到處理。排序鍵是 (PaymentDueAtUtc, Id)，
/// 游標就用同一組值。
/// </summary>
public sealed record OrderTimeoutCursor(DateTime PaymentDueAtUtc, long OrderId);

/// <summary>
/// 一批掃描的結果。
///
/// 組長 PR #85 round-3 review [P2]：只回「取消了幾筆」不夠——呼叫端用它判斷「這批有沒有取滿」，
/// 而失敗或被跳過的訂單同樣佔用了一個名額。<see cref="Examined"/> 才是判斷還有沒有下一批的依據；
/// <see cref="Failed"/> 讓呼叫端知道這一輪留下了多少需要人工處理的資料。
/// </summary>
public sealed record OrderTimeoutSweepResult(
    int Examined,
    int Cancelled,
    int Failed,
    OrderTimeoutCursor? NextCursor);

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
///
/// 裁定 B1 之後這是唯一的逾時入口：沒有任何路徑可以「釋放庫存卻不取消訂單」。
/// </summary>
public interface IOrderTimeoutCancellationService
{
    /// <summary>
    /// 取消付款期限已過且仍停在 PendingPayment 的訂單，一次最多 <paramref name="batchSize"/> 筆，
    /// 依 (PaymentDueAtUtc, Id) 穩定排序，從 <paramref name="after"/> 之後開始。
    ///
    /// 冪等：已經不是 PendingPayment 的訂單會被跳過，輸給付款的那一筆也只是跳過，不會拋出。
    /// 庫存狀態不一致的訂單留給人工處理，並留下帶訂單識別的 Warning。
    /// </summary>
    Task<OrderTimeoutSweepResult> CancelOverduePendingPaymentOrdersAsync(
        DateTime now,
        int batchSize,
        OrderTimeoutCursor? after,
        CancellationToken cancellationToken);
}
