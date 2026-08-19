---
文件狀態: 已寫回
最後更新: 2026-08-19
decision_type: direct
status: applied
applied_at: 2026-08-19
---

# AUTO-DEC-006｜本機資料庫、最小 Seed 與 SQL Readiness 定版

## 背景

`InitialCreate` 已通過靜態審查，組長授權依序完成本機套用、結構驗證、最小開發 Seed、API SQL 讀取及文件／交付基線。這批決策讓組員能在不等待 10,000 筆正式展示資料產生器的情況下開始開發。

## 定案

| 項目 | 統一結果 |
|---|---|
| Migration 套用 | 由開發者或部署腳本明確執行；API 啟動不得自動呼叫 `Migrate()`／`MigrateAsync()` |
| 最小 Seed 觸發 | 只接受明確 `--seed-minimal` 指令或 `seed-minimal-development-data.ps1`；一般 API 啟動不得自動 Seed |
| 正式管理角色 | 建立 SuperAdmin、CatalogManager、InventoryManager、OrderManager、FinanceManager、CustomerService、CustomerServiceSupervisor、MarketingAnalyst、PrivacyAdmin、SecurityAdmin 十個角色 |
| 開發帳號 | 建立已確認 Email 的 `admin@doselect.local` 與 `member@doselect.local`；管理員只先指派 SuperAdmin |
| 密碼保存 | `Seed:AdminPassword`、`Seed:MemberPassword` 只存於 .NET User Secrets，不得進入 Repository、文件、日誌或對話 |
| 管理員 TOTP | Seed 不預先設定 TOTP；首次正式管理登入流程必須先完成 Google Authenticator 綁定。重跑 Seed 不得關閉後續已啟用的 TOTP |
| 最小型錄 | 建立一組明確標示為開發用的虛構 Brand、Category、Product、SKU；固定自然鍵與 PublicId，重跑不重複 |
| 冪等性 | 既有帳號不重設密碼；既有資料以角色名、Email 與型錄 Code 查找；第二次執行新增數必須全為 0 |
| Readiness | `/health/ready` 除檔案根目錄探針外，必須由 EF Core 對 `DoSelectDb` 執行最小 `SELECT 1`；公開回應只保留整體 status |
| 範圍邊界 | 本 Seed 是組員開發基線，不取代 DATA-06 的 10,000 筆固定亂數展示資料產生器，也不等同完整認證授權功能 |

## 實作與驗證結果

- `20260819013357_InitialCreate` 已套用至本機 `DoSelectDb`。
- `verify.sql` 驗證 93 張資料表、315 個索引、`vw_CaseWorkbench` 12 欄及 Migration History，結果 PASS。
- 最小 Seed 建立 10 個角色、2 個帳號、2 個 Profile 與 4 筆型錄基礎資料。
- `verify-minimal-seed.sql` 結果 PASS；第二次 Seed 執行新增數全為 0。
- API 實際啟動後，`smoke-api-database.ps1` 對 `/health/ready` 驗證 PASS。
- Identity 密碼仍採框架預設規則；設定腳本在寫入 User Secrets 前檢查長度及大小寫、數字、特殊字元。

## 影響文件

- [[03-架構/本機開發環境與版本基線]]
- [[03-架構/Logging與HealthCheck設計]]
- [[03-架構/資料表實作交付/README]]
- [[05-規劃/未完成項目追蹤表]]
- `FP.dev/README.md`

## 後續 Gate

- [ ] 建立專用 SQL Server Provider-backed 整合測試資料庫。
- [ ] 演練 Down／空庫重新建立，不直接破壞目前唯一的本機開發資料庫。
- [ ] 完成 DATA-06 的 10,000 筆展示 Seed 與 `reset-demo-data.ps1`。
- [ ] 完成管理員首次登入強制 TOTP 綁定及正式認證授權流程。
