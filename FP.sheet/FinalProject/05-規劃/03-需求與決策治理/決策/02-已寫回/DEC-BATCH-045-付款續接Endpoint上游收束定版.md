---
batch_id: DEC-BATCH-045
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P355
supersedes:
  - DEC-P352
---

# DEC-BATCH-045｜付款續接 Endpoint 上游收束定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P355 | 採用上游 PR #87 已進 `dev` 的 Owner-scoped `GET /api/v1/orders/{id}/payment-attempts/latest`，移除本分支重複建立的 `/payment-attempts/current`、Reader、DI、測試與 Typed Client。`latest` 回傳最新一筆 Payment Attempt，包含 `paid`／`failed`／`expired`／`cancelled` 等終態；訂單不存在或尚無 Attempt 均回 `404 resource_not_found`，未登入回 `401 authentication_required`，有效 Owner Member／Guest Scope 回 `200 PaymentAttemptDto`。C-15 先恢復 latest Attempt：非終態續接，失敗／過期／取消可顯示結果並允許重試，已付款則保留完成結果。 |

本決策覆寫 [[DEC-BATCH-043-Checkout付款續接與優惠券配送試算定版|DEC-P352]] 的 `/current + 204` 契約；DEC-P353 的 Coupon Shipping Quote 不變。舊紀錄保留作為歷史，不再作為現行 API 或前端實作依據。

## Lowest-Cost Analysis

1. 接受兩個 Endpoint 並存：同一需求會出現 `/current` 與 `/latest` 兩套狀態篩選、授權、測試及前端行為，契約漂移與維護成本不必要，未採用。
2. 只在文件宣告別名：不能消除重複 Controller、Reader、DI、測試與 OpenAPI 公開面，未採用。
3. 以設定切換 Endpoint：既有 `/latest` 已完整進入 `dev`，沒有需要保留兩套執行路徑的環境差異，未採用。
4. 重用上游既有能力並刪除本分支重複路徑：完整涵蓋 Owner／Guest 授權、終態恢復、Provider-backed Reader 與前端測試，且不新增 Schema、套件或服務，採用。
5. 另建聚合服務或新資料模型：既有能力已滿足驗收，成本與回復面更大，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 重新整理付款頁的會員與已驗證訪客，以及維護 Payment API／前端的開發者 |
| 現況風險 | 兩套相同目的 Endpoint 會讓付款頁、OpenAPI 與錯誤語意分岔；`current` 排除終態後也會遺失失敗原因、繳費資訊或付款完成狀態 |
| 觸及頻率 | 每次 C-15 初始載入與重新整理；實際使用量未知 |
| 預期可量測結果 | 公開契約只保留一個 latest Endpoint；前端對非終態續接、可重試終態與已付款終態均有回歸測試；不存在／未授權語意與 API 測試一致 |
| 建置／持續成本 | 刪除重複路徑、同步文件與產生型契約、重跑既有測試；無新增持續費用 |
| 風險成本 | 既有尚未合併的 `/current` 呼叫者必須改用 `/latest`；本專案沒有已發布外部用戶，回復成本低 |
| 信心 | 高；上游能力已有 Application、API 授權矩陣、SQL Reader、前端與 Browser E2E 證據 |
| 成功指標 | Repository 不再存在現行 `/current` 實作或產生型契約；Solution、Payment focused tests、customer-web 與 OpenAPI Diff Gate 全數通過 |
| 停止／回復條件 | 若 `/latest` 無法維持 Owner／Guest 限單隔離，或終態恢復造成重複付款入口，停止合併並回到 Payment Contract Review |

## 執行與驗收邊界

- `latest` 是唯讀查詢，不建立 Attempt、不改變付款狀態。
- Member 不是訂單擁有者時仍可由同瀏覽器的有效 Guest Scope 證明權限；失敗時不得揭露資源是否存在。
- Guest Scope 過期／撤銷回 `401 guest_order_access_expired`；跨訂單 Scope 回 `404 guest_order_scope_mismatch`。
- 沒有付款嘗試不是 `204`，而是 `404 resource_not_found`；前端將此情況視為可建立第一筆 Attempt。
- 非終態 Attempt 繼續既有流程；`failed`／`expired`／`cancelled` 顯示狀態後允許建立新 Attempt；`paid` 不提供重複付款入口。
- 本決策不新增資料表、Migration、套件、外部服務或新的付款寫入命令。

## 影響文件

- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
