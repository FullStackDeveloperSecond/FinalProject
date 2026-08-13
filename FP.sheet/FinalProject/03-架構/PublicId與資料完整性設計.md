---
文件狀態: 部分已確認
最後更新: 2026-08-12
追蹤項目:
  - DES-07
  - DES-08
  - DES-15
---

# PublicId 與資料完整性設計

## 目的

資料庫內部使用 `bigint identity` 作為叢集主鍵與關聯鍵；所有會出現在 API、URL、Log、匯出檔或前端狀態中的業務資源，改以 `PublicId` 對外識別。這個分工避免暴露連號資料量，也保留 SQL Server Join 與索引效率。

## 識別碼規則

| 項目 | 已確認規則 |
|---|---|
| 內部主鍵 | `bigint identity`，只在資料庫與伺服器內部使用 |
| 對外識別碼 | Application 建立 UUID v7，SQL Server 使用 `uniqueidentifier` |
| API 格式 | 回應統一小寫 Guid `D` 格式；Request 接受大小寫，OpenAPI 使用 `type: string, format: uuid` |
| 索引 | `PublicId` 建立非叢集 Unique Index；內部 `Id` 維持叢集主鍵 |
| 對外範圍 | Member、Admin、Product、SKU、Order、Payment、Shipment、Return、Refund、SupportTicket、ReportCase、BuildList、分享連結、圖片及附件等所有外部資源 |
| API 邊界 | Request／Response／Route 不接受或回傳內部 `Id` |
| Log 與稽核 | 一般 Log 記錄 PublicId；AuditLog 可保存內部關聯鍵，但管理介面顯示 PublicId |

PublicId 是資源定位資訊，不是授權憑證。取得 PublicId 後仍必須通過登入、角色、資源所有權及狀態檢查；分享連結另使用具足夠隨機性的可撤銷 Token。

純關聯且沒有獨立 Route、稽核查詢或生命週期的 Join Entity 不配置 PublicId，改以兩端內部 FK 建立複合唯一鍵；可獨立操作的角色指派、附件關聯或其他具生命週期關聯才配置 PublicId。

## 正規化與唯一性

- 一般輸入先 `Trim` 與 Unicode NFKC；顯示值與正規化值依用途分開保存。
- Email 使用 ASP.NET Core Identity 的 `NormalizedEmail`／Invariant 規則，不自行改寫 local-part。
- SKU Code、優惠碼、門市 Code 等系統代碼以大寫保存，採不分大小寫唯一。
- 只限制有效資料的 Code 使用 Filtered Unique Index，軟刪除歷史仍保留。
- Application 先回傳可理解的重複錯誤；資料庫 Unique Constraint 是最終競態保護。

## Constraint 與索引命名

| 類型 | 格式 | 範例用途 |
|---|---|---|
| 一般索引 | `IX_{Table}_{Columns}` | 查詢、排序、FK |
| 唯一索引 | `UX_{Table}_{Columns}` | PublicId、Normalized Code |
| 外鍵 | `FK_{Dependent}_{Principal}_{Column}` | 關聯完整性 |
| 檢查限制 | `CK_{Table}_{Rule}` | One-of 型別值、金額與數量 |

所有 FK 預設建立索引；複合欄位順序、Include 與 Filtered 條件必須依實際查詢與執行計畫調整，不能只靠欄位存在推定。

## 刪除與關聯原則

- 預設 Delete Behavior 為 `Restrict`，避免刪除父資料時連帶移除交易、庫存、付款、客服或稽核歷史。
- 第一版 Cascade 白名單只有 `CartItem`、`BuildListItem`、`ImportRow`。它們必須由父 Aggregate 完整擁有且沒有獨立稽核生命週期。
- OrderItem、Payment、Shipment、Return、Refund、Inventory、Attachment、Notification、AuditLog、Outbox 等其餘關聯維持 `Restrict`，或由具稽核的清理 Use Case 明確刪除。
- MemberProfile 與 AdminProfile 互斥；管理員若需以前台會員身分購買，使用另一個會員帳號。
- Profile 互斥、One-of 規格值與有效資料唯一性需同時由 Application 驗證及資料庫 Constraint 保護。

## EF Core 實作檢查

1. Entity 建立時由 Application／Domain 產生 UUID v7，不依賴資料庫預設值。
2. DTO、Controller Route、OpenAPI Schema 與前端 TypeScript Client 只暴露 PublicId。
3. Migration 必須明確產生 Unique、Filtered Unique、Check Constraint、FK 與 Delete Behavior。
4. 整合測試使用 SQL Server 驗證重複鍵、Cascade、互斥 Profile、One-of 值及並行競態。
5. PublicId 查不到與沒有權限時，錯誤回應不得洩漏資源是否存在。

## 尚待實作

- 各資料表的精確 Unique／Filtered Unique、複合索引與 Check Constraint。
- MemberProfile／AdminProfile 互斥的 SQL Server Constraint 實作方式。
