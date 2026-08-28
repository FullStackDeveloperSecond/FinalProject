---
batch_id: DEC-BATCH-030
status: applied
decision_date: 2026-08-28
decision_ids:
  - DEC-P322
  - DEC-P323
---

# DEC-BATCH-030｜商品圖片 Hash 與 Outbox 人工重送定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P322 | 公開商品圖片 URL 的 `contentHash` 必須是該尺寸 WebP 衍生檔自己的 SHA-256，不使用原圖 Hash。`ProductImages` 新增 nullable `SmallSha256`、`MediumSha256`、`LargeSha256 binary(32)` 以相容既有列；新圖必須三者齊備才能 Published。公開 Route 只接受 `320`／`800`／`1600`，Hash、Published 狀態或實體檔不符均回 404；成功回一年 immutable Cache。 |
| DEC-P323 | Outbox 人工重送使用 `POST /api/v1/admin/outbox-messages/{publicId}/actions/retry` 與具名 Policy `Outbox.Retry`，只允許已完成 MFA 的 SuperAdmin。Request 必填穩定 `reasonCode`；只有 Failed 可改回 Pending，其他狀態回 `409 outbox_message_not_retryable`。原 Payload 與 AttemptCount 不得改寫，狀態更新與 `outbox.retry` 中央 Audit 必須同次提交，成功回 202。 |

## Lowest-Cost Analysis

### 商品圖片

1. 維持原圖 Hash：無法讓 URL 精確表示實際回傳的衍生檔內容，未採用。
2. 由檔案系統每次讀取時計算：不用改 Schema，但增加每次請求 I/O／CPU 且失去低成本 immutable 快取驗證，未採用。
3. 保存三個既有產圖流程已計算的 Hash：只增加三個 nullable 欄位與讀取路由，完整符合契約，採用。

### Outbox 人工重送

1. 只靠唯讀 Dashboard／人工改 SQL：無法滿足授權、狀態不變量及 Audit，未採用。
2. 新增最小具名 Action Endpoint，延用既有 Policy、DbContext、RowVersion 與 Audit Writer：可完整滿足需求且無新服務／套件，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 前台商品瀏覽者、值班 SuperAdmin、展示與維運人員 |
| 現況風險 | 衍生圖 URL 無法精確失效；Failed Outbox 缺少受控恢復入口，只能停留或人工改資料 |
| 預期可量測結果 | 正確 Hash／狀態圖片請求 200、錯誤組合 404；Outbox 只有 Failed＋MFA SuperAdmin 可得 202，並存在同資源 Audit |
| 建置／持續成本 | 三個 nullable 欄位、兩個 Controller 路徑及既有測試；無新套件、服務或固定費用 |
| 風險成本 | Migration 尚未套用前，執行新程式會缺欄；舊 Published 圖片無 Variant Hash 時安全回 404，須由商品圖片垂直切片重新處理或補值 |
| 信心 | 高；加法 Migration 與 SQL Server Provider-backed 路徑已有測試證據 |
| 停止／回復條件 | Migration 基線不符、出現非三欄加法 DDL、Policy／Audit 測試失敗即停止；應用可先不發布新路徑，資料庫以 roll-forward 為優先 |

## 實作與資料庫 Gate

- Migration `20260828015922_AddProductImageVariantHashes` 只新增三個 nullable `binary(32)`；無回填、Drop、Rename、Index 或 Constraint。
- Migration 與 SQL 已完成 Scaffold／Review，並只套用至唯一命名、驗證後已刪除的可拋棄測試資料庫；沒有套用至開發資料庫。
- 2026-08-28 已將分支 rebase 至 `origin/dev@5c31cd1`，保留上游 `20260827153437_AddOrderShippingMethodBaseFeeSnapshot` 後重新 Scaffold。`has-pending-model-changes` 回報無差異；定向 SQL Review 只有三個 nullable `ALTER TABLE ... ADD` 與 Migration History，Migration Gate 已通過。Migration Chain 只套用至唯一命名、驗證後已刪除的暫時測試資料庫，未套用開發資料庫。
- 商品管理上傳、預覽、發布與中繼資料 Endpoint 仍屬 M-03 垂直切片；本批不代替該功能。
- Outbox 各業務事件寫入與告警仍由各垂直切片完成；本批不擴張事件種類或 Dispatcher 重試政策。

## 影響文件

- [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]
- [[03-架構/03-資料與一致性/資料字典-商品庫存與組裝]]
- [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]
- [[03-架構/05-背景工作與維運/背景工作與Hangfire設計]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/API錯誤碼目錄]]
- [[01-需求/角色與權限]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
