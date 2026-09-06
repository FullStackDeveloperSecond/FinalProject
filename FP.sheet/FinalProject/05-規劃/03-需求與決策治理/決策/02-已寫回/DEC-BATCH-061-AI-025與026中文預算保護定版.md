---
batch_id: DEC-BATCH-061
status: applied
decision_date: 2026-09-06
decision_ids:
  - DEC-P404
---

# DEC-BATCH-061｜AI 025 與 026 中文預算保護定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P404 | 採用既有商品 SearchIntent Adapter 內的繁體中文確定性預算保護，不把完整測試句硬編碼。單一且明確、沒有「至少／最低」語意的「三萬五」等口語金額視為最高預算；若最低預算高於最高預算，保留較安全的最高預算、將最低預算設為 null，並由後端產生預算衝突補問；「左右／上下／大約／大概／約莫／差不多」及「兩三萬」等模糊金額不強制覆寫模型輸出。只套用 `zh-TW`，Prompt 升為 `product-search-v8`；公開 API、DTO、資料庫、模型、逾時、重試、額度與降級契約不變。 |

## Lowest-Cost Analysis

1. 接受 v7 現況：025 會遺失明確上限並多問，026 會遺失安全上限，不能滿足已核准評估契約，未採用。
2. 只修文件、資料或人工覆核：不會改變執行時行為，未採用。
3. 只追加 Prompt：相同規則在 v6 曾通過、v7 又失敗，無法提供跨模型變異的確定性保證，未採用。
4. 延伸既有 Adapter 做窄範圍正規化：不新增套件、服務、Schema 或公開契約，可確定保存明確上限並對模糊輸入維持保守，採用。
5. 新增中文斷詞／金額解析服務：現有窄規則已足夠涵蓋核准案例，額外依賴與維護成本不必要，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用繁體中文自然語言商品搜尋的訪客與會員 |
| 現況風險 | 已說明預算的顧客仍被重複追問，或在衝突條件下失去安全上限，導致候選錯誤或流程中斷 |
| 可量測結果 | 025 保存 NT$35,000 且移除純預算補問；026 保存 NT$15,000、最低值清空並提出衝突補問；模糊金額不被強制覆寫 |
| 建置與維護成本 | 延伸既有 Adapter 與回歸測試；無外部呼叫、Schema、依賴或持續費用 |
| 風險／回復 | 規則只限 `zh-TW` 且模糊表達 Fail Closed；可回復 `product-search-v7` 與移除單一 Guard |
| 信心 | 對 025／026 deterministic 契約信心高；模型整體品質仍須新付費 Smoke 證明 |

## 驗證與邊界

- RED：模擬模型把 025／026 的 `budget` 回成 null；明確上限與衝突上限兩案失敗，模糊金額保守案例原本即通過。
- GREEN：`OpenAiProductSearchClientTests` 20／20、`LiveEvaluationPlanTests` 23／23 通過；涵蓋明確上限、衝突安全上限、模糊負例、非 `zh-TW` 隔離，且 025 的正式評估路徑以模型漏預算並錯問預算的輸出重現後仍通過。
- Solution Build 為 0 warning／0 error，四個變更 C# 檔案通過 format check。
- 120 筆 Dataset 來源同步、Schema／分布／Split／隱私驗證通過；三輪 Release Dry Run 為 36 案、22 live、14 deterministic-only、66 次規劃請求，`IsLiveReady=true`。
- 沒有呼叫 OpenAI、沒有 Token 或費用；不得據此宣稱 v8 Live Smoke 或 Release baseline 通過。
- 歷史 v7 Smoke 保持原始 `FAIL`；下一 Gate 是另行授權的新六案付費 Smoke。

## 影響文件與追蹤

- `FP.dev/src/backend/DoSelect.Infrastructure/Ai` 的商品搜尋 Adapter 與中文預算 Guard。
- Infrastructure Adapter／Live Evaluation 回歸測試、AI manifest／README 與零成本修正報告。
- AI 詳細設計、AI 測試規格、測試策略、安全檢查表、M 功能矩陣、AI-09 追蹤、Alex 交付計畫、決策索引／紀錄與日誌。
