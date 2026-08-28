# PR #51 Review 修正測試與驗證

- Run ID: `20260828T012750Z-pr51-review-repair`
- Stage: Test and verification
- Author/runtime: Claude Code (Sonnet 5)
- UTC time: 2026-08-28T02:10:00Z
- Input report paths: `01-requirements.md`, `02-functional-analysis.md`
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Base commit（最終 rebase 後）: `origin/dev@db7b17f`
- Head commit（最終）: `feature/support-supervise-des23@6b7d3ef`

## 驗證環境

- .NET SDK：固定使用 `10.0.303`（`D:\期末小組電商網站\.tools\dotnet-10.0.303`），符合 `global.json`。
- SQL Server：本機具名執行個體 `.\SQL2025`，Windows 驗證。
- 本輪 rebase 撿到 `dev` 新合併的訪客查單功能（PR #40）要求
  `GuestOrderAccess:Pepper`（≥32 UTF-8 bytes）於啟動時通過 `ValidateOnStart` 驗證，且倉庫內
  （含 `appsettings.json`／`appsettings.Development.example.json`）沒有任何安全預設值——這是該功能
  本身的既有缺口，與 DES-23 無關。為了讓測試能執行，僅以環境變數
  `GuestOrderAccess__Pepper=<本機測試用值>` 提供（未寫入任何已追蹤或會被提交的檔案）；另外在
  gitignored 的本機 `appsettings.Development.json` 補上同一個鍵，方便日常以 `start-all.ps1` 手動操作
  API。兩者皆不影響提交內容。

## 後端驗證（固定 SDK 10.0.303）

| 項目 | 結果 |
| --- | --- |
| `dotnet build DoSelect.slnx --no-restore -warnaserror` | 0 警告 / 0 錯誤 |
| `dotnet format DoSelect.slnx --verify-no-changes --no-restore` | 通過（無需變更） |
| `dotnet test` — Domain | **442 / 442** 通過 |
| `dotnet test` — Application | **362 / 362** 通過 |
| `dotnet test` — Infrastructure（含 SQL Server provider-backed） | **407 / 407** 通過 |
| `dotnet test` — API Integration（含 SQL Server provider-backed／HTTP policy acceptance） | **436 / 436** 通過 |
| 後端測試總計 | **1,647 / 1,647** 通過 |
| `dotnet ef migrations has-pending-model-changes` | 無待處理的模型變更 |
| `dotnet list package --vulnerable --include-transitive`（全部 8 個專案） | 無易受攻擊套件 |

## 前端驗證

### Admin Web

| 項目 | 結果 |
| --- | --- |
| `npm run typecheck`（`vue-tsc -b`） | 通過 |
| `npm run lint -- --max-warnings 0` | 0 警告 |
| `npm test -- --run`（vitest） | **153 / 153** 通過（26 個測試檔） |
| `npm run build`（production） | 成功 |
| `npm audit --omit=dev` | 0 個弱點 |

### Customer Web

| 項目 | 結果 |
| --- | --- |
| `npm run typecheck` | 通過（`npm ci` 後解決 rebase 新增的 `@playwright/test` 型別缺失，屬環境同步，非程式問題） |
| `npm run lint -- --max-warnings 0`（含 `frontend/shared`） | 0 警告 |
| `npm test -- --run`（vitest） | **70 / 70** 通過（18 個測試檔） |
| `npm run build`（production） | 成功（含既有 Return／登入／重設密碼頁面，確認 PR #42 功能保留） |
| `npm audit --omit=dev` | 0 個弱點 |

## OpenAPI 契約

- 啟動 API（`scripts/start-all.ps1`）後執行 `npm run api:export && npm run api:generate`
  （於 `frontend/customer-web`），對 `contracts/openapi.v1.json` 與
  `frontend/shared/src/api/generated/schema.d.ts` 重新匯出／產生。
- 冪等性確認：連續執行兩次匯出／產生，兩次輸出的 md5 雜湊完全相同。
- rebase 過程中這兩個產出物在合併時發生衝突（因 `dev` 新增訪客查單／Outbox／訂單運費快照等端點），
  以暫用版本解衝突後，於 rebase 完成、API 建置成功並啟動後，重新匯出／產生為最終版本並另立
  `chore(api)` commit（非 amend）。

## 全面自查（PR #51 完整 DES-23 功能，不限 review comment 七項）

1. **Action 矩陣**（Claim／Assign／Transfer／ChangePriority／ChangeStatus／Cancel／Reopen／
   Internal Note）：`SupportPolicyHttpAcceptanceTests`／`AdminSupportTicketSuperviseStoreTests`／
   `AdminSupportTicketServiceTests` 涵蓋，全數通過。
2. **角色矩陣**（CustomerService／CustomerServiceSupervisor／單一 SuperAdmin／多角色聯集／Member）：
   `PolicyMatrix`（`RegisteredPolicies_EnforceExactRoleMatrix`）與各動作的角色 InlineData 全數通過；
   確認 Claim 等日常 Handle 動作對單一 SuperAdmin 正確回 403，Detail／SLA／Workbench 對單一
   SuperAdmin 正確回 200。
3. **Detail／SLA Queue／Case Workbench 的 Actor Scope**：三者統一走 `Admin` policy +
   `CanHandle() || CanSupervise()` 程式判斷，測試涵蓋。
4. **409 全面刷新**：`queries.spec.ts` 對 Claim／Assign／Transfer 各自以
   `support_ticket_assignment_conflict` 與 `concurrency_conflict` 兩種 code 驗證三個 projection
   （detail／SLA queue／case workbench）皆被 invalidate。
5. **指派競爭與 RowVersion 競爭、輸家零副作用**：既有 `AdminSupportTicketSuperviseStoreTests`
   涵蓋（非本輪新增，rebase 後重跑仍全數通過）。
6. **Ticket／Assignment History／Status History／SLA Event／Audit 同交易**：
   `AdminSupportTicketStore.MutateAsync` 於單一 `SaveChangesAsync` 呼叫內寫入全部效果；新增的
   `ChangeStatusAsync_FromWaitingForCustomer_ResumesSlaAndCommitsAllEffectsAtomically` 與
   `ChangeStatusAsync_RejectsDedicatedActionEdges`（含 `Assigned` InlineData）以 SQL Server
   provider-backed 測試驗證。
7. **Reopen SLA 依優先度重算**：`AdminSupportTicketStore.ReopenAsync` 以
   `SupportSlaPolicy.GetTargets(ticket.Priority).Resolution` 即時計算目前優先度對應的到期時間，
   非沿用舊值。
8. **Internal Note 不得出現在會員端**：`frontend/customer-web/src` 全文搜尋
   `isInternal`／`internal-note`／`InternalNote` 均無任何引用；相關型別僅存在於 admin-only 的
   `schema.d.ts` 生成型別與 admin-web 程式碼中。
9. **Workbench 篩選／分頁／cursor fingerprint 與大小寫路由**：`CaseWorkbenchPage.vue` 的
   `filterFingerprint`／`cursorFilterFingerprint`（`flush: 'sync'`）與對應測試（keyword／status／
   assignee 三種第二頁篩選變更情境）全數通過；`normalizeCaseType` 大小寫正規化沿用先前修正。
10. **既有功能保留**：`git diff origin/dev...HEAD --name-only` 確認未觸及任何 Returns／登入／
    重設密碼／Migration 檔案；`App.vue`／`router/index.ts` 的差異純為新增 Case Workbench 導覽與
    路由，「退貨案件」既有連結與 `/returns` 路由未被移除；customer-web production build 成功產出
    `LoginPage`／`ResetPasswordPage`／`ReturnNewPage`／`ReturnDetailPage` 等既有頁面 bundle。
11. **無混入其他模組變更**：`git diff origin/dev...HEAD --stat` 共 40 個檔案，全部屬於客服工單／
    案件工作台／Admin UI／Audit 契約／測試／`.workflow` 日誌／OpenAPI 產出物，未見退款、庫存、
    Migration 或其他成員模組檔案。

## 指令與結果摘要

- `dotnet build DoSelect.slnx --no-restore -warnaserror`：exit 0。
- `dotnet format DoSelect.slnx --verify-no-changes --no-restore`：exit 0。
- `dotnet test DoSelect.slnx --no-build`（`GuestOrderAccess__Pepper` 環境變數提供）：exit 0，
  1,647 / 1,647 通過。
- `dotnet ef migrations has-pending-model-changes`：exit 0（無待處理變更）。
- `dotnet list package --vulnerable --include-transitive`：exit 0（無弱點）。
- Admin Web／Customer Web：`typecheck`／`lint -- --max-warnings 0`／`test -- --run`／`build`／
  `audit --omit=dev`：全部 exit 0。
- `npm run api:export && npm run api:generate`（`frontend/customer-web`）：exit 0；連續執行兩次
  雜湊一致。
- `git diff --check`：exit 0（無空白字元錯誤）。
- 衝突標記搜尋（`<<<<<<<`／`=======`／`>>>>>>>`）：無殘留。
- `git diff origin/dev...HEAD --stat`：40 檔，6,245 insertions / 98 deletions，範圍確認乾淨。

## 已知非本次範圍事項

- `dev` 分支新合併的訪客查單功能（PR #40）之 `GuestOrderAccess:Pepper` 啟動驗證缺乏任何環境的
  安全預設值，導致任何從該 commit 之後 rebase 的分支、在未額外設定本機密鑰的情況下，
  `dotnet test`／本機啟動 API 都會啟動失敗。這不屬於 DES-23／PR #51 範圍，未在本次改動；
  建議另外反映給該功能的負責人補上開發用預設值或文件說明。
- `SupportTicketDetailPage.spec.ts` 的工作副本存在純換行符號差異（CRLF/LF 混雜，`git diff`
  正規化後為 0 行語意差異）——已還原為 HEAD 版本，未提交，不影響本次改動。
