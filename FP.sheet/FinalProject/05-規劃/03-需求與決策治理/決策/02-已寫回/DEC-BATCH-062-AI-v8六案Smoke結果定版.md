---
batch_id: DEC-BATCH-062
status: applied
decision_date: 2026-09-06
decision_ids:
  - DEC-P405
---

# DEC-BATCH-062｜AI v8 六案 Smoke 結果定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P405 | Commit `45eeed27` 的 `product-search-v8 + support-v2` 固定六案 Smoke 已完成：6 次模型請求、成本 US$0.007117，未達 US$0.05 停止線；自動 Schema、Intent、補問、推薦、Citation、Privacy／Authorization、deterministic、延遲與成本門檻全部通過。Alex 已以顧客視角完成六案人工覆核並全部判定 Pass，因此本次小型 Smoke 正式結果為 `PASS`。本結果只核准進入 66 次 Release baseline 的下一項成本決策，不等於 AI-09 完成，也不授權執行 baseline。 |

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 商品搜尋／客服使用者、組長與發布審查者 |
| 現況改善 | v7 的 019、025、026 已知回歸在 v8 的 Provider-backed 結果與顧客視角覆核中均關閉 |
| 可量測結果 | 六案自動 Gate 100%，人工 6／6 Pass；商品／客服 P95 2,765／3,236 ms；成本 US$0.007117 |
| 建置與持續成本 | 本輪實際 6 次 Responses 請求；無新服務、套件、Schema 或固定成本 |
| 風險／回復 | 單輪小樣本無法代表完整集合與變異；若後續 baseline 失敗，停止發布 Gate 並回到案例／模型／Adapter 分類 |
| 信心 | 對固定六案修正信心高；對完整 36 案三輪仍不足 |

## 邊界與後續

- 歷史 v7 Smoke 保留 `FAIL`，不追溯改寫。
- Runner 原始 summary 保留自動階段 `PENDING_HUMAN_REVIEW`；正式報告合併 Alex 的人工 6／6 Pass 後得出 Smoke `PASS`。
- 66 次 Release baseline 尚未執行，成本停止線與執行授權須另案確認。
- AI-09 維持進行中；M-18／M-19 已合併狀態不變。

## 影響文件與追蹤

- `FP.dev/evals/ai/v1/results/2026-09-06-v8-smoke-45eeed27.md`
- AI Dataset README／Manifest、AI 詳細設計、AI 測試規格、測試策略、安全檢查表。
- AI-09 追蹤、M 功能矩陣、Alex 交付計畫、決策索引／紀錄與日誌。
