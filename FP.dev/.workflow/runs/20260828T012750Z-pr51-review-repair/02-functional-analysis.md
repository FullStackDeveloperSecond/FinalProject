# PR #51 Review 修正功能分析

- Run ID: `20260828T012750Z-pr51-review-repair`
- Stage: Functional analysis
- Author/runtime: Claude Code (Sonnet 5)
- UTC time: 2026-08-28T01:27:50Z
- Input report paths: `01-requirements.md`
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Working-tree summary: 逐檔比對 Codex 交接內容與實際 `git diff` 內容，確認七項修正的落地狀態

## 接手前置檢查結果

- 分支 `feature/support-supervise-des23`、無未完成 merge/rebase、`.localdata/` 僅為 untracked。
- `ahead 5, behind 3`（相對 `origin/feature/support-supervise-des23`）純粹是 Claude Code 先前已在本機
  完成、尚未推送的 rebase（架到 `origin/dev@f461187`），非遺失 commit。
- 未提交修改共 18 個檔案（含 2 個產出物 `contracts/openapi.v1.json`／`schema.d.ts`），逐檔比對後與
  交接內容七項描述一致；`SupportTicketDetailPage.spec.ts` 確認為 Codex 所疑的純換行差異
  （工作副本 CRLF/LF 混雜、內容雜湊與 HEAD 不同，但 `git diff` 正規化後為 0 行）——不予 stage。

## 逐項落地確認

1. **禁止 change-status → Assigned**：`AdminSupportTicketStore.ChangeStatusAsync` 的守門判斷式已加入
   `command.TargetStatus == SupportTicketStatus.Assigned`；Admin UI（`SupportTicketDetailPage.vue`）的
   一般狀態選單已移除 `'assigned'`；SQL Server regression test 新增
   `ChangeStatusAsync_RejectsDedicatedActionEdges` 的 `Assigned` InlineData，斷言 Ticket 維持 Open、
   `AssigneeAdminUserId` 為 null、無 status history、無 audit。
2. **WaitingForCustomer → InProgress 恢復 SLA**：改走 `ticket.ResumeFromCustomerWait()`，同一次
   `MutateAsync` 交易內寫入 status history 與新增的 `SupportSlaEvent`（type=Resumed）；`DueAtUtc` 以
   `DateTime.SpecifyKind(..., DateTimeKind.Utc)` 修正 SQL Server 回傳的無 Kind 日期；
   同一 UTC-Kind 修正也同步套用到 `SupportTicketService.cs`（會員端可能共用的恢復路徑）。新增 SQL
   regression test `ChangeStatusAsync_FromWaitingForCustomer_ResumesSlaAndCommitsAllEffectsAtomically`
   驗證 `PausedSeconds`、status history、SLA 事件時長／到期時間、audit 均存在。
3. **單一 SuperAdmin 完整 Supervise**：`GetDetail`／`GetSlaQueue`／`CaseWorkbenchController.Get` 三者
   統一改為 `[Authorize(Policy = Admin)]` 入口 + 程式判斷 `CanHandle() || CanSupervise()` 才放行；
   `IAdminSupportTicketService.GetDetailAsync` 新增 `canHandle` 參數；`ComputeAvailableActions` 依角色
   拆分：Claim／ChangeStatus／InternalNote／Cancel／Reopen 僅 `canHandle`；ChangePriority 為
   `canHandle || canSupervise`；Assign／Transfer 維持既有 `SupportTicketSupervise` policy 不變。
   （過程中發現 Codex WIP 曾誤將 `Claim` 動作的 policy 一併鬆綁為裸 `Admin`——已修正回
   `SupportTicketHandle`，確認與最初提交版本一致，屬於還原而非新變更。）
4. **409 全面刷新**：前端 `isSupportAssignmentConflict` 改名並廣義化為 `isSupportWriteConflict`，
   判斷任何 409（不限 `support_ticket_assignment_conflict` code），Claim／Assign／Transfer 的
   `onError` 均呼叫 `invalidateSupportProjections`（同時刷新 detail／SLA queue／case workbench）。
5. **Workbench cursor fingerprint**：`CaseWorkbenchPage.vue` 新增 `filterFingerprint`（涵蓋
   caseTypes／priorities／statusesInput／assigneePublicId／overdueOnly／keyword）與
   `cursorFilterFingerprint`，`currentCursor` 僅在兩者相符時才帶出 cursor；`watch(filterFingerprint,
   resetPagination, { flush: 'sync' })` 確保同一輸入事件內即清除，不會與舊 cursor 配對。
6. **ChangePriority 必填**：`ChangeSupportTicketPriorityRequest.Priority` 由 `[Required] CasePriority`
   改為 `required CasePriority`（C# required member，JSON 反序列化缺欄位即拋 400）。
7. **Workbench 僅顯示 Support**：`caseTypeOptions` 移除 Return／Report 選項，僅保留 Support；
   說明文字同步調整；PR #42 的 Returns 導覽（`App.vue` 的「退貨案件」連結）、路由
   （`/returns`、`ReturnNewPage`／`ReturnDetailPage`）與 OpenAPI 契約經比對均為純新增，未被移除。

## 已知非本次範圍但一併確認之項目

- `git diff origin/dev...HEAD`（含未提交修改，共 38 檔）僅涉及客服工單／案件工作台／Admin UI／
  Audit 契約／測試／日誌／OpenAPI 產出物，未混入退款、庫存、Migration 或其他成員模組變更。
- Internal Note 僅出現在 admin-only 端點與其生成型別中，`frontend/customer-web/src` 完全未引用
  `isInternal`／`internal-note` 相關內容。
- Reopen 的 `resolutionTarget` 由 `SupportSlaPolicy.GetTargets(ticket.Priority).Resolution` 依
  **當前**優先度即時計算，確認依優先度重算而非沿用舊值。

## 風險與待驗證

- `origin/dev` 在本輪修正期間又前進一個 commit（`f461187` → `db7b17f`，訪客查單功能），需在完成
  全套驗證後再 rebase 一次並重跑受影響測試（見 06-final.md）。
- OpenAPI 產出物需重新匯出／產生以反映本輪所有修正後的 API 介面（Codex 中斷前的版本可能已過期）。
