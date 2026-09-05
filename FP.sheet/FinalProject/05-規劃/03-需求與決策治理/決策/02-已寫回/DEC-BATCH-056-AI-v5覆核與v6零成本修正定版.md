---
batch_id: DEC-BATCH-056
status: applied
decision_date: 2026-09-05
decision_ids:
  - DEC-P392
  - DEC-P393
  - DEC-P394
  - DEC-P395
  - DEC-P396
---

# DEC-BATCH-056｜AI v5 覆核與 v6 零成本修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P392 | 採 `1B`：Run `20260904T193855Z-v5-smoke-system` 的 Codex 初步內容判定不直接轉為正式人工結果；六案 Human Verdict 全部維持 `pending`，由 Alex 逐案重新覆核，不得預填或推測。 |
| DEC-P393 | 採 `2A`：`citations.required=false` 表示回答可省略 citation；只要模型有輸出 citation，每筆仍須位於案例允許來源。非法來源由既有 Adapter Fail Closed，runner 不得把未回答／遭拒絕的結果記為 grounded。Grader 升為 `deterministic-v1.1.1`。 |
| DEC-P394 | 採 `3A`：SearchIntent Semantic Key 格式統一為正式大寫契約，Regex 與 Catalog／Schema 一致；仍須通過精確 allowlist，不因格式修正放寬任意欄位。 |
| DEC-P395 | 採 `4A`：後端確定性推薦理由以核准候選的預算與至少兩個 Badge 明確說明取捨；不降低「須解釋推薦理由」的人工驗收標準，也不新增模型呼叫。 |
| DEC-P396 | 採 `5A`：商品 Prompt 升為 `product-search-v6`，先加入不複製 Release 案例文字／金額的泛化範例，補強一般整機衝突預算與用途＋預算分類；若新 Smoke 仍失敗，才另案評估 deterministic parser。本批不授權付費重跑。 |

## Lowest-Cost Analysis

1. 維持現況：019 會拒絕正式大寫 Key、016 的選填引用會被誤判，013 理由與 026 分類仍不能通過既定 Gate，未採用。
2. 只改文件／人工忽略失敗：不能修正 runner、validator 與實際回答品質，且會產生假綠燈，未採用。
3. 延伸既有 Regex、grader、確定性理由與 Prompt：不改公開 API、資料庫、依賴或成本設定，已完整涵蓋本輪已定位缺口，採用。
4. 直接新增 deterministic parser：會擴大分類程式與維護成本；尚未先驗證較小的泛化 Prompt 修正，因此延後。

## Business Impact

| 項目 | 內容 |
|---|---|
| 受影響者 | AI 商品搜尋訪客／會員、AI 客服評估者、Alex 人工覆核者 |
| 現況損失／風險 | 合法大寫規格被拒、選填引用誤傷安全回答、推薦理由未說明取捨、一般整機分類不穩定 |
| 觸及範圍／頻率 | 每次含規格的 AI SearchIntent、AI 評估引用判定及商品推薦理由；尚無正式流量資料 |
| 預期可量測結果 | Uppercase allowlist、選填 citation、非法 citation Fail Closed、v6 Prompt 與取捨理由的 deterministic tests 通過；下一輪 Smoke 再量測品質／P95／成本 |
| 建置／持續成本 | 小型既有程式與測試修正；無新套件、Migration、服務或固定費用 |
| 預期風險成本 | Prompt 泛化仍可能受模型變異影響；以固定六案 Smoke、三輪 baseline 與人工覆核控制 |
| 信心 | 契約與 runner 修正可由零成本測試證明，信心高；v6 Live 品質仍無證據 |
| 成功指標 | 聚焦與完整非 Provider 測試、Build、Format、120 筆驗證與 Release Dry Run 通過；另行授權 Smoke 才判定 Live Gate |
| 停止／回復條件 | 若需改公開 DTO／資料庫或 v6 Smoke 仍失敗，停止並重新決策 parser；Prompt 與 grader 可獨立回復 |

## 影響文件與追蹤

- `FP.dev/evals/ai/v1/manifest.json`、`grader-contract.v1.json`、`README.md` 與 v5 Smoke 報告。
- AI 應用／測試規格、AI-09 追蹤列、M 功能矩陣、Alex 交付計畫、決策索引與本次日誌。
- AI-09 維持進行中；六案正式人工覆核與任何付費 v6 Smoke 均未完成。
