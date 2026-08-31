---
batch_id: DEC-BATCH-037
status: applied
decision_date: 2026-08-31
decision_ids:
  - DEC-P343
  - DEC-P344
  - DEC-P345
  - DEC-P346
---

# DEC-BATCH-037｜S-02 例外授權與模擬付款完成邊界定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P343 | PR #66 已合併的 S-02 商品評價與審核視為 2026-09-14 預演前的**一次性核准例外**。此例外只涵蓋 PR #66 已交付的 S-02 範圍，不改變 PM-07：其他 S 功能仍須組長另行明確授權，且不得取代核心 M、Seed、E2E 與 Demo 缺口。 |
| DEC-P344 | M-09 沿用 PR #71 已合併的 `POST /api/v1/simulated-payments/{attemptId}/actions/complete` 與 `CompleteSimulatedPaymentRequest`，只在 Demo Profile 且 `Demo:SimulationEndpointsEnabled=true` 時開放。呼叫者必須是訂單 Owner Member 或持有該訂單有效 Guest Scope，Cookie 寫入由全域 Antiforgery 保護；`simulationKey` 同時作為此操作的唯一重播鍵與模擬 Provider Event 鍵，不再增加第二個 `Idempotency-Key` Header。即時付款 outcome 補齊 `cancelled`。 |
| DEC-P345 | COD 不得透過前台模擬付款完成端點提前入帳；只有訂單履約投影進入 `Delivered`／`PickedUp` 時，才進入同一付款完成行為並把應收款投影為已付款。重播履約事件不得重複付款、通知、稽核或開票。 |
| DEC-P346 | PR #71 已完成的 Owner／Guest、PaymentEvent、PaymentAttempt、`Order.ApplyPaymentProjection`、`PaidAmount`／`PaidAtUtc`、狀態歷程、中央 Audit、通知 Outbox 與 SQL Server 交易視為既有基線。成功付款須再寫入可冪等處理的模擬發票 Outbox，由交易提交後的 Consumer 建立發票；相同付款事件重送不得重複開票。後續差異由 alex 直接 Review；不安排 haru 覆核。 |

即時付款支援 `succeeded`、`failed`、`cancelled`；ATM／超商代碼另可由相同模擬入口在到期前模擬入帳或 `expired`。COD 不使用此前台模擬完成入口，而是在既定 `Delivered`／`PickedUp` 收款事件中進入相同付款完成行為。

## Lowest-Cost Analysis

1. 不補 S-02 授權紀錄：PM-07 與已合併事實持續衝突，未採用。
2. 只補一次性例外紀錄：完整解決範圍治理衝突，且不需回復已合併程式，採用。
3. 停用或回復 S-02：會增加測試與整合成本，且可能連帶影響 PR #66 的 M-15／INT-04，沒有必要。
4. 只把文件改成 PR #71 現況：無法滿足已確認的付款取消、COD 收款時點與付款後自動開票，未採用。
5. 重用 PR #71 既有 Endpoint、DTO、PaymentAttempt／Event、Order、Idempotency、Audit 與 Outbox，只補 `cancelled`、履約收款接點及發票事件／Consumer：不新增外部服務、套件或資料模型即可符合核心驗收，採用。
6. 建立獨立 Mock Provider／完整簽章 Webhook：第一版 Demo 不需跨網路回呼，建置與維運成本較高，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 顧客／訪客、yinyin、alex、Demo 操作者 |
| 現況風險 | S-02 授權鏈不完整；PR #71 尚無 `cancelled`，COD 可在履約前入帳，付款成功後也沒有自動建立模擬發票的後提交路徑 |
| 預期可量測結果 | S-02 例外可追溯；相同付款或履約結果重送只有一次副作用；成功後 Attempt、Event、Order、PaidAmount、Outbox 與後續發票一致 |
| 建置／持續成本 | 延伸既有 Application／Infrastructure／API／Vue 與測試；無新增外部服務、套件、Schema 或固定費用 |
| 風險成本 | Demo Endpoint 若環境防線錯誤可能被誤開；跨模組交易若不原子可能造成付款與訂單不一致 |
| 信心 | 中高；契約與資料模型已存在，正式 Completion 呼叫端、Provider-backed replay 與 E2E 仍待實作 |
| 成功指標 | Demo Profile 外 404／不可用；Owner／Guest Scope、Antiforgery、冪等及金額不變量通過；核心交易 E2E 可重跑 |
| 停止／回復條件 | 任一重複付款、COD 提前入帳、跨訂單完成、金額不一致、未授權成功、通知／發票重複即停止合併；可關閉 `Demo:SimulationEndpointsEnabled` 回復模擬入口為不可用 |

## 影響文件

- [[05-規劃/01-時程與進度/40天開發計畫]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/03-資料與一致性/狀態機設計]]
- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]

## 實作邊界

- 本決策定版觸發、交易、冪等、環境防線與責任；PR #71 是已合併基線，但不代表剩餘差異或 M-09 E2E 已完成。
- 實作不得加入外部金流、真實 Webhook、額外資料庫或新套件。
- PR 必須同步 OpenAPI／Typed Client、API／Application／SQL Server Provider-backed tests、QA-08 授權矩陣與核心交易 E2E。
