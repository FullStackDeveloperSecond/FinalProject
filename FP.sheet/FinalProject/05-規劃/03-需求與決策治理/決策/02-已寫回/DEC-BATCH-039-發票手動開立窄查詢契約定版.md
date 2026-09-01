---
batch_id: DEC-BATCH-039
status: applied
decision_date: 2026-09-01
decision_ids:
  - DEC-P348
---

# DEC-BATCH-039｜發票手動開立窄查詢契約定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P348 | 採用 A1：新增 `Invoice.Manage` 專用的 `GET /api/v1/admin/orders/{orderId}/invoice-issuance`。回應只含訂單 `PublicId`、訂單編號、已付款／已取消事實、`RowVersion` 與是否已有模擬發票；不得回傳收件人、品項、內部 `OrderId` 或其他訂單內容，也不得擴大 `Order.Manage`。FinanceManager／SuperAdmin 必須先讀取此快照，再以同一 `RowVersion` 呼叫既有手動開立命令；遇到 `concurrency_conflict` 時重新查詢並要求操作者再次確認。 |

## Lowest-Cost Analysis

1. 接受現況或由操作者手動輸入 `RowVersion`：FinanceManager 沒有合法來源可取得並驗證目前版本，無法完成可靠操作，未採用。
2. 只改流程／文件或設定：不能建立必要的可信伺服器讀取邊界，未採用。
3. 讓 FinanceManager 使用既有完整 Order 管理查詢：會擴大 `Order.Manage` 並暴露與開票無關的訂單內容，超出最小權限，未採用。
4. 擴充既有 Orders-owned 開票讀取埠並新增目的型窄 DTO：不新增 Schema、套件或第二套查詢架構，且完整滿足操作與最小揭露，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | FinanceManager、SuperAdmin |
| 現況風險 | 操作者無法合法取得最新 Order RowVersion；若改用完整訂單查詢會造成過度授權與資料揭露 |
| 觸及頻率 | 每次管理端手動開立模擬發票；實際頻率未知 |
| 預期可量測結果 | 允許角色可查到六個核准欄位並完成開立；OrderManager、未 MFA 與匿名呼叫被拒；回應無收件人、品項及內部 ID |
| 建置／持續成本 | 重用既有 Orders port、Invoicing existence reader、`Invoice.Manage`、OpenAPI 與 Vue Query；無新套件、Schema、服務或固定維運成本 |
| 風險成本 | 主要為過度揭露、過期 RowVersion 或重複開立；以窄 DTO、Policy、冪等與併發測試控制 |
| 信心 | 高；API pipeline、Application、SQL Server provider 與 Vue 元件均有聚焦證據 |
| 成功指標 | 欄位白名單、角色／MFA 正負向、SQL 投影、RowVersion 開立及衝突重查全部通過 |
| 停止／回復條件 | 回應出現未核准欄位、繞過 `Invoice.Manage`、重複發票或 RowVersion 不受控時停止；此 additive Endpoint／UI 可獨立移除 |

## 契約與實作邊界

- `GET /api/v1/admin/orders/{orderId}/invoice-issuance` 為 additive 契約，既有查詢與命令 Route 不改名、不移除。
- Orders Infrastructure 只投影開票確認需要的訂單事實；Invoicing Application 再由自己的 existence reader 補上 `hasInvoice`，兩個模組不互相直查 DbSet。
- 內部 `OrderId` 只允許在既有窄 FK 例外中查詢 `SimulatedInvoices.OrderId`，不得序列化、記錄或傳到前端。
- 無 Entity、Mapping、Migration 或 Production SQL 變更。

## 影響文件

- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-負責範圍補全推進計畫]]
