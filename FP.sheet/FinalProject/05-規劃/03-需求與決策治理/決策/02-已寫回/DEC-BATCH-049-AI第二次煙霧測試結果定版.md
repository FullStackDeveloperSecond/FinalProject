---
batch_id: DEC-BATCH-049
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P365
---

# DEC-BATCH-049｜AI 第二次煙霧測試結果定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P365 | 在可追溯 Commit `9ea03fc3`、資料集 `zh-TW-v1.0.2-draft` 上執行第二次雙案例 Live 煙霧測試；2／2 deterministic 與人工覆核均通過，成本 US$0.006085、3,545 input／694 output tokens，未達 US$0.10 停止線。此結果只證明修正版路徑可運作，不是完整 Release baseline；商品搜尋單筆 10,083 ms 高於 5 秒目標，須保留為正式 baseline 的待確認風險，不得以單筆樣本宣稱 P95。 |

## Lowest-Cost Analysis

1. 不執行修正版煙霧測試：無法確認 strict Schema 與完整政策 Fixture 是否已排除初次失敗，未採用。
2. 依已核准的兩案例、單輪與 US$0.10 停止線重跑：能以最低外部成本確認完整路徑，採用；實際成本 US$0.006085。
3. 直接執行 129 次正式 Release baseline：尚未取得獨立成本停止線授權，且煙霧測試結果尚未人工覆核，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 商品搜尋／客服使用者、Alex、專題展示評審 |
| 現況風險 | 初次煙霧測試失敗；修正版若未重驗，無法證明 Schema、政策引用與成本保存能真實運作 |
| 預期結果 | 以兩案例證明修正版端到端成功，並在完整 baseline 前提早暴露商品搜尋延遲訊號 |
| 建置／持續成本 | 本次 US$0.006085；完整 baseline 仍需另外授權與人工審查 |
| 信心 | 修正版路徑高；P95 與整體品質低至中，因本次每功能僅一筆樣本 |
| 成功指標 | 2／2 deterministic 與人工通過、引用 100%、成本可追溯、無個資或 Secret 外洩 |
| 停止／回復條件 | 任一 Hard Fail、成本達停止線、引用未受支持或完整 baseline 顯示品質／延遲不達標時停止發布宣稱並修正 |

## 證據與後續 Gate

- `FP.dev/evals/ai/v1/results/2026-09-04-smoke-9ea03fc3.md`
- Run ID：`20260904T065223Z`
- 正式 Release baseline：24 個商品搜尋案例、9 個客服案例，三輪共 129 次模型請求；尚未授權。
