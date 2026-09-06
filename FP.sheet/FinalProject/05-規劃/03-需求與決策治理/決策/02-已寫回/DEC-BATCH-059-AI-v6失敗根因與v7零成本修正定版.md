---
batch_id: DEC-BATCH-059
status: applied
decision_date: 2026-09-05
decision_ids:
  - DEC-P399
  - DEC-P400
  - DEC-P401
  - DEC-P402
---

# DEC-BATCH-059｜AI v6 失敗根因與 v7 零成本修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P399 | Live Runner 必須忠實模擬正式產品流程：SearchIntent 含補問或既有零件確認時停止於該階段，不得再呼叫 Explanation 或虛構推薦；只有意圖完整、無補問、無待確認既有零件且有預期候選時才評分推薦理由。 |
| DEC-P400 | 新增正式大寫 Semantic Key `STORAGE_CAPACITY_GB` 表示儲存容量，並由 SQL 型錄 Metadata 提供分類對 Semantic Key 白名單；Storage 不得使用 `MEMORY_*`。模型回傳 TB 時由後端依 `1 TB = 1024 GB` 正規化為 GB，非法單位／分類與規格不相符一律 Fail Closed。此 Key 為搜尋／顯示規格，不加入零件相容性硬規則；不新增資料表或 Migration。開發最小 Seed 以冪等方式補 2TB 容量。 |
| DEC-P401 | 商品 Prompt 升為 `product-search-v7`：用途後的單一口語金額視為最高預算；「存家庭照片」是偏好，不是 General purpose；硬性數值已明示時不得要求補問；使用者未提及時不得追問螢幕或周邊；仍保留真正預算衝突與必要相容性資訊不足的補問。範例只能是泛化規則，不複製 Release 案例答案。 |
| DEC-P402 | AI 資料集升為 `zh-TW-v1.0.4-draft`，grader 升為 `deterministic-v1.1.3`；`SEARCH-NOVICE-019` 正式期待為 Storage、`STORAGE_CAPACITY_GB >= 8192 GB` 與「家庭照片」偏好，不再標為 General purpose。Grader 必須精確核對分類、結構化必要規格與已宣告偏好。v6 Live 結果維持歷史 `FAIL`；先完成零成本回歸，本批不呼叫 OpenAI，新 v7 Smoke 與 66 次 baseline 均須另行授權。 |

## Lowest-Cost Analysis

1. 維持現況：Runner 會產生正式流程不存在的推薦，且 8TB 儲存可被誤判為 8192GB RAM，不能滿足評估可信度與顧客正確性，未採用。
2. 只修改人工覆核文字：無法阻止正式 Adapter 接受分類錯誤的規格，也不能讓自動 Gate 找出相同回歸，未採用。
3. 只改 Prompt：模型仍可能回傳錯誤 Key，且 Runner／grader 本身的 fidelity 缺陷不會消失，未採用。
4. 延伸既有 Metadata、驗證器、Runner 與資料產生流程：使用既有契約加入一個規格 Key、分類白名單、正規化及精確 grader，不新增公開 DTO、資料表、套件或服務，能完整滿足條件，採用。
5. 新增 deterministic parser、向量服務或新 Schema：既有最小延伸已能建立 Fail Closed 邊界，成本與回復面較高，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用自然語言搜尋的訪客／會員、AI-09 評估執行者與審查者 |
| 顧客價值 | 8TB 儲存不再顯示為不可能的 RAM 規格；資訊足夠時不以多餘問題中斷導購 |
| 現況風險 | v6 Smoke 的評估器與正式流程不一致，且資料契約無法區分儲存容量與記憶體容量 |
| 可量測結果 | 兩個失敗案例的 deterministic 回歸、分類／規格拒絕、TB→GB、SQL Metadata、120 筆資料驗證及 Solution Build 通過 |
| 建置與維護成本 | 延伸既有 C# 契約、Seeder、Prompt、Runner、Dataset 與 Grader；沒有新服務、套件、Migration 或執行費用 |
| 風險／回復 | 內部 Metadata 新欄位採選填以維持既有測試替身相容；Prompt／Dataset／Grader 可按版本回復，舊 v6 證據不改寫 |
| 信心 | 零成本契約與 Provider-backed SQL 邊界信心高；真實模型品質仍須新 Live Smoke 驗證 |

## 驗證與邊界

- RED 先證明 Runner 會錯誤進入推薦，以及 Storage 可接受 RAM Key／TB 不會正規化；修正後聚焦案例均通過。
- Application AI 47／47、Infrastructure AI focused 50／50、API AI exact classes 24／24、系統 PowerShell SQL Server focused 1／1 通過。
- 資料來源／衍生檔 `--check`、120 筆驗證與隱私掃描通過；Solution Build 為 0 warning／0 error。
- SQL 測試只建立並刪除唯一暫存資料庫；沒有修改共用 `DoSelectDb`。
- 本批沒有真實 OpenAI 呼叫、Token 或費用，不等於 v7 Live Gate 通過；AI-09 維持進行中。

## 影響文件與追蹤

- 商品搜尋 Application／Infrastructure 契約、型錄 Semantic Key、最小開發 Seeder、Live Runner 與聚焦測試。
- `FP.dev/evals/ai/v1` 的來源、JSONL、Schema、Manifest、Grader、README 與零成本結果報告。
- AI 應用／測試規格、AI-09 追蹤列、M 功能矩陣、Alex 交付計畫、決策索引／紀錄與本次日誌。
