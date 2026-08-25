---
type: decision-record
batch_id: DEC-BATCH-024
title: SQL Server 整合測試納入 Required CI
status: applied
created_at: 2026-08-25
applied_at: 2026-08-25
source: alex 採用沿用既有 SQL Server CI service 並移除 RequiresSqlServer 排除條件的建議
decision_ids:
  - DEC-P307
---

# DEC-BATCH-024｜SQL Server 整合測試納入 Required CI

## 背景

Backend CI 已啟動 SQL Server 2025 container、提供測試連線字串並套用 EF Core Migration，但 `dotnet test` 仍使用 `Category!=RequiresSqlServer`，使 Actor A／B、資料一致性與 Provider-backed 測試在 Required Gate 中被靜默排除；Workflow 註解也與實際已存在的 SQL service 矛盾。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P307 | Required CI 沿用現有 SQL Server 2025 service、健康檢查、連線字串與 Migration，不新增 Runner、服務或套件；Backend 測試命令固定為無 `RequiresSqlServer` 排除條件的 `dotnet test DoSelect.slnx --no-build --no-restore`。Workflow 或測試分類變更不得使 Provider-backed 測試靜默退出 Required Gate。 |

## 最低成本分析

1. 維持排除條件：無法證明 SQL Server 授權與資料副作用測試會阻擋合併，不符合 QA-08，排除。
2. 只要求人工本機證據：證據不穩定且不能保護後續 PR，不符合自動合併 Gate，排除。
3. 沿用既有 CI SQL service 並移除 filter：不新增依賴或基礎設施即可滿足驗收，採用。
4. 新增專用 Runner／資料庫服務：現有 GitHub-hosted Runner 已具備所需能力，成本較高且沒有額外必要成果，不採用。

## 商業影響

- 受影響者：API／資料庫開發者、Reviewer 與整合負責人。
- 目前風險：跨會員存取、拒絕後資料副作用或 SQL Server 專屬行為可能在本機漏測後進入 `dev`。
- 觸及頻率：每次修改私人 Endpoint、EF Core 查詢、授權範圍或資料寫入的 PR。
- 預期可量測成果：`RequiresSqlServer` 測試由每次 Required CI 實際執行，任一失敗阻擋合併。
- 建置與持續成本：調整一條既有測試命令；既有 SQL service 本就會啟動，未新增服務、套件、Schema 或帳務支出，測試時間可能增加。
- 主要風險成本：過去只在本機執行的測試可能暴露 Linux／隔離性／資料清理問題並延長 CI；這是應被看見的驗收失敗，不以重新排除測試處理。
- 信心：高；Workflow 已具備 SQL Server health check、連線字串與 Migration 步驟。
- 成功指標：PR Required CI 的 Backend Job 以無 filter 的 `dotnet test` 成功，並包含 `RequiresSqlServer` 案例。
- 停止／回退條件：只有確認測試屬非決定性或共享資料污染時，才先隔離／修正該測試；不得以全面 filter 作為常態回退。

## 影響文件與追蹤

- `.github/workflows/ci.yml`
- [[03-架構/08-測試與驗收/測試策略]]
- [[03-架構/08-測試與驗收/QA-08私人資源授權覆蓋矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]] 的 `QA-08`、`DEV-07`
