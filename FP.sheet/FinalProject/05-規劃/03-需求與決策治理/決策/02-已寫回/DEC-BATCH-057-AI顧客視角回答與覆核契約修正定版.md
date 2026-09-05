---
batch_id: DEC-BATCH-057
status: applied
decision_date: 2026-09-05
decision_ids:
  - DEC-P397
---

# DEC-BATCH-057｜AI 顧客視角回答與覆核契約修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P397 | AI 商品搜尋與客服的回答對象是顧客／實際 AI 使用者，不是內部開發或維運人員。SearchIntent Enum、Semantic Key、Fixture ID、商品／分類／品牌內部代碼及後端驗證用語只供系統內部判斷，不得直接出現在顧客可見回答；回答須直接承接顧客問題，以自然語言說明用途、預算、硬性規格、軟性偏好、推薦理由或補問。人工覆核表每案必須同時列出顧客原問題、必要回答重點與顧客可見回答。 |

## 最低成本檢查

沿用現有單次 SearchIntent 模型呼叫、後端確定性理由組裝器與 Live Runner 即可完成；不需要第二次模型呼叫、公開 DTO／資料庫變更、新套件或新服務。

## 影響與驗證

- 顧客可見理由使用在地化用途、分類、規格與偏好文字；測試 Fixture 缺少商品名稱時使用合成顯示名稱，不再回傳候選 ID。
- Fixture 升為 `v1.0.4`，依既有已核准 `SEARCH-NOVICE-019` 必答點補齊 8TB 儲存裝置名稱、容量與「單一裝置不等同完整備份」Badge；不變更該案例預期。
- Live Runner 對顧客輸出加入內部識別碼／後端術語的確定性檢查，失敗碼為 `CUSTOMER_FACING_OUTPUT_INVALID`。
- Grader 升為 `deterministic-v1.1.2`，新增顧客視角 hard-fail 與人工品質檢查。
- v5 六案歷史輸出不因修正而改寫，正式人工結果仍全部 `pending`；新的 v6 顧客可見輸出須重新覆核，任何付費 Smoke 仍需另行授權。

## 影響文件與追蹤

- AI 應用詳細設計、AI 測試與評估規格、AI 評估 README／Grader／Manifest／v5 報告。
- AI-09 追蹤列、M 功能矩陣、Alex 個人交付計畫、決策索引／紀錄與本次日誌。
