---
type: decision-record
batch_id: DEC-BATCH-026
title: 相容性規則中央稽核與 API 契約定版
status: applied
created_at: 2026-08-26
applied_at: 2026-08-27
source: alex 依 PR #34 完整 Review 結果確認上游契約
decision_ids:
  - DEC-P309
  - DEC-P310
  - DEC-P311
  - DEC-P312
---

# DEC-BATCH-026｜相容性規則中央稽核與 API 契約定版

## 背景

PR #34 已實作自由組裝與相容性規則 API，但後台設定、啟停與測試的稽核責任、測試結果 JSON 版本、啟停 Route，以及 Build List DTO 的擁有者欄位仍與現行 `dev` 共用能力或權威文件不一致。最新 `dev` 已有中央 `AuditLogs`、`IAuditWriter` 與 `AddCentralAuditLogs` Migration，因此不應再為相容性功能建立第二套稽核 Schema。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P309 | 相容性規則的警告門檻更新、啟停與管理員測試一律沿用中央 `IAuditWriter`／`AuditLogs`。三個 Action 固定為 `compatibility_rule.warning_setting.update`、`compatibility_rule.activation.update`、`compatibility_rule.test`；設定與啟停以本次新增的 `CompatibilityRuleSetting.PublicId` 為 Resource PublicId，測試以本次新增的 `CompatibilityCheckRun.PublicId` 為 Resource PublicId。白名單只允許 `ruleCode`、`settingCode`、`value`、`isActive`、`settingsVersion`、`inputHash`、`overall` 等安全結構化欄位；中央 Audit 保存 Actor PublicId／角色、遮蔽 IP、Reason、CorrelationId、TraceId、Before／After 與既有 Schema Version。核心資料與 Audit 必須使用同一 DbContext／SQL Server 交易，成功操作恰有一筆 Audit，Audit 失敗時整體回滾。不得新增相容性專用 Audit 表、重複稽核欄位或新的 EF Migration。 |
| DEC-P310 | `CompatibilityCheckRun`／`CompatibilityCheckResult` 保存可重現的技術快照；`FactsJson` 固定使用 `{"schemaVersion":1,"facts":{...}}` Envelope，未來變更內容結構時增加版本而非猜測舊格式。管理員測試的中央 Audit 只保存輸入 SKU PublicId 集合的 SHA-256 Hash、結果摘要、設定版本與 TraceId，不保存完整商品內容副本。 |
| DEC-P311 | 相容性規則啟停固定使用 `PATCH /api/v1/admin/compatibility-rules/{ruleCode}/activation`，Request 帶 `isActive`、`reason` 與 `rowVersion`；不使用 `POST .../actions/set-activation`。仍只有 `CompatibilityRule.ManageActivation`（SuperAdmin）可執行。 |
| DEC-P312 | `BuildListDto` 不回傳固定值 `owner:member`。擁有權由後端 Actor Scope／Owner Query 強制驗證，不以 DTO 常數代表授權；公開 `SharedBuildDto` 仍不得回 Owner。 |

## 最低成本分析

1. 接受現況：中央 Audit 契約、Route 與 DTO 文件持續不一致，PR #34 無法形成可驗收基線，排除。
2. 只在 PR 留言描述：Terry 修正後仍缺少上游權威來源，後續實作者可能再次採用舊契約，排除。
3. 沿用既有中央 Audit、現有設定／測試快照與既有 Migration，只更新正式契約並讓 PR #34 接線：可滿足稽核、資料完整性、相容性與可追溯要求，採用。
4. 新增相容性專用 Audit Schema／Migration：與中央能力重複，增加資料同步、保留政策、查詢與回復成本，排除。

## 商業影響

- 受影響者：後台管理員、Reviewer、維運與自由組裝使用者。
- 目前風險：規則異動或測試無法完整追查操作者與版本；平行稽核實作可能產生不一致紀錄；錯誤 Route／DTO 會增加前後端重工。
- 觸及頻率：每次警告門檻更新、規則啟停、管理員測試，以及每次 Build List 明細查詢。
- 預期可量測成果：三種成功管理操作各產生恰一筆中央 Audit；Audit 失敗時核心資料不提交；FactsJson 可依版本解析；OpenAPI 與 Typed Client 只保留裁定 Route／欄位。
- 建置與持續成本：沿用現有 Audit Entity、Writer、Migration 與 365 天保留；只增加 Action／Resource／Field 白名單、Use Case 接線、JSON Envelope 與測試，無新套件、Schema、服務或持續費用。
- 主要風險成本：漏接任一管理 Action、同一操作重複寫 Audit，或測試 Run 與 Audit Resource 未對齊。
- 信心：高；中央 Audit 已在 `dev` 合併並有 SQL Server Provider-backed 證據。
- 成功指標：SDK 10.0.303 下 Build／Format／完整 SQL Server 測試、Migration pending check、OpenAPI／Typed Client diff 全部通過。
- 停止／回退條件：若中央 Audit 無法承載已列安全欄位或同交易寫入，停止 PR #34，不得直接新增第二套 Schema；須重新提出 Migration Gate 與資料治理決策。

## Terry 實作 Gate

1. 由 alex 完成上游文件並將 PR #34 rebase 到最新 `dev`；Terry 不需自行 rebase，待上游同步完成後再實作下列項目。
2. 接入三個中央 Audit Action／Resource／Field 白名單，並以同交易測試證明成功恰一筆、Audit 失敗整體回滾。
3. 讓管理員測試建立 `CompatibilityCheckRun` 後取得其 PublicId，作為 Audit Resource；FactsJson 使用 Schema Version 1 Envelope。
4. 將啟停 Route、OpenAPI、Typed Client 與測試統一成裁定的 PATCH Route；移除 `BuildListDto.owner`。
5. 完整自我檢查功能範圍，不只逐項修正 Review 留言；再處理 PR #34 其餘已知的分享鎖競態、Retention 排程／索引與 OpenAPI CI 問題。

## 影響文件

- [[03-架構/07-領域設計/相容性規則後台設計]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
