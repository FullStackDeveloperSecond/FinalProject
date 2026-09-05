---
batch_id: DEC-BATCH-041
status: applied
decision_date: 2026-09-01
decision_ids:
  - DEC-P350
---

# DEC-BATCH-041｜Checkout 配送查詢責任邊界定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P350 | 採用 B1：維持目前只接手 yinyin 範圍，不擴大接手 Terry-owned Shipping／Shopping 實作。正式 `GET /api/v1/cart/shipping-options` 與 `GET /api/v1/convenience-stores` 仍由 Terry 依既有規格交付 Application port、Provider-backed query、Controller、OpenAPI／Typed Client 與測試。C-14 Checkout 與 WP-08 核心 E2E 在兩支查詢落地前維持等待；WP-07 已完成的付款、訂單摘要、發票與訪客驗證可獨立保存，但不得宣告完整完成。前端不得硬寫配送方式代碼、門市 PublicId、費用、資格或付款方式，也不得跨 owner 直接查 Shipping／Shopping 資料表。 |

## Lowest-Cost Analysis

1. 接受未標示的缺口：會讓 WP-07／WP-08 看似可繼續，增加前端硬寫與重複實作風險，未採用。
2. 只以責任與等待條件收束流程／文件：足以維持既有 owner 邊界，也保留已完成成果，採用。
3. 以前端設定或假資料補齊配送選項／門市：無法證明費用、資格與付款方式來自正式 Provider，未採用。
4. 重用或擴充 Terry-owned 查詢路徑：應由既有 owner 完成；目前沒有擴大接手授權，不由 alex／yinyin 建立平行實作。
5. 由 alex 跨 owner 補齊兩支查詢：能縮短等待，但增加交接、重工與資料邊界風險；B1 已足以滿足目前只接手 yinyin 的範圍，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 所有會員與訪客 Checkout 顧客，以及 Shipping／Shopping、Checkout 交付者 |
| 現況風險 | 若 Checkout 自行硬寫或重複查詢，配送費、可用資格與付款方式可能與正式資料來源漂移 |
| 觸及頻率 | 每次載入 C-14 Checkout 與執行核心交易 E2E；實際流量未知 |
| 預期可量測結果 | 兩支正式 Endpoint 由單一 owner 交付後，C-14 只消費 Typed Client 並可續作 WP-08 |
| 建置／持續成本 | B1 不新增程式與基礎設施；成本是跨 owner 協調及 Checkout／E2E 等待時間 |
| 風險成本 | 主要是上游延遲；以明確 blocker、禁止硬寫與不得誤報完成控制 |
| 信心 | 高；正式 Endpoint／DTO 已存在於文件，但目前缺少 Application、Infrastructure、API 與 OpenAPI 實作證據 |
| 成功指標 | 兩支 Endpoint、Provider-backed 測試、OpenAPI／Typed Client 全部落地，C-14 與 WP-08 再恢復執行 |
| 停止／回復條件 | 若前端出現硬寫代碼／費用／資格，或 alex／yinyin 直接查 Terry-owned 資料表，立即停止並回復責任邊界 |

## 執行與驗收邊界

- Terry 上游交付前，WP-07 只能標示部分完成，WP-08 不得開始宣告核心 Checkout E2E 完成。
- 上游驗收至少包含 Provider-backed 測試、公開 API 契約、OpenAPI 與 Typed Client 同步；只有文件或 InMemory 假資料不構成解除阻塞。
- C-14 解鎖後只能透過正式 Typed Client 取得配送選項與示範門市，不建立平行來源。
- 本決策不改變既有 Checkout POST、政策版本查詢、付款、發票或 Guest Owner 驗證契約。
- 本決策不授權新增 Entity、Mapping、Migration、Production SQL 或跨 owner 程式碼。

## 影響文件

- [[05-規劃/02-分工與交接/工程包/Yinyin-負責範圍補全推進計畫]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
