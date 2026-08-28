---
batch_id: DEC-BATCH-031
status: applied
decision_date: 2026-08-28
decision_ids:
  - DEC-P324
  - DEC-P325
  - DEC-P326
  - DEC-P327
---

# DEC-BATCH-031｜AI 客服同意、額度與 Owner Query 定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P324 | AI 客服同意使用 SQL Server `AiConsentRecords` append-only 保存；Grant／Withdraw 都建立新列，最新 `CreatedAtUtc`＋`Id` 決定目前狀態。保存 Member、PolicyVersion、Locale、Grant／Withdraw 時間與 Source，Member FK 採 Restrict。 |
| DEC-P325 | AI 客服每日上限固定 20 則、以 UTC 日界線計算。`AiUsageLedger` 以 RequestPublicId 全域唯一保證 replay 不重扣；每位會員的最後一額採 Serializable 交易與 SQL Server Key-range Lock，先完成同意／Owner／內容檢查，第一次模型呼叫前才預留。預留成功後即使用模型失敗也不退還。 |
| DEC-P326 | 訂單 Context Reader 只接受後端可信登入 Member ID 與最多三個 Order PublicId，只查本人訂單；外送內容限訂單 PublicId／編號／訂單、付款、履約狀態與商品／SKU／數量快照，不含姓名、Email、電話、地址或 Owner ID。真正 GuestOrderAccess Cookie 可完成 Authentication，但 AI Policy 因缺 Member Claim 固定回 403，且不得讀取 Admission／Context 或呼叫模型。 |
| DEC-P327 | AI-13 本批只完成 SQL-backed 同意／額度 Admission、Owner Query、真正 Guest Scheme 與 deterministic／Provider-backed 證據，功能旗標維持預設關閉。OpenAI Responses API Adapter 為下一階段；同意／撤回 UI、客服歷史 Query 與瀏覽器 E2E 歸 M-19；live 品質／成本證據歸 AI-09，不把這些後續項目混入 AI-13 關閉條件。 |

## Lowest-Cost Analysis

1. 保留 Fake／記憶體 Gate：無法證明重啟、多人併發、最後一額與正式 Owner Query，未採用。
2. 只以設定或單一計數欄保存額度：無法提供 RequestPublicId replay、使用證據與後續成本追蹤，未採用。
3. 延用單一 `DoSelectDbContext`，新增兩張 append-only 表及兩個既有介面的 EF 實作：不新增服務、套件或第二個 DbContext，即可滿足一致性、隱私與測試條件，採用。
4. 同批加入 OpenAI Adapter、前端同意與 E2E：跨越外部 Provider 與前端垂直切片，增加失敗面且不是本批安全資料層完成所必需，延後至既定後續階段。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用 AI 客服的登入會員、客服承接人、開發與展示人員 |
| 現況風險 | Fake Gate 無法證明正式同意、額度併發與本人訂單隔離；錯誤宣告完成會使個資或成本邊界沒有實證 |
| 預期可量測結果 | 未同意／撤回／超額／跨會員／Guest／資料庫故障時模型呼叫為 0；同會員最後一額併發只成功 1 筆；同 RequestPublicId replay 只有 1 筆 Ledger |
| 建置／持續成本 | 兩張表、兩個 EF Adapter、一支加法 Migration 與既有測試專案；無新依賴、服務或固定費用 |
| 風險成本 | 新程式若先於 Migration 發布會 Fail Closed 為 503；Migration Down 在產生同意／用量後會遺失稽核證據 |
| 信心 | 高；SQL Server Migration Chain、併發、冪等、Owner 隔離與完整 API 回歸已在可拋棄資料庫通過 |
| 成功指標 | Domain 4、Application 32、Infrastructure 6、API 10 AI focused 全綠；完整後端 1,793／1,793；EF Pending Model 為 0 |
| 停止／回復條件 | Migration 基線不符、出現既有表 Alter／Drop、最後一額重複成功、Guest／跨會員可進模型即停止；功能旗標保持關閉並優先 roll-forward 修正 |

## 實作與 Migration Gate

- Migration：`20260828050333_AddAiSafetyConsentAndUsage`。
- `Up()` 只新增 `AiConsentRecords`、`AiUsageLedger`、三個 Index、五個 Check Constraint 與兩個 Restrict FK；不修改、搬移或刪除既有資料。
- `Down()` 會刪除兩張新表；未有正式資料時可結構回退，有資料後優先關閉功能並 roll-forward，避免刪除同意／用量證據。
- 完整 Migration Chain 只套用至唯一命名、完成後已刪除的驗證資料庫；共用 `DoSelectDb` 維持原狀。
- 本批 Commit `6523589` 已推送並建立 PR #57；尚待 Required CI／Review／Squash Merge，進 `dev` 狀態由 M 功能實作矩陣另行更新。

## 影響文件

- [[02-領域需求/90-驗收規格/AI搜尋與客服驗收規格]]
- [[03-架構/06-AI設計/AI應用詳細設計]]
- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
