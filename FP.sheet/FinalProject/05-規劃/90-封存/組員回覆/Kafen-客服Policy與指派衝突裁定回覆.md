# 給 Kafen｜客服 Policy 與指派衝突裁定回覆

Kafen 你好，兩點已定版，請依下列契約實作。

## 1. 客服 Action 的正式 Policy

採用兩個 Policy：

| Policy | 允許角色 | 適用操作 |
|---|---|---|
| `SupportTicket.Handle` | `CustomerService`、`CustomerServiceSupervisor` | 公開回覆、internal-notes、claim、一般 change-priority、change-status、cancel、reopen 與一般案件處理 |
| `SupportTicket.Supervise` | `CustomerServiceSupervisor`、`SuperAdmin` | assign、transfer、priority review／override |

補充邊界：

- `SuperAdmin` 單一角色不通過 `SupportTicket.Handle`。
- 若 SuperAdmin 帳號需要日常回覆、寫內部備註、自領或操作一般狀態，必須另外授予 `CustomerService` 或 `CustomerServiceSupervisor`。
- 多角色使用允許權限聯集。
- 兩個 Policy 都不能繞過案件狀態、Actor／承辦範圍、RowVersion、必填理由、歷程與 Audit。
- 一般客服調整優先級走 Handle；主管覆核／覆寫走 Supervise。沿用既有 `change-priority` Action，不自行新增 Action 名稱。

## 2. `409 support_ticket_assignment_conflict`

採 A：只回既有 API 共通規範定義的標準 Problem Details；沿用共通欄位與產生管線，包含 `code`、`traceId`、`correlationId`，不建立客服專用錯誤格式。

不要加入 `currentAssigneePublicId`、`currentAssigneeDisplayName` 或其他最新承辦人欄位。

前端收到此 409 後：

1. 顯示「案件已由其他客服領取或轉派」提示。
2. 失效該案件明細 Query。
3. 失效目前客服佇列／工作台 Query。
4. 重新查詢，使用最新 `SupportTicketDto` 的 Assignee、RowVersion、AvailableActions 更新畫面。

## 驗收與 PR 要求

- Handle：CS、CSS 成功；只有 SuperAdmin 的帳號被拒絕。
- Supervise：CSS、SuperAdmin 成功；只有 CS 的帳號被拒絕。
- 驗證一個帳號同時有 SuperAdmin＋客服角色時依權限聯集成功。
- 競爭自領／轉派固定回 409，Response 不含承辦人擴充欄位。
- 前端測試需證明收到 409 後會重新查詢案件明細及佇列，而不是沿用舊 RowVersion 或舊承辦資訊。

本次不需要 Schema、Migration 或新套件。正式紀錄為 `DEC-BATCH-015`（DEC-P281～DEC-P282），實作追蹤項目為 `DES-23`。
