---
type: decision-record
batch_id: DEC-BATCH-028
title: 零元訂單最低應付金額定版
status: applied
created_at: 2026-08-27
applied_at: 2026-08-27
source: alex 明確選擇方案 B
decision_ids:
  - DEC-P319
---

# DEC-BATCH-028｜零元訂單最低應付金額定版

## 背景

優惠券可以把適用商品折扣到 0，且運費可能同時為 0。現行 `PaymentAttempt.Amount` 必須大於 0；若 Checkout 沒有先驗證，流程會在已開始建立訂單後由 Payment Entity 丟出非業務例外，無法提供穩定錯誤碼，也增加整合者誤解付款狀態的風險。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P319 | DoSelect V1 不接受零元訂單。Checkout 以後端可信價格、優惠券、運費及組裝費計算 `RawPayableAmount`，再依既定 `MidpointRounding.AwayFromZero` 四捨五入為整數 TWD；若 `GrandTotal < NT$1`，回 `409 order_total_below_minimum`。拒絕必須發生在建立 Order／PaymentAttempt 前，整筆交易不得留下 Order、Item、Reservation、Movement、CouponRedemption、OrderCoupon、PaymentAttempt 或 Cart 轉換；使用者需移除優惠券或調整購物車後重新結帳。`PaymentAttempt.Amount > 0`、既有付款狀態、成功 DTO 與資料模型均不改變。 |

## 最低成本分析

1. 接受現況：零元結果會落入 Payment Entity 的一般例外，可能回 500，無法達成安全且可處理的業務失敗，排除。
2. 以文件或人工限制不建立全額折抵券：Seed、管理設定或未來資料仍可能產生零元，後端不變量未被保護，排除。
3. 在既有 Checkout 金額計算後加入最低 NT$1 驗證、穩定錯誤碼與 SQL 回滾測試：完整滿足政策且不改 Schema／狀態／成功契約，採用。
4. 允許零元訂單並新增免付款狀態：需修改 Order／Payment／發票／報表與 API，成本及整合風險較高，且第一版沒有必要，排除。

## 商業影響

- 受影響者：使用全額折抵優惠券且同時無其他應付費用的顧客、優惠券管理者與付款整合者。
- 目前風險：零元結帳可能變成非預期 500，或被誤判為需要／已完成付款。
- 觸及頻率：只在所有折扣與費用計算後四捨五入低於 NT$1 時觸發。
- 預期可量測成果：此情境固定回 `409 order_total_below_minimum`，核心交易資料新增筆數為 0，Cart 與庫存不變。
- 建置與持續成本：一個後端條件、一個 append-only 錯誤碼與一個 SQL Server 回歸案例；無新套件、Schema、服務或持續費用。
- 主要風險成本：優惠券管理員建立無法在單一低價購物車使用的全額折抵組合，需要 UI 顯示可理解的調整提示。
- 信心：高；既有付款模型已要求正金額，最小修正與既有狀態、發票及報表契約相容。
- 成功指標：SQL Server 測試證明 409 code 與零資料副作用；完整 Build／Test／Format 綠燈；Endpoint 與錯誤碼目錄一致。
- 停止／回退條件：若未來商業需求明確要求贈送零元實體商品，停止沿用本政策並另行設計免付款狀態、發票與報表口徑；不得只略過 PaymentAttempt。

## 影響文件

- [[02-領域需求/03-交易與履約/優惠券規則]]
- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/API錯誤碼目錄]]
- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
