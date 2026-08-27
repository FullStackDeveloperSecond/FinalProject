---
type: decision-record
batch_id: DEC-BATCH-029
title: 訂單自助取消原子一致性與原因碼定版
status: applied
created_at: 2026-08-27
applied_at: 2026-08-27
source: alex 明確選擇 A1、B1
decision_ids:
  - DEC-P320
  - DEC-P321
---

# DEC-BATCH-029｜訂單自助取消原子一致性與原因碼定版

## 背景

PR #43 提供會員在待付款階段自助取消訂單。取消不只是更新訂單狀態，還涉及 Checkout 已建立的庫存保留、優惠券使用次數、狀態歷程與中央 Audit；若其中任一步驟失敗但其他資料已提交，會造成可售量、優惠券額度與訂單畫面互相矛盾。取消理由也需要固定字彙，避免前後端各自新增自由格式值。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P320 | 會員在 `PendingPayment` 自助取消時，訂單轉為 `Cancelled`、所有 Active 庫存保留釋放、InventoryBalance 還原、建立相反方向 InventoryMovement、Reserved 優惠券兌換釋放、符合期間與額度條件的 `Exhausted` 優惠券恢復 `Active`、新增 OrderStatusHistory 與中央 `order.cancel` Audit，必須在同一 `DoSelectDbContext`／SQL Server 交易中原子完成。任何驗證、RowVersion 衝突、Audit 或資料庫寫入失敗都整體回滾；不得只改訂單狀態，也不得新增專用取消稽核表或 Migration。 |
| DEC-P321 | 顧客自助取消 `reasonCode` 固定為 `changed_mind`、`ordered_by_mistake`、`found_better_price`、`shipping_too_slow`、`other`。`note` 為最多 500 字的選填補充，保存在中央 Audit 的 Reason；不得以 `note` 取代固定原因碼，也不得接受清單外值。 |

## 最低成本分析

1. 接受只更新訂單狀態：會留下未釋放庫存與優惠券額度，破壞資料一致性，排除。
2. 以文件或人工清理補救：無法防止併發、部分提交或漏清理，排除。
3. 延伸既有 Order Service、InventoryReservation／Balance／Movement、CouponRedemption、OrderStatusHistory 與中央 Audit，沿用單一 DbContext 交易：不新增 Schema、服務或相依套件即可完整滿足，採用。
4. 新增 Cancellation Aggregate、Outbox Saga 或專用 Audit 表：第一版待付款即時取消沒有跨資料庫或外部系統需求，成本與維護面過大，排除。

## 商業影響

- 受影響者：待付款訂單的會員、庫存與優惠券營運人員。
- 目前風險：部分取消可能造成商品被幽靈保留、優惠券使用次數未返還或缺少可追溯原因。
- 觸及頻率：每次會員在 `PendingPayment` 狀態執行自助取消時。
- 預期可量測成果：成功取消時訂單、庫存、優惠券、歷程與 Audit 同步完成；任一失敗時所有資料保持取消前狀態。
- 建置與持續成本：延伸既有交易路徑與 SQL Server 回歸測試；無新套件、Schema、服務或持續費用。
- 主要風險成本：取消交易所需鎖定與查詢增加；以單筆訂單範圍及既有樂觀併發限制風險。
- 信心：高；本次 Provider-backed 與 API 整合測試覆蓋成功與 RowVersion 回滾。
- 成功指標：取消成功與失敗案例皆驗證四類資料一致性；Build、前端型別／測試與 SQL 測試通過。
- 停止／回退條件：若未來取消需呼叫已完成付款、物流或外部不可逆操作，停止沿用單一同步交易假設並另行決定退款／補償流程；不得在此路徑直接擴張。

## 影響文件

- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[02-領域需求/03-交易與履約/優惠券規則]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
