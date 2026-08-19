---
文件狀態: 已寫回
最後更新: 2026-08-19
decision_type: direct
status: applied
applied_at: 2026-08-19
---

# AUTO-DEC-005｜初始 Migration 與案件工作台實作定版

## 背景

四位 Owner 的 Entity／Configuration 與跨模組 FK 已完成，初始 Migration 可開始產生；但 `vw_CaseWorkbench` 的 Return Priority、Title 與 RequesterDisplay 尚缺明確來源，不能以實作者猜測補入 SQL。

## 定案

| 項目 | 統一結果 |
|---|---|
| Return Priority | `ReturnRequests` 新增 Low／Normal／High／Urgent 四級 `Priority varchar(16)`，建立預設 `Normal`；後台經具名商業操作調整並更新 `UpdatedAtUtc` |
| 工作台 Title | 不投影 Subject／Description；Support 使用 Category，Return／Report 使用 ReasonCode，交由 API／前端依受控代碼本地化 |
| RequesterDisplay | Support／Report 固定 `會員`；Return 依 RequesterUserId 是否為 Null 顯示 `訪客`／`會員`，不輸出姓名、聯絡方式、Identity UserId 或短識別碼 |
| SLA | Support 首回前採 FirstResponseDue、首回後採 ResolutionDue 並加上最多 72 小時 WaitingForCustomer 暫停；終態為 Null／false。Return／Report 無 SLA，為 Null／false |
| Migration | 單一 `DoSelectDbContext` 初始 Migration 固定命名 `InitialCreate`，Migration Assembly 與 Startup Project 均為 `DoSelect.Infrastructure` |
| View 建立 | `vw_CaseWorkbench` 以 Migration 自訂 SQL 建立；因 SQL Server `CREATE VIEW` 批次限制，使用 `EXEC(N'CREATE VIEW ...')`，Down 必須先 `DROP VIEW IF EXISTS` 再移除來源表 |
| 套用邊界 | 本次只 Scaffold、靜態審查、產生 Review SQL 與測試；未授權 `database update`，不得建立或修改 `DoSelectDb` |

## 實作結果

- Migration：`20260819013357_InitialCreate`
- 建立：93 張應用／Identity 資料表、315 個索引與 `vw_CaseWorkbench`
- Snapshot：模型無 Pending Change
- Review SQL：`FP.dev/database-deploy/initial-create/InitialCreate.review.sql`
- Review SQL SHA-256：`CFC5D74F0907D7F3DDA7516332CEE80CD8ABA82A812B4D2514C7EA42959ABC5C`
- `Up` 無 Drop／Alter／Delete／UpdateData；`Down` 的 93 個 DropTable 只作未套用初始 Migration 的對稱回復。
- 尚未連線套用 Migration，`DoSelectDb` 仍不存在。

## 影響文件

- [[03-架構/統一案件工作台設計]]
- [[03-架構/資料字典-購物交易與售後]]
- [[03-架構/資料表實作交付/Kafen-客服售後與檢舉最終Schema]]
- [[03-架構/本機開發環境與版本基線]]
- [[00-專案概述/DoSelect完整系統規格書-v1.0]]
- [[05-規劃/未完成項目追蹤表]]

## 後續 Gate

> 2026-08-19 後續決策覆寫：Migration 套用、最小 Seed 與 SQL Readiness 已依 [[05-規劃/決策/02-已寫回/AUTO-DEC-006-本機資料庫最小Seed與SQLReadiness定版]] 完成；本節保留原始 Gate 脈絡。

- [x] 取得明確授權後，在專用空白資料庫套用 Migration。
- [x] 驗證 93 張表、315 個索引、View 12 欄型別與 Migration History。
- [ ] 驗證三分支實際資料、SLA 情境及 Down／重新建立。
- [ ] 建立 SQL Server Provider-backed 整合測試。
- [x] 完成最小開發 Seed 與 API SQL Readiness。
- [ ] 完成 10,000 筆展示 Seed 與資料重設。
