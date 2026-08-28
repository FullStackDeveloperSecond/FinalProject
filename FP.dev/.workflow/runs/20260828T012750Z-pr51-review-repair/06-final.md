# PR #51 Review 修正最終交付摘要

- Run ID: `20260828T012750Z-pr51-review-repair`
- Stage: Output
- Author/runtime: Claude Code (Sonnet 5)
- UTC time: 2026-08-28T02:15:00Z
- Input report paths: `01-requirements.md`, `02-functional-analysis.md`, `04-test-and-verification.md`
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Base commit（最終）: `origin/dev@db7b17f`
- Head commit（最終）: `feature/support-supervise-des23@6b7d3ef`

## 交付內容

組長 review PR #51 提出的七項問題全部修正完成，並在此基礎上完成一次全面自查（涵蓋 PR #51 完整
DES-23 功能，而非僅限 review comment 提及的七項），最終 rebase 到最新 `origin/dev`（含訪客查單、
Outbox、訂單運費快照等新合併功能）並重跑全套驗證。

1. 一般 change-status 動作禁止建立無承辦人的 `Assigned`，失敗零副作用；Admin UI 狀態選單同步移除。
2. `WaitingForCustomer → InProgress` 改走 `ResumeFromCustomerWait()`，同一交易寫入
   `PausedSeconds`／status history／`SupportSlaEvent.Resumed`／audit；SQL Server 無 Kind 日期
   修正為 UTC。
3. Detail／SLA Queue／Case Workbench 統一為 `Admin` policy + 程式判斷 `Handle OR Supervise`，
   單一 SuperAdmin 可讀取三者並執行 Assign／Transfer，但仍不得執行日常 Handle 動作。
4. Claim／Assign／Transfer 的任何 HTTP 409（不限 `support_ticket_assignment_conflict`）均刷新
   Detail／SLA Queue／Case Workbench 三個 projection。
5. Case Workbench 新增 filter fingerprint／cursor fingerprint，篩選條件變更時同步清除 cursor。
6. `ChangeSupportTicketPriorityRequest.Priority` 改為 `required`，省略時回 400；OpenAPI／
   TypeScript schema 已重新產生並確認冪等。
7. Case Workbench 本階段案件類型篩選僅顯示 Support；Returns（PR #42）導覽／路由／契約確認保留。

過程中額外發現並修正：`Claim` 動作的 `[Authorize]` policy 曾被中途誤鬆綁為裸 `Admin`（會讓沒有
客服角色的 SuperAdmin 也能自領工單），已還原為 `SupportTicketHandle`。

## 驗證

- 後端：`dotnet build -warnaserror` 0/0；`dotnet format --verify-no-changes` 通過；完整測試
  **1,647 / 1,647** 通過（Domain 442、Application 362、Infrastructure 407、API Integration 436，
  含 SQL Server provider-backed 測試）；無待處理 Migration；無易受攻擊套件。
- Admin Web：typecheck／lint（0 警告）通過，**153 / 153** 測試通過，production build 成功，
  0 個弱點。
- Customer Web：typecheck／lint（0 警告）通過，**70 / 70** 測試通過，production build 成功
  （含既有 Returns／登入／重設密碼頁面），0 個弱點。
- OpenAPI 契約：rebase 後以啟動中的 API 重新匯出／產生，連續兩次執行雜湊相同（冪等）。
- `git diff origin/dev...HEAD --stat`：40 個檔案，全數屬於 DES-23／PR #51 範圍（客服工單、案件
  工作台、Admin UI、Audit 契約、測試、`.workflow` 日誌、OpenAPI 產出物），未混入退款、庫存、
  Migration 或其他成員模組變更。

詳細數字與逐項自查見 `04-test-and-verification.md`。

## 已知風險／非本次範圍

- `dev` 新合併的訪客查單功能（`GuestOrderAccess:Pepper` 啟動驗證）在任何環境都缺乏安全預設值，
  導致從該 commit 之後 rebase 的分支若未額外設定本機密鑰，測試與本機啟動會失敗。此為該功能本身
  的既有缺口，不屬於 DES-23 範圍，未在本次改動；已於 `04-test-and-verification.md` 記錄，建議
  另行反映給該功能負責人。
- `SupportTicketDetailPage.spec.ts` 工作副本存在純換行符號差異（CRLF/LF 混雜，`git diff`
  正規化後 0 行語意差異），已還原為 HEAD 版本、未提交，符合「不提交純格式變更」的要求。
- `.localdata/` 全程僅為 untracked 目錄，未被讀取、修改、加入索引或提交。
