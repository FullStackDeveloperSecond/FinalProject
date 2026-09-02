---
batch_id: DEC-BATCH-044
status: applied
decision_date: 2026-09-02
decision_ids:
  - DEC-P354
---

# DEC-BATCH-044｜優惠券折扣類型欄位長度修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P354 | 採用 A：`Coupons.DiscountType` 與 `OrderCoupons.DiscountType` 由 `varchar(16)` 擴充為 `varchar(24)`，以完整保存最長 20 字元的正式 enum `AssemblyFreeShipping`。只新增一支非破壞性 EF Core Migration，`Up` 僅包含兩個擴欄 `AlterColumn`；不改 enum 名稱、不加入自訂短碼 Converter、不修改既有 Migration，也不在本次套用任何資料庫。 |

## Lowest-Cost Analysis

1. 接受現況：SQL Server 會拒絕 `AssemblyFreeShipping`，既定組裝免運功能無法使用，未採用。
2. 只改文件、操作或前端：不能改變 SQL 欄位限制，未採用。
3. 在既有 `varchar(16)` 內使用自訂短碼 Converter：需維護額外映射，資料值不再等同正式 enum，契約與回復成本較高，未採用。
4. 將既有兩欄擴充至 `varchar(24)`：保留全部既有值、支援目前最長 enum，且只需兩個 `ALTER COLUMN`，採用。
5. 改成整數 enum、另建查詢表或重做優惠券 Schema：現有字串契約足夠，變更與遷移成本過大，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 建立組裝免運券的行銷管理員，以及在 Checkout 使用該券的顧客 |
| 現況風險 | `AssemblyFreeShipping` 寫入時被 SQL Server 截斷拒絕，導致功能不可用；Order Coupon 快照亦無法保存該類型 |
| 觸及頻率 | 每次建立／保存組裝免運券與其訂單快照；實際流量未知 |
| 預期可量測結果 | 兩欄均為 `varchar(24)`；既有 Coupon 經 Migration 保留；`AssemblyFreeShipping` 可寫入與讀回 |
| 建置／持續成本 | 兩處 Configuration、一支 Migration 與 Migration／SQL Provider 測試；無套件、服務或持續費用 |
| 風險成本 | `ALTER COLUMN` 需短暫 Schema Modification Lock；展示資料規模有限，但實際部署前仍須確認目標 Migration baseline 與可接受阻塞時間 |
| 信心 | 高；差異由 SQL Server 真實寫入測試重現，產生 SQL 只有兩個擴欄操作 |
| 成功指標 | pending model 為零、Migration 只有兩個 `AlterColumn`、既有資料保留、組裝免運可寫入、相關 Checkout SQL 測試通過 |
| 停止／回復條件 | 目標資料庫 Migration baseline 不等於前一支 Migration、發現外部 Schema 管理或 `ALTER COLUMN` 阻塞超出維護門檻時停止套用；優先 roll forward，不以可能截斷新值的 `Down` 作日常回復 |

## Migration 與回復邊界

- Migration：`20260902031406_WidenCouponDiscountTypeColumns`。
- `Up`：只將 `Coupons.DiscountType`、`OrderCoupons.DiscountType` 從 `varchar(16)` 改為 `varchar(24)`；不更新資料、不改 Nullability、索引、Constraint 或關聯。
- `Down`：結構上可縮回 16，但只要已存在 `AssemblyFreeShipping` 就會截斷／失敗，因此不是安全的資料回復方案；應停用功能並以修正 Migration roll forward。
- 本次只產生與審查 Migration；沒有對 `DoSelectDb` 或任何其他持久資料庫執行 DDL／DML。

## 影響文件

- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[03-架構/09-資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
