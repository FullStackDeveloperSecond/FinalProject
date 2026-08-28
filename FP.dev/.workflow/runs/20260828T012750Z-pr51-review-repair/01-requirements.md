# PR #51 Review 修正需求

- Run ID: `20260828T012750Z-pr51-review-repair`
- Stage: Requirements analysis
- Author/runtime: Claude Code (Sonnet 5)
- UTC time: 2026-08-28T01:27:50Z
- Input report paths: `.workflow/runs/20260827T071811Z-des23-support-supervise/*`（Codex 交接內容，終端中斷前的最後快照）
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Base commit（接手時）: `origin/dev@f461187`（後續 rebase 到 `origin/dev@db7b17f`，見 06-final.md）
- Working-tree summary: 接手時工作樹已有 Codex 未提交的 WIP（涵蓋七項修正中的六項半），Claude Code 在此基礎上完成剩餘修正與全面驗證

## 背景

組長 review PR #51（DES-23 客服監督操作）後提出七項必須修正的問題。Codex 已在
`support-des23-worktree` 工作樹中開始修正，但終端於完成前中斷，且無法重新啟動確認即時狀態。
使用者將 Codex 整理的交接內容轉交給 Claude Code，要求：先重新核對 Git 狀態（不可假設交接內容完整）、
接手剩餘修正、完成全面自查與完整驗證、最後以 rebase（禁止 merge）同步最新 `origin/dev` 並推送更新 PR #51。

## 七項修正目標

1. 一般 change-status 動作禁止將目標狀態設為 `Assigned`（僅能透過 Claim／Assign 進入），失敗時零副作用。
2. `WaitingForCustomer → InProgress` 走 `ResumeFromCustomerWait()`，同一交易寫入 Ticket 狀態／`PausedSeconds`／
   status history／`SupportSlaEvent.Resumed`／audit；SQL Server 無 Kind 日期轉為 UTC。
3. 單一 SuperAdmin 可讀取 Detail／SLA Queue／Case Workbench 並執行 Assign／Transfer（Supervise 範圍），
   但不得執行日常 Handle 動作（Claim／ChangeStatus／InternalNote／Cancel／Reopen）；ChangePriority 為
   Handle 或 Supervise 皆可。
4. Claim／Assign／Transfer 任何 HTTP 409（不限 `support_ticket_assignment_conflict`）都需刷新
   Detail／SLA Queue／Case Workbench 三個 projection。
5. Case Workbench 第二頁修改篩選條件時，必須同步清除 cursor（filter fingerprint／cursor fingerprint），
   不得帶著舊 cursor 發出新查詢。
6. `ChangeSupportTicketPriorityRequest.Priority` 省略時必須回 400 `validation_failed`，服務不得被呼叫、
   DB 不得異動；OpenAPI／TypeScript schema 需同步重新產生。
7. Case Workbench 本階段案件類型篩選僅顯示 Support；Return／Report 尚未取得授權 scope 與明細路由前
   不得呈現為可選項；PR #42 已合併的 Returns 導覽／路由／契約不得在 rebase 或衝突解決中遺失。

## 驗收條件

- 全面自查涵蓋 PR #51 完整 DES-23 功能（不只 review comment 提及的七項），包含角色矩陣、Actor Scope、
  409 全面刷新、指派競爭零副作用、同交易寫入、Reopen SLA 重算、Internal Note 隔離、Workbench 篩選／
  分頁、既有功能（Returns、登入／重設密碼、路由、OpenAPI 契約）保留。
- 固定 .NET SDK 10.0.303；後端建置（`-warnaserror`）、格式化、完整測試（Domain／Application／
  Infrastructure／API Integration，含 SQL Server provider-backed）、Migration 檢查、NuGet 弱點檢查全部通過。
- Admin Web／Customer Web 的 typecheck／lint（0 警告）／test／build／audit 全部通過。
- OpenAPI 匯出／產生／一致性檢查通過。
- `.localdata/` 全程不得讀取、修改、加入索引或提交。
- 最終以 `git push --force-with-lease` 推送，禁止裸 `--force`；禁止 `git reset --hard`／
  `git checkout -- .` 等破壞性清除指令；不得覆蓋他人修改。

## 限制

- 只能使用 rebase 同步 `dev`，不可 merge。
- 確認完整功能、測試及最新 `dev` 相容性前不得 push。
- 不建立重複 PR，只更新既有 PR #51。
