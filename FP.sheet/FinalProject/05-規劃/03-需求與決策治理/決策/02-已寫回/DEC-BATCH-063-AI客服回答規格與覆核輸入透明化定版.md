---
batch_id: DEC-BATCH-063
status: applied
decision_date: 2026-09-06
decision_ids:
  - DEC-P406
---

# DEC-BATCH-063｜AI 客服回答規格與覆核輸入透明化定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P406 | AI 客服不能只收到安全與引用限制，還必須收到不洩題的通用顧客回答規格。客服 Prompt 升為 `support-v3`：先直接回答顧客問題，再只補充核准資料支持且與問題相關的條件、期限、費用、例外與不確定性；需要操作時以顧客下一步收尾，並禁止內部代碼、Enum、資料庫欄位、Fixture ID 與實作用語。案例 `requiredPoints` 只供 Grader／人工覆核，絕不送給模型。人工覆核表同步列出 Prompt 版本、客服每案實際模型可用的核准來源與核准資料，並明示顧客問題不是完整模型輸入。商品搜尋維持 `product-search-v8`，因其模型只解析 SearchIntent；覆核表集中列出實際 Metadata，並逐案列出後端用來產生顧客回答的核准商品事實，清楚標示後者不是模型輸入。 |

## Lowest-Cost Analysis

1. 維持現況：模型無法穩定知道預期的顧客回答方式，覆核者也無法判斷答案是否超出來源，不採用。
2. 只修改人工評分或把每題 `requiredPoints` 送給模型：前者不改善正式回答，後者會洩漏答案並使評估失真，不採用。
3. 延伸既有 Prompt 與 Runner：不新增套件、服務、Schema 或 API，能同時補齊回答契約與覆核證據，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 客服顧客、人工覆核者與發布審查者 |
| 現況風險 | 技術安全規則完整，但回答結構未定義；隱藏評分重點與模型輸入不對稱，可能造成離題回答與不公平評分 |
| 可量測結果 | Prompt／覆核輸出契約測試通過；後續 Live 結果須重新確認顧客回答品質，不沿用 `support-v2` 結果 |
| 建置與持續成本 | 最小程式、測試與文件修改；無新依賴、資料表、服務或固定成本，Live 重驗費用須另行授權 |
| 風險／回復 | Prompt 變更可能改變措辭、Token 與延遲；若 Citation、安全、Schema 或品質回歸，可獨立回退至前一 Prompt 版本 |
| 信心 | 對輸入／評分契約缺口為高；對 Provider 實際品質改善仍待 Live 證據 |

## 驗證與邊界

- TDD RED：客服 Prompt 版本／回答結構與覆核來源欄位兩項聚焦測試均先按預期失敗。
- GREEN：Application AI 47／47、受影響的 Responses Adapter／Live Runner 34／34、Solution Build 0 warning／0 error及 focused Format 通過。
- 擴大 Infrastructure AI 執行另有 15 個 SQL Server 案例因本機 SSPI／加密連線環境失敗；與本次未觸及資料庫的修改無關，未宣稱通過。
- 沒有呼叫 OpenAI、沒有 Token 或費用；歷史 `support-v2` Smoke／Baseline 不追溯改寫，也不能證明 `support-v3` 的 Live 品質。

## 影響文件與追蹤

- `FP.dev/src/backend/DoSelect.Application/Ai/AiToolAndPromptPolicy.cs`
- `FP.dev/tools/DoSelect.AiEvals/LiveEvaluationRunner.cs`
- `FP.dev/evals/ai/v1/README.md`
- AI 應用詳細設計、AI 測試與評估規格、AI-09 追蹤、決策索引／紀錄與開發日誌。
