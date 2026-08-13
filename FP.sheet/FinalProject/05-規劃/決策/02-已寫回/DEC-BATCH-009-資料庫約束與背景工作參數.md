---
type: decision-record
batch_id: DEC-BATCH-009
title: 資料庫約束與背景工作參數
status: applied
created_at: 2026-08-12
applied_at: 2026-08-12
decision_count: 30
decision_range: DEC-P205～DEC-P234
---

# DEC-BATCH-009｜資料庫約束與背景工作參數

## 決策結果

| ID | 決策 | 正式結果 |
|---|---|---|
| DEC-P205 | 動態規格型別 | `StringValue`、`DecimalValue`、`BooleanValue`、`OptionId` 四擇一，以 Check Constraint 保證 One-of |
| DEC-P206 | 規格單位 | 建立 `MeasurementUnit` 參照資料，由規格定義引用 Unit Code |
| DEC-P207 | Option 基數 | 第一版單選；多值標籤使用 Join Entity |
| DEC-P208 | 地址快照 | 台灣地址與超商欄位明確保存，依配送方式驗證必填 |
| DEC-P209 | 商品規格快照 | 顯示摘要＋版本化輔助 JSON；金額與退款不依 JSON 重算 |
| DEC-P210 | PublicId 格式 | 小寫 Guid `D` 格式；輸入接受大小寫，OpenAPI 為 UUID |
| DEC-P211 | Join PublicId | 只有可獨立操作、稽核或有生命週期的 Join Entity 配置 PublicId |
| DEC-P212 | Cascade 白名單 | 第一版只允許 CartItem、BuildListItem、ImportRow；其他預設 Restrict |
| DEC-P213 | 工作台欄位 | 固定 12 個共通欄位 |
| DEC-P214 | 工作台授權 | 每個 UNION 分支在 SQL 查詢前套用授權範圍 |
| DEC-P215 | 工作台分頁 | `LastActivityAtUtc DESC, CasePublicId DESC` Cursor 分頁 |
| DEC-P216 | Import 狀態 | 完整狀態機；Failed 重新建立批次，Committed 不可重送 |
| DEC-P217 | ImportRow | 正規化欄位＋有限、版本化 Raw JSON |
| DEC-P218 | 匯入上限 | 單檔 10 MB、5,000 列 |
| DEC-P219 | 並行批次 | 每位管理員每種匯入類型一個 Active Batch |
| DEC-P220 | 成功 Staging | Batch 摘要 90 天，Row／Raw 24 小時 |
| DEC-P221 | 失敗 Staging | 錯誤列 24 小時，Batch 摘要 90 天 |
| DEC-P222 | 庫存匯入 | 輸入目標 OnHand，預覽計算 Delta，提交時檢查 Balance 版本 |
| DEC-P223 | 調整原因 | 受控原因 Code；Other 必填備註 |
| DEC-P224 | Outbox 保留 | 成功 30 天；失敗訊息不自動刪除 |
| DEC-P225 | Dispatcher | 每 5 秒、每批 20 筆 |
| DEC-P226 | 訊息順序 | 同 Aggregate 有序，不同 Aggregate 可並行 |
| DEC-P227 | AuditLog 保留 | 一般保存 365 天 |
| DEC-P228 | Audit 權限 | SecurityAdmin／PrivacyAdmin 依職責查詢，只有 SuperAdmin 可匯出 |
| DEC-P229 | Legal Hold | 第一版不做 UI，預留 `RetentionUntilUtc`／`HoldReason` 設計點 |
| DEC-P230 | 處理中冪等鍵 | 回 `409 Conflict`＋`Retry-After`；不同 Request Hash 拒絕重用 |
| DEC-P231 | ResponseSummary | 最多 32 KB 版本化 JSON；超過改存資源 PublicId |
| DEC-P232 | 本機資料根目錄 | 展示機採 `E:\FinalProjectData`，由本機未提交設定覆寫 |
| DEC-P233 | 磁碟告警 | 可用空間低於 20% 或 20 GB 任一條件告警 |
| DEC-P234 | 庫存核對 | 每日 02:00（Asia/Taipei）；差異建立 Critical 案件，不自動改值 |

## 一致性與衝突檢查

- 30 題都有已選答案，沒有自主輸入，沒有缺答。
- 全部答案與 DEC-BATCH-001～008 相容。
- DEC-P212 將先前「逐項白名單」具體化，未列關聯維持 `Restrict`。
- DEC-P224～231 補完 Outbox、AuditLog 與 Idempotency 的操作參數，不改變既有交易邊界。
- DEC-P232 是展示機設定，不得硬編碼進共用 Repository；其他成員可用各自本機覆寫。

## 影響文件

- [[02-領域需求/商品、組裝與相容性]]
- [[02-領域需求/購物車、訂單、付款與物流]]
- [[03-架構/PublicId與資料完整性設計]]
- [[03-架構/統一案件工作台設計]]
- [[03-架構/資料一致性、Outbox與冪等設計]]
- [[03-架構/匯入暫存與庫存調整設計]]
- [[03-架構/背景工作與Hangfire設計]]
- [[03-架構/備份與復原策略]]
- [[01-需求/角色與權限]]
- [[05-規劃/未完成項目追蹤表]]

## 追蹤結果

- DES-13 的工作台設計條件已補完，可由實作與測試接續。
- TECH-09、TECH-11 的設計決策已補完，剩餘工作屬 Schema／Consumer 與程式實作。
- DOM-10、DES-07、DES-08、DES-15、QA-07 進一步收斂，但仍有其他逐表或腳本工作。
