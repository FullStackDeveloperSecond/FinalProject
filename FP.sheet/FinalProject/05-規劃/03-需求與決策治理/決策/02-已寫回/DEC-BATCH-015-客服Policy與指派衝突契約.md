---
type: decision-record
batch_id: DEC-BATCH-015
title: 客服 Policy 與指派衝突契約
status: applied
created_at: 2026-08-20
submitted_at: 2026-08-20
applied_at: 2026-08-20
decision_count: 2
decision_range: DEC-P281～DEC-P282
source: alex 選擇 1-A、2-A
---

# DEC-BATCH-015｜客服 Policy 與指派衝突契約

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P281 | 建立兩個正式客服 Policy。`SupportTicket.Handle` 允許 `CustomerService`、`CustomerServiceSupervisor`，涵蓋公開回覆、內部備註、claim、一般優先級調整、合法狀態處理、取消與重開；`SupportTicket.Supervise` 允許 `CustomerServiceSupervisor`、`SuperAdmin`，涵蓋 assign、transfer 與優先級覆核／覆寫。`SuperAdmin` 單一角色不自動取得 Handle；需要日常處理時必須另授客服角色。多角色採允許權限聯集，所有操作仍須驗證狀態、RowVersion、理由、歷程與 Audit。 |
| DEC-P282 | `409 support_ticket_assignment_conflict` 只回既有 API 共通規範定義的標準 Problem Details（包含 `code`、`traceId`、`correlationId`），不附最新承辦人 PublicId／DisplayName。前端收到衝突後顯示提示，失效並重新查詢案件明細與所屬佇列，以最新 `SupportTicketDto` 的 Assignee、RowVersion、AvailableActions 更新畫面。 |

## 最低成本與商業影響

- 沿用現有 `CustomerService`、`CustomerServiceSupervisor`、`SuperAdmin`、共通 Problem Details 與 `SupportTicketDto`，只新增兩個具名 Policy 定義；不新增角色、資料表、套件或錯誤回應型別。
- 維持現況無法讓 Kafen 對每個客服 Action 實作一致授權，也會讓前端無法確定衝突後的資料恢復流程，因此不採用。
- 受影響者為客服人員、客服主管、SuperAdmin 與同時操作案件的後台使用者。主要風險是過度授權與畫面保留過期承辦資訊。
- 成功指標：每個 Action 有唯一可測的 Policy；僅 SuperAdmin 的帳號不能執行日常客服處理；指派衝突不洩漏額外承辦人資料，重新查詢後畫面與伺服器一致。
- 建置成本限於 Policy 註冊、Action 套用、前端 Query 失效／重查及測試；無持續服務成本。若實作造成既有客服流程阻塞，可回退 Policy 綁定與前端衝突 handler，文件決策另行覆寫。

## 寫回範圍

- [[01-需求/角色與權限]]
- [[02-領域需求/04-客服與售後/客服與AI功能]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/API錯誤碼目錄]]
- [[03-架構/08-測試與驗收/M功能測試案例目錄]]
- [[05-規劃/02-分工與交接/工程包/Kafen-客服退貨檢舉與RWD工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## 實作 Gate

- 本批只完成規格、API 契約與工作分派，不代表 Authorization Policy、Controller、前端 Query invalidation 或測試已完成。
- DES-23 完成前不得宣稱客服 Action 授權邊界與 409 衝突恢復流程已完成。
- 不需要 Schema 或 Migration；若實作時發現必須改動公開 Action 名稱或 DTO，須另提契約變更，不得自行擴張本決策。
