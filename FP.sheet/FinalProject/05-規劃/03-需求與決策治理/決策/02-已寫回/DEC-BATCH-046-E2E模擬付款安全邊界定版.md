---
batch_id: DEC-BATCH-046
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P356
---

# DEC-BATCH-046｜E2E 模擬付款安全邊界定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P356 | 產品行為維持只有 Demo Environment 且 `Demo:SimulationEndpointsEnabled=true` 才能使用模擬付款完成端點；另允許隔離的 `E2E` Environment 在同一個顯式開關為 `true` 時啟用該端點，以驗證 Cart → Checkout → Payment → Invoice 的正式 HTTP 旅程。`Development`、`Production` 與其他 Environment 只要開啟此設定仍須啟動失敗。本機 E2E Host 只使用可丟棄的 `DoSelectE2E_<GUID>` SQL Server 資料庫；GitHub CI 可使用該 Job 專屬且隨 SQL Server service container 銷毀的 `DoSelectE2e`。兩者都只能使用測試 HMAC 與測試 Cookie 設定，不得連線共用 `DoSelectDb`，也不得把 E2E 例外當成公開產品 Profile。 |

本決策只新增測試環境邊界，不覆寫 [[DEC-BATCH-037-S02例外授權與模擬付款完成邊界定版|DEC-P344]] 的產品授權、Owner／Guest Scope、Antiforgery、冪等或 COD 規則。

## Lowest-Cost Analysis

1. 不新增完整核心 E2E：無法證明正式 UI、API、Guest Cookie、付款完成、Outbox 與發票能在同一旅程協作，未採用。
2. 以人工改資料或直接呼叫 Application Service 代替：無法覆蓋 HTTP、Antiforgery、授權與瀏覽器 Cookie 邊界，未採用。
3. 延伸既有 Environment／Feature Flag：只在隔離 E2E Host 重用既有 Demo Endpoint，無新 Endpoint、Schema、套件或服務，採用。
4. 為 CI 建立 HTTPS 憑證或第二套付款模擬服務：可驗證相同行為，但增加憑證或平行服務的建置、維護及故障面，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 開發團隊、Demo 操作者與負責核心交易驗收的人員 |
| 現況風險 | 單層測試皆通過時，跨 UI／API／SQL／Cookie 的核心交易仍可能在現場才失敗 |
| 觸及頻率 | 每次執行核心交易 Playwright 與對應 CI；正式產品請求不受影響 |
| 預期可量測結果 | 固定 Guest Cart 可完成 Checkout、同請求 replay、Guest 限單驗證、模擬付款成功與發票顯示；SQL 測試證明 replay 不重複與最後一件只成功一筆 |
| 建置／持續成本 | 延伸既有測試設定、Seed 與 Playwright；持續成本為既有 CI 執行時間，無新增外部費用 |
| 風險成本 | E2E 設定誤用可能暴露模擬寫入入口；以 Environment 白名單、顯式開關、fail-fast 與專屬資料庫限制 |
| 信心 | 高；產品環境的拒絕測試、隔離 E2E 允許測試、SQL 交易測試與完整瀏覽器旅程均可自動重跑 |
| 成功指標 | Demo／E2E 允許矩陣及 Development 拒絕測試通過；核心 Playwright 綠；測試資料庫執行後刪除；無共用資料庫變更 |
| 停止／回復條件 | 若 Production／Development 可啟用端點、E2E 連到共用資料庫、Owner／Guest 隔離失效或產生重複付款／訂單，立即停止合併；回復 E2E Environment 例外及測試 Host 設定 |

## 執行與驗收邊界

- `Demo` 是產品展示 Profile；`E2E` 只是一個不可部署的自動化測試 Environment。
- E2E 必須同時顯式設定 `Demo:SimulationEndpointsEnabled=true`，預設值仍為 `false`。
- E2E 可使用非敏感、固定的測試 HMAC 值；不得讀取或提交真實 Secret。
- 本機核心旅程使用專屬 `DoSelectE2E_<GUID>` 並於測試結束後刪除；GitHub CI 使用 Job 專屬 SQL Server service container 內的 `DoSelectE2e`，Job 結束即整體銷毀。兩者都先套用正式 Migration 與確定性 Seed。
- 背景工作只在需要驗證付款後發票 Outbox 的旅程開啟；其他 E2E 可維持關閉以避免非必要副作用。
- 本決策不新增公開 API、資料表、Migration、套件、真實金流或真實物流。

## 影響文件

- [[03-架構/04-安全與檔案/設定與Secrets管理規範]]
- [[03-架構/08-測試與驗收/測試策略]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/02-分工與交接/工程包/Alex-個人剩餘交付實作計畫]]
- [[05-規劃/02-分工與交接/工程包/Alex-暫時接手Haru-負責範圍補全推進計畫]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
