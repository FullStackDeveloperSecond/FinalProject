---
batch_id: DEC-BATCH-055
status: applied
decision_date: 2026-09-05
decision_ids:
  - DEC-P389
  - DEC-P390
  - DEC-P391
---

# DEC-BATCH-055｜AI 商品意圖分類、人工覆核與安全診斷定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P389 | 採方案 A：使用者只說一般「主機」，且沒有用途、效能目標或配／組／組裝等字詞時，SearchIntent 採 `PrebuiltComputer`；明示組裝，或提出用途＋預算的整機需求時採 `CustomBuild`。既有「遊戲美術」等職稱不得自動推論 Gaming，除非使用者明確表示遊戲用途。公開 Intent enum 不新增值。 |
| DEC-P390 | 採用 Run `20260904T144911Z` 的初步內容覆核為正式 Alex 覆核：`SEARCH-NOVICE-025`、`SUPPORT-POLICY-010`、`SUPPORT-SECURITY-016` Pass；`SEARCH-CREATOR-013`、`SEARCH-NOVICE-019`、`SEARCH-NOVICE-026` Fail。整輪人工結果 3 Pass／3 Fail，與自動 Verdict `FAIL` 一致。 |
| DEC-P391 | InvalidOutput 證據只保存固定驗證失敗原因碼與不含值的欄位名稱；不得保存模型原始無效輸出、使用者文字、API Key、Authorization Header 或個資。診斷欄位只進內部 SearchIntent result 與本機評估 JSONL，不加入公開 HTTP DTO、資料表或產品日誌。 |

## Lowest-Cost Analysis

1. 維持分類不一致：會使已核准案例、Prompt 與 deterministic grader 對同一句「主機」產生不同答案，無法重驗，未採用。
2. 只更新文件：無法讓實際模型指令與測試遵守分類，未採用。
3. 延伸既有 Prompt 與內部 result：可沿用現有三種 Intent、Runner 與 Fail Closed，不改公開 API 或資料庫，採用。
4. 新增 `Undetermined` Intent 或保存完整原始模型輸出：前者擴大公開契約，後者增加敏感資料留存風險；目前均非必要，未採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | 用一般語言搜尋整機的訪客／會員、AI-09 評估與故障分析人員 |
| 現況損失／風險 | 相同「主機」文字可能落入不同購買流程；InvalidOutput 只有總類型，無法在不重跑付費模型的情況下定位驗證層 |
| 觸及範圍／頻率 | 每次 AI 商品意圖解析與每筆失敗評估；尚無正式流量 |
| 預期可量測結果 | Prompt／dataset／test 對分類一致；未來 InvalidOutput JSONL 含固定 reason code／field 且不含 raw output |
| 建置／持續成本 | 既有 Prompt、內部 contract、Runner、測試與文件的小型修改；無新依賴、Migration 或固定費用 |
| 預期風險成本 | `PrebuiltComputer`／`CustomBuild` 邊界仍可能受自然語言歧義影響；以補問、固定案例與三輪評估控制 |
| 信心 | 內部契約與資料安全可由 deterministic tests 驗證，信心高；模型品質須另行付費重驗，信心不足 |
| 成功指標 | focused tests、Build、Format、120 筆資料驗證及 Dry Run 通過；下一次另行授權 Smoke 能留下安全診斷並改善 013／019／026 |
| 停止／回復條件 | 若需新增公開 Intent、保存 raw output、改資料庫或對外錯誤 DTO，停止並重新決策；Prompt 可回復到 v4，但不得讓文件與資料集保持衝突 |

## 證據

- `FP.dev/evals/ai/v1/results/2026-09-04-low-latency-smoke-f195c453.md`
- `FP.dev/.run/ai-evals/20260904T144911Z/human-review.md`
- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/src/backend/DoSelect.Application/Ai/AiProductSearchContracts.cs`
- `FP.dev/tools/DoSelect.AiEvals/LiveEvaluationRunner.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/OpenAiProductSearchClientTests.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/LiveEvaluationPlanTests.cs`
