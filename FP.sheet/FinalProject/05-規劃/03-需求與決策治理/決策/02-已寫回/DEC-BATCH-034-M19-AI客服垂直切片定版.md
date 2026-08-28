---
batch_id: DEC-BATCH-034
status: applied
decision_date: 2026-08-28
decision_ids:
  - DEC-P335
  - DEC-P336
  - DEC-P337
  - DEC-P338
  - DEC-P339
---

# DEC-BATCH-034｜M-19 AI 客服垂直切片定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P335 | 新增 `GET /api/v1/ai/consents/current`，回傳當前登入會員的有效同意狀態、後端目前 Policy Version、Locale 與決定時間，讓前台重新整理後仍可由伺服器狀態正確呈現；Grant／Withdraw 維持 append-only。 |
| DEC-P336 | `AiSupportMessageRequest` 新增 `referencedSupportTicketPublicIds:uuid[0..3]`。後端只投影當前會員本人案件的公開訊息，排除 Internal Note、附件、Sender 身分與個資；Conversation、Order、SupportTicket 都必須在模型呼叫與額度預留前完成 Owner 驗證。 |
| DEC-P337 | AI 客服價格不硬編，改由 `OpenAI:SupportInputCostPerMillionTokens` 與 `OpenAI:SupportOutputCostPerMillionTokens` 設定；AI 啟用時兩者必填且不得為負。每次 Interaction 使用 Provider 實際 Input／Output Token 計算並保存成本；累計 US$70／90 警告與非 Demo 停用規則維持，Demo Member PublicId 由 `OpenAI:DemoMemberPublicIds` 設定且最多兩筆。 |
| DEC-P338 | A-28 AI 用量頁採最小權限：`SuperAdmin`、`FinanceManager`、`CustomerServiceSupervisor`、`MarketingAnalyst` 可進入；只有 Finance／SuperAdmin 可查看成本金額。`CustomerService` 與 `SecurityAdmin` 不開放，較舊權限矩陣同步收束。 |
| DEC-P339 | `OpenAI:BudgetAlertRecipientAdminPublicId` 指定唯一組長帳號，AI 啟用時必須提供非空的 `AspNetUsers.PublicId`；執行時再確認帳號為 Active Admin、AdminProfile 有效且持有 `SuperAdmin`。累計估算成本首次由低於 US$70 跨越門檻時，在保存 Interaction 的同一交易寫入 Email 與站內通知 Outbox；後續互動不得重複送出。設定或角色不合法時，在模型呼叫前 Fail Closed。 |

## Lowest-Cost Analysis

1. 只保留記憶體 UI 狀態：重新整理會遺失同意與對話識別，不能證明 append-only 同意與後端授權，未採用。
2. 只更新文件或讓前端傳完整客服對話：不能保護 Internal Note、附件與跨會員資料，未採用。
3. 重用既有 Member Policy、AI Admission、SupportTicket Schema、Responses Adapter、OpenAPI Client 與 SQL Server，新增小型查詢／保存投影及三張既定 AI 表：可完成同意、Owner 脈絡、互動保存、成本與前後台 UI，採用。
4. 新增向量資料庫、Provider 對話狀態、第二套 AI SDK 或即時串流：M-19 第一版沒有需要，增加維運與隱私面，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用 AI 客服的會員、人工客服、客服主管、行銷分析、財務與 SuperAdmin |
| 現況風險 | 同意狀態無法在 Reload 還原；客服歷史若直接外送可能洩漏 Internal Note／個資；Token 有紀錄但缺版本化價格，無法執行成本保護 |
| 預期可量測結果 | 跨會員／Internal Note 外送為 0；每次成功或降級 Interaction 保存模型、Token、成本與狀態；每日 20 則及 US$90 非 Demo 保護生效；AI 停用仍可轉人工 |
| 建置／持續成本 | 重用既有基礎，新增 API、EF 投影、Migration、兩套 Vue 頁面及測試；無新增固定服務，只有啟用後的 OpenAI 實際用量 |
| 風險成本 | 真實品質、P95 與單次成本仍需 AI-09 live baseline；組長帳號停用或撤銷 SuperAdmin 時，必須先更新成本通知設定，否則 AI 客服會安全停用 |
| 信心 | 高（授權、同意、額度、Migration、OpenAPI、元件與 API deterministic 證據）；中（真實 Provider 與完整 E2E 待 CI／live baseline） |
| 成功指標 | 合約、Application、API、前端與 Migration Gate 通過；SQL Provider-backed 與 Playwright 在 Required CI 通過；live baseline 達既定品質／P95／成本門檻 |
| 停止／回復條件 | 任一跨會員／Internal Note 外送、成本失真、撤回後仍可呼叫、Migration 破壞既有資料或 E2E 無法降級即停止；回復為 `Features:AiEnabled=false` |

## 影響文件

- [[02-領域需求/90-驗收規格/AI搜尋與客服驗收規格]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]
- [[03-架構/04-安全與檔案/設定與Secrets管理規範]]
- [[03-架構/06-AI設計/AI應用詳細設計]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
