---
batch_id: DEC-BATCH-064
status: applied
decision_date: 2026-09-06
decision_ids:
  - DEC-P407
---

# DEC-BATCH-064｜AI v8 Baseline 五案零成本修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P407 | 依 `dev@155bafa3` 三輪 Release baseline 的原始逐輪證據修正商品五案：014 補齊 2TB SSD 精確規格、015 將自組清單期待改為 `CustomBuild`、020 改為單一主機板的既有 AM5 CPU 確認階段並精確評分 `proposedExistingParts` 與 Wi‑Fi 偏好、021 移除虛構用途並補齊 2TB／速度偏好、023 以通用 Prompt 規則辨識「遊戲滑鼠」等明確商品標籤用途。商品 Prompt 升為 `product-search-v9`，Dataset／Grader 升為 `zh-TW-v1.0.6-draft`／`deterministic-v1.1.5`；Fixture 維持 `v1.0.4`。另以 development 009 與 challenge 030 保護泛化，challenge 只供獨立觀測，不用於調整 Prompt。120 案與 72／36／12 分割不變。公開 API、Schema、資料庫、模型、逾時、重試、品質及成本門檻不變；後續付費 Smoke 必須另行列明範圍與取得授權。 |

## Lowest-Cost Analysis

1. 維持現況：五案會持續壓低 Intent、補問與有效推薦指標，且 020 的評分階段與正式流程不一致，不採用。
2. 只放寬評分門檻：會掩蓋資料期待錯誤與顧客語意缺口，不能滿足可重複評估，不採用。
3. 延伸既有 Prompt、Dataset 與 Runner：不新增依賴、API、Schema 或服務，即可修正根因並保留版本追溯，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用自然語言找商品的電腦新手、AI 品質覆核者與發布決策者 |
| 現況風險 | 明確的遊戲商品、單品採購與既有零件情境可能被錯分或多問，評估資料本身亦會產生假失敗 |
| 可量測結果 | 五案契約與泛化案例通過；Dataset 120 筆同步；後續 Live Smoke 檢查五案與 `support-v3` 顧客輸出 |
| 建置與持續成本 | 最小程式、資料與文件修改；無新固定成本。Provider-backed 驗證另計並需事前授權 |
| 風險／回復 | Prompt 可能影響未抽樣語句；若小型 Smoke 的品質、安全、延遲或成本回歸，可回退 `product-search-v8` 與對應 Dataset／Grader 版本 |
| 信心 | 對五案根因與零成本契約修正為高；對真實模型泛化效果仍待 Live 證據 |

## 驗證與邊界

- TDD RED 已證明原 Prompt 與 Runner 缺少通用規則及既有零件提案評分。
- GREEN：聚焦 7／7、Application AI 150／150、受影響 Infrastructure 45／45。
- Dataset 120 筆來源同步、Schema 與隱私驗證通過；Release 三輪 Dry Run 規劃 66 次且 `IsLiveReady=true`。
- 本輪未呼叫 OpenAI，Token 與費用為 0；不能取代 `product-search-v9 + support-v3` 的 Live Smoke 或完整 baseline。

## 影響文件與追蹤

- `FP.dev/src/backend/DoSelect.Infrastructure/Ai/OpenAiProductSearchClient.cs`
- `FP.dev/tools/DoSelect.AiEvals/LiveEvaluationRunner.cs`
- `FP.dev/evals/ai/v1/*`
- AI 應用詳細設計、AI 測試與評估規格、AI-09 追蹤、決策索引／紀錄與開發日誌。
