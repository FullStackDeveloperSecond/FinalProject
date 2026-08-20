---
type: knowledge
title: SQL Server rowversion
aliases:
  - RowVersion
  - SQL Server timestamp
  - 樂觀併發權杖
tags:
  - 知識點
  - SQL Server
  - EF Core
  - 併發
  - rowversion
created_at: 2026-08-10
related:
  - "[[01-需求/專案名詞表]]"
  - "[[02-領域需求/02-商品庫存與組裝/庫存規則]]"
  - "[[03-架構/03-資料與一致性/狀態機設計]]"
  - "[[知識點/04-資料模型與一致性/冪等性]]"
  - "[[知識點/03-API契約與可觀測性/DTO與API Schema]]"
---

# SQL Server rowversion

## 它是什麼

`rowversion` 是 SQL Server 自動產生的 8-byte 二進位值。包含 `rowversion` 欄位的資料列每次被 `INSERT` 或 `UPDATE` 時，SQL Server 會寫入新的資料庫版本值。

```sql
CREATE TABLE Products
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    RowVersion rowversion NOT NULL
);
```

它常用作樂觀併發權杖，回答：「這筆資料從我上次讀取後，有沒有被其他交易修改？」

## 它不是什麼

- 不是日期或時間，不能換算成 `UpdatedAt`。
- 不是每筆資料從 1 開始的版本號。
- 不適合作為 Primary Key，因為每次更新都會改變。
- 不保證能跨資料庫比較。
- SQL Server 舊稱 `timestamp`，但該名稱容易與時間混淆，且 `timestamp` 語法已棄用；新設計應使用 `rowversion`。

即使 UPDATE 把欄位設成原本相同的值，SQL Server 仍可能產生新的 `rowversion`。

## EF Core 映射

使用 Data Annotation：

```csharp
public sealed class Product
{
    public Guid Id { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
```

或 Fluent API：

```csharp
modelBuilder.Entity<Product>()
    .Property(x => x.RowVersion)
    .IsRowVersion();
```

`[Timestamp]` 是 .NET Attribute 名稱；SQL Server 欄位型別仍應是 `rowversion`。

## 樂觀併發流程

```text
使用者 A、B 同時讀取 Product，RowVersion = V1
→ A 先更新成功，資料庫產生 V2
→ B 仍帶 V1 更新
→ UPDATE 找不到 Id＋V1 的資料列
→ EF Core 拋出 DbUpdateConcurrencyException
```

EF Core 產生的 SQL 概念如下：

```sql
UPDATE Products
SET Name = @name
WHERE Id = @id
  AND RowVersion = @originalRowVersion;
```

成功時資料庫回傳新的 `rowversion`；失敗時應重新讀取目前資料，再依使用案例選擇拒絕、顯示差異、合併或安全重試。

## API 契約

`byte[]` 可在 JSON 使用 Base64 字串傳遞：

```json
{
  "id": "...",
  "name": "新名稱",
  "rowVersion": "AAAAAAAAB9M="
}
```

成功更新後，Response 必須回傳新的 `rowVersion`。若採 HTTP ETag，也可以：

```http
ETag: "AAAAAAAAB9M="
If-Match: "AAAAAAAAB9M="
```

專案已統一採 Body Token：Response 回傳 Base64 `rowVersion`，Update／Command Request 必須原樣帶回；併發衝突回：

```http
409 Conflict
```

並使用統一 Problem Details，包含穩定錯誤碼、Trace ID，以及前端重新載入所需資訊；不可把資料庫例外堆疊直接回傳。

## 庫存情境

`rowversion` 能偵測同一列已被修改，但不會自動保證庫存規則。防止超賣仍需短交易、條件更新與重新驗證：

```sql
UPDATE Inventory
SET ReservedQuantity = ReservedQuantity + @quantity
WHERE SkuId = @skuId
  AND RowVersion = @rowVersion
  AND OnHandQuantity - ReservedQuantity >= @quantity;
```

只有影響一列才算成功。多 SKU 訂單必須在同一資料庫交易中全部成功，任一失敗就回滾；不能只在前端比較 `rowVersion`。

## 衝突處理策略

| 情境 | 建議處理 |
|---|---|
| 管理員編輯商品 | 回 409，顯示資料已變更並要求重新載入／比較 |
| 庫存保留 | 在短交易中重新讀取並有限重試，仍不足則回庫存衝突 |
| 狀態機轉移 | 重新驗證最新狀態，不可只覆蓋 |
| 背景冪等工作 | 若目標已完成則安全結束；否則依最新狀態決定是否重試 |

不可對所有 `DbUpdateConcurrencyException` 無條件重試，否則可能覆蓋使用者修改或重複外部副作用。

## 模型與遷移注意事項

- 一張資料表最多一個 `rowversion` 欄位。
- 任何欄位更新都會使整列 Token 改變，可能造成與業務無關的衝突。
- 加入 `rowversion` 會改變 EF Model、Snapshot 與 migration，應依遷移流程審查，不直接套用正式資料庫。
- 投影 DTO 必須包含 Token；否則前端更新時失去原始版本。
- 測試兩個獨立 DbContext 讀取同一列，再依序更新，確認第二次產生衝突。

> [!warning] 專案決策邊界
> DEC-P56 已確認一般可編輯資料使用 `rowversion`、衝突回 `409`，庫存另搭配交易與條件更新；配置白名單與欄位表達方式已在 [[03-架構/03-資料與一致性/PublicId與資料完整性設計]] 定義，正式 API 規則見 [[03-架構/02-API與前端契約/API共通規範]]。

## 參考資料

- [Microsoft Learn：SQL Server rowversion](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql?view=sql-server-ver17)
- [Microsoft Learn：EF Core Concurrency Conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Microsoft Learn：SQL Server Provider Value Generation](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/value-generation)
- [[03-架構/03-資料與一致性/狀態機設計]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
