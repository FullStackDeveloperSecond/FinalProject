---
batch_id: DEC-BATCH-053
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P381
  - DEC-P382
  - DEC-P383
  - DEC-P384
---

# DEC-BATCH-053｜AI 商品搜尋單次模型與 5 秒降級定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P381 | 組長選擇方案 A：保留既有公開 API 與 DEC-P170 的商品搜尋 P95 ≤5 秒目標，不新增非同步工作、第二套 Endpoint、快取服務、依賴、Schema 或資料庫。此決策完成 DEC-P377 的延遲方向，並只覆寫 DEC-P113 的「商品搜尋 8 秒、暫時錯誤與格式錯誤同步重試」部分；AI 客服 12 秒與最多一次暫時錯誤／格式修復重試維持不變。 |
| DEC-P382 | 每次商品搜尋只允許一次 OpenAI Responses 意圖解析呼叫，單次逾時 5 秒；429、5xx、網路錯誤、逾時或無效輸出均不得在同步請求內重試，立即 Fail Closed 並走既有關鍵字搜尋降級。 |
| DEC-P383 | 推薦理由不再呼叫模型，改由後端只使用已核准候選的名稱、品牌、分類、售價／牌價、最高預算與 Badges 確定性組合。不得宣稱 Fixture 未提供的型號、效能、Benchmark 或相容性；外部 API／DTO 保持不變。 |
| DEC-P384 | 商品 Prompt 升為 `product-search-v4`，Live Runner 每個商品案例的規劃模型請求由 2 次降為 1 次；22 個 live 案例三輪 Release 共規劃 66 次。零成本 focused tests 與 dry run 可直接執行；任何付費 Smoke／Release baseline 仍須另行核准，且 v4 尚未實測的品質、P95、Token 與成本不得宣稱通過。 |

## Lowest-Cost Analysis

1. 維持兩次模型呼叫與同步重試：已量測商品 P95 18,742 ms，不能滿足既定 5 秒目標，未採用。
2. 只調文件或放寬 5 秒門檻：會掩蓋真實使用者等待與基本電商降級風險，未採用。
3. 用既有設定與降級能力將逾時改為 5 秒：仍保留第二次模型理由呼叫，無法完整滿足同步路徑成本與延遲邊界，單獨使用不足。
4. 重用既有意圖、SQL 候選與關鍵字降級路徑，將理由改為後端確定性文字：不改公開契約即可移除第二次模型呼叫並滿足 Fail Closed，採用。
5. 新增背景工作、快取或另一套 API：較高建置、操作與回復成本，現階段無必要，未採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | 使用自然語言搜尋的訪客／會員、AI-09 評估與展示人員 |
| 現況損失／風險 | 修正版 Smoke 的商品 P95 為 18,742 ms；兩階段模型與同步重試會延長等待並增加每次搜尋 Token／費用 |
| 觸及範圍／頻率 | 每次 AI 商品搜尋；未提供正式流量，頻率證據不足 |
| 預期可量測結果 | 每個 live 商品案例規劃請求從 2 降至 1；三輪規劃從 96 降至 66；同步模型呼叫 5 秒後即降級 |
| 建置／持續成本 | 既有程式、測試、設定與文件的 bounded change；無新依賴、Migration 或固定費用 |
| 預期風險成本 | 確定性理由較不自然；以三語模板與可信候選事實降低。模型意圖若超時會較早降級，基本關鍵字搜尋仍可用 |
| 信心 | 單次呼叫與零第二次 HTTP 有自動測試，高；live P95／品質尚未重跑，低至中 |
| 成功指標 | focused tests、資料集驗證與 dry run 通過；另經授權的 v4 live baseline 達 Schema／Intent／P95／成本 Gate |
| 停止／回復條件 | 若公開 API 需改動、確定性理由無法達驗收，或經授權的 live baseline 仍無法達 Gate，停止擴大執行並重新決策；可回復到前一 Prompt／Adapter commit，但不得默默放寬 5 秒門檻 |

## 證據

- `FP.dev/evals/ai/v1/results/2026-09-04-remediation-smoke-f195c453.md`
- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/OpenAiProductSearchClientTests.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/LiveEvaluationPlanTests.cs`
