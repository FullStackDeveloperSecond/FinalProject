---
batch_id: DEC-BATCH-054
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P385
  - DEC-P386
  - DEC-P387
  - DEC-P388
---

# DEC-BATCH-054｜AI 商品搜尋低延遲推理設定定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P385 | 保留 `gpt-5.6-luna`、單次 Responses 呼叫、5 秒逾時、零同步重試與預設 service tier；不調整公開 API、DTO、資料庫或 AI 客服設定。 |
| DEC-P386 | 商品 SearchIntent 是延遲敏感的結構化分類，Responses 請求固定傳送 `reasoning.effort: none`，取代 Luna 預設的 medium 推理量；strict JSON Schema、白名單與後端驗證仍為正確性邊界。 |
| DEC-P387 | 商品 SearchIntent 請求固定傳送 `text.verbosity: low`，降低非必要輸出；既有 strict Schema 仍決定輸出欄位，不以 verbosity 取代 Schema 驗證。 |
| DEC-P388 | 本批只授權程式、回歸測試、Dry Run 與文件更新，不授權付費模型呼叫。新設定是否改善 Live 完成率、P95、Token 與成本，必須另行授權一輪固定案例 Smoke 後判定；通過前不得啟動 66 次 Release baseline。 |

## Lowest-Cost Analysis

1. 不變更設定：既有 v4 Smoke 的商品案例只有 1／4 在 5 秒內完成，不能提供足夠的 AI-09 Live Gate 證據，未採用。
2. 只調整文件或操作方式：無法改變模型推理時間，未採用。
3. 延長 Timeout 或改寫 5 秒門檻：會覆寫已定版的體驗與降級邊界，且不先利用現有低延遲設定，未採用。
4. 重用現有 Responses payload，加入官方既有的 `reasoning.effort: none` 與 `text.verbosity: low`：不新增依賴或契約，為第一個能直接針對延遲且完整滿足安全邊界的方案，採用。
5. 更換模型、購買優先 service tier 或加入新服務：會增加品質比較、費率或維運成本，須在低成本設定實測仍不足後再決策，未採用。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | 使用 AI 商品搜尋的訪客／會員，以及 AI-09 評估與展示人員 |
| 現況損失／風險 | v4 Smoke 商品案例 3／4 逾時並安全降級，AI 商業亮點無法穩定呈現 |
| 觸及範圍／頻率 | 每次 AI 商品 SearchIntent；正式流量尚無證據 |
| 預期可量測結果 | 保持單次呼叫與 5 秒邊界，下一輪固定 Smoke 比較 5 秒內完成率、P95、Token 與成本 |
| 建置／持續成本 | 既有 payload 的 bounded change 與回歸測試；無新套件、服務、Migration 或固定費用 |
| 預期風險成本 | 降低推理量可能降低複雜意圖品質；strict Schema、後端白名單／驗證與關鍵字降級維持 Fail Closed |
| 信心 | 設定已由官方 API 支援且 payload contract 可自動驗證，信心高；是否達成 Live P95，證據不足 |
| 成功指標 | payload contract tests、既有 focused tests、資料集驗證與 Dry Run 通過；另行授權 Smoke 達既定品質與延遲 Gate |
| 停止／回復條件 | 若新 Smoke 仍無法穩定在 5 秒內完成，或 Intent 品質退化，停止 baseline 並重新決策 Timeout、模型或 service tier；可移除兩個 payload 欄位回復 |

## 證據

- `FP.dev/evals/ai/v1/results/2026-09-04-v4-smoke-f195c453.md`
- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Ai/OpenAiProductSearchClientTests.cs`
- [GPT-5.6 Luna model](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
- [GPT-5 model guidance](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.6-luna)
- [Responses create API](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)

## 後續驗證結果

- 系統 PowerShell Run `20260904T144911Z` 依本批設定執行 6 案／1 輪，成本 US$0.007224。
- 商品 4／4 在 5 秒內取得 Provider 結果，P95 3,013 ms；低延遲方向達成該輪門檻。
- Schema 83.33%、Intent 25%、有效推薦 66.67%、Deterministic 50%，整體 Verdict 仍為 `FAIL`，不得進入 66 次 baseline。
- `SEARCH-NOVICE-026` 的「主機」taxonomy、正式人工覆核與安全診斷已由 DEC-BATCH-055 定版；`product-search-v5` 零成本修正已通過聚焦驗證，但尚未付費重驗。
- 詳見 `FP.dev/evals/ai/v1/results/2026-09-04-low-latency-smoke-f195c453.md`。
