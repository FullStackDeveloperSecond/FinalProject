---
type: knowledge
title: 軟刪除與 Filtered Unique Index
aliases:
  - Soft Delete
  - Filtered Unique Index
  - 條件式唯一索引
tags:
  - 知識點
  - SQL Server
  - EF Core
  - 索引
  - 資料完整性
created_at: 2026-08-13
related:
  - "[[03-架構/03-資料與一致性/PublicId與資料完整性設計]]"
  - "[[03-架構/03-資料與一致性/資料庫正規化與反正規化策略]]"
---

# 軟刪除與 Filtered Unique Index

## 軟刪除的目的

軟刪除以 `IsDeleted` 或 `DeletedAtUtc` 標記資料不再有效，而非立即實體刪除。它適合需要保留歷史參照、稽核或復原的主資料，但會讓查詢、唯一性、關聯及保存政策更複雜。

軟刪除不是永久保存的理由；個資刪除、Retention 與真正 Purge 仍要有明確流程。

## 唯一性衝突

若 `SkuCode` 建立全表 Unique Index，已軟刪除的舊資料仍占用 Code。SQL Server Filtered Unique Index 可只限制有效資料：

```sql
CREATE UNIQUE INDEX UX_Skus_NormalizedCode_Active
ON dbo.Skus (NormalizedCode)
WHERE DeletedAtUtc IS NULL;
```

這表示多筆歷史刪除列可以保留相同 Code，但同一時間只能有一筆有效資料。實際 Filter 必須與應用程式的「有效」定義一致。

## 實作注意事項

- Application 先回可理解的重複錯誤，Unique Index 作最終競態保護。
- EF Core Query Filter 不會保護原生 SQL、背景工作或管理查詢；敏感查詢仍需測試。
- Restore 前要重新驗證唯一性與關聯狀態。
- 索引名稱、Filter 與正規化欄位應在 Migration 中明確審查。
- 只有需要保留歷史的 Entity 才使用軟刪除，不要把所有表一律套用。

> [!note] 專案決策邊界
> SKU Code、優惠碼、門市 Code 等正規化系統代碼使用資料庫唯一限制；只限制有效資料時採 Filtered Unique Index。逐表條件仍需在 EF Mapping／Migration 落實。

## 參考資料

- [Microsoft Learn：Create filtered indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes?view=sql-server-ver17)
- [Microsoft Learn：EF Core indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
