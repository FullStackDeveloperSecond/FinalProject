# PR #42 管理員退貨明細 E2E 修正驗證

- Run ID: `20260827T063338Z-pr42-admin-return-detail-e2e`
- Stage: test-and-verification
- Author/runtime: Codex / Windows PowerShell / .NET SDK 10.0.303
- UTC time: `2026-08-27T06:33:38Z`
- Input reports: PR #42 reviewer comment at head `853b45d`; existing M-12 workflow reports
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree\FP.dev`
- Base commit: `origin/dev@79f246caeee5f692cbbecc78718b8e76e67a4dd8`
- Working-tree summary: Returns query fix, two SQL Server-backed HTTP regression tests, regenerated OpenAPI artifacts

## Objective

修正 `ReturnStore.ListInspectionsAsync` 在 SQL Server 無法翻譯「先投影 DTO 再排序」所造成的管理員退貨明細 HTTP 500，並在 rebase 最新 dev 後完成全範圍驗證。

## Evidence

- 查詢改為先以 `ReturnInspection.InspectedAtUtc`、`ReturnInspection.Id` 排序，再投影 `ReturnInspectionDto`。
- `AdminReturnDetailHttpTests.GetDetail_NewReturnWithoutInspections_Returns200WithEmptyInspections`：真實 HTTP + SQL Server，驗證新案件回 200 且 inspections 為空。
- `AdminReturnDetailHttpTests.GetDetail_MultipleInspections_Returns200OrderedByTimestampThenIdentity`：真實 HTTP + SQL Server，驗證多筆資料依時間與 Id 穩定排序並回 200。
- 完整後端測試：Domain 390、Application 283、Infrastructure 351、API Integration 358，共 1382/1382 通過。
- Admin Web：typecheck、lint 0 warnings、129/129 tests、production build、production audit 0 vulnerabilities。
- Customer Web：typecheck、lint 0 warnings、70/70 tests、production build、production audit 0 vulnerabilities。
- EF Core：無 pending model changes；C# format verify 通過。

## Decisions

- Rebase 中舊 OpenAPI 生成檔衝突不人工拼接；保留最新 dev 版本，待所有 commits 重放後由 live API 重新 export/generate。
- 不修改退款執行、庫存回補、發票、付款或其他成員功能；本修正只改 Returns inspection 讀取順序及回歸測試。

## Acceptance-criteria mapping

1. 無 inspection 的新案件可載入：通過。
2. 多筆 inspection 依時間、Id 穩定排序：通過。
3. `GET /api/v1/admin/returns/{id}` 經真實授權與 HTTP pipeline 回 200：通過。
4. 最新 dev rebase、無 merge commit、無 conflict marker：通過。
5. M-12 狀態機、API/DB 契約、SQL Server、雙前端未回歸：完整自動測試通過。

## Commands and exit codes

- `dotnet restore DoSelect.slnx` → 0
- `dotnet build DoSelect.slnx --no-restore -warnaserror` → 0（0 warnings / 0 errors）
- focused `AdminReturnDetailHttpTests` → 2/2
- `dotnet format DoSelect.slnx --verify-no-changes --no-restore` → 0
- `dotnet test DoSelect.slnx --no-build --no-restore` → 1382/1382
- `dotnet ef migrations has-pending-model-changes ... --no-build` → 0
- Admin/Customer Web typecheck, lint, test, build, audit → 0

## Risks and unresolved items

- 統一 Scheduler 尚未提供，因此逾期未寄回取消仍只保留冪等 use case，不宣稱自動排程已完整交付。
- Guest Order Cookie 待 C-17 正式實作合併後依既有裁定對齊；本次未新增 Cookie 機制。

## Next-stage instructions

提交本次修正與重新產生契約；再次 fetch/rebase 最新 dev；執行 `api:check`、diff/self-review；以 `--force-with-lease` 更新已 rebase 的 PR #42 分支，並更新 PR 驗證資訊後通知 reviewer。
