---
batch_id: DEC-BATCH-052
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P378
  - DEC-P379
  - DEC-P380
---

# DEC-BATCH-052｜AI 創作者 Fixture v1.0.3 定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P378 | 組長授權依此階段繼續，`workstation-3d-70` 採用最小合成事實：顯示名稱「懂選 3D 創作者工作站」、`GPU 預算優先`與 `64GB RAM`；原價格 NT$70,000、用途與候選 ID 不變。 |
| DEC-P379 | 資料集升為 `zh-TW-v1.0.3-draft`、Fixture 升為 `v1.0.3`；Live Runner 只原樣映射選用的 Name／Badges 到既有 `ProductCardDto`，不新增 API、Schema、依賴或產品功能。 |
| DEC-P380 | Fixture 變更同時影響 `SEARCH-CREATOR-008`、`013`；兩案先改為 `pending`，依正式責任由 Terry 主標、Alex 第二審。覆核完成前完整 Release plan 必須 `IsLiveReady=false`；本決策不授權付費重跑，也不包含商品兩階段延遲方向。 |

## Lowest-Cost Analysis

1. 不修正：無法支持已定的 GPU／RAM／預算取捨必答點，未採用。
2. 只改文件或放寬期待：會隱藏 Fixture 缺口，未採用。
3. 擴充既有合成 Fixture 與 Runner 映射：不改公開契約或產品資料，可完整支持必答點，採用。

## 影響與停止線

- 受影響者：AI-09 評估者、專業創作者案例的人工覆核者。
- 預期結果：模型可以引用明確候選事實解釋 GPU、RAM 與預算取捨，不需虛構型號或 Benchmark。
- 建置／持續成本：只修改合成資料、映射、測試與版本紀錄；無新依賴與外部費用。
- 成功指標：資料產物一致、deterministic validator 通過、Runner 映射測試通過；人工覆核與付費模型結果分開追蹤。
- 停止線：未完成 Terry／Alex 覆核前不得完整付費 Release；商品 5 秒延遲方向必須另行決策。

## 證據

- `FP.dev/evals/ai/v1/reviews/SEARCH-CREATOR-v1.0.3-review.md`
- `FP.dev/evals/ai/v1/results/2026-09-04-remediation-smoke-f195c453.md`
