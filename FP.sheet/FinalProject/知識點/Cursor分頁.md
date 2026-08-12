---
type: knowledge
title: Cursor 分頁
aliases:
  - Cursor Pagination
  - Keyset Pagination
  - Seek Pagination
  - 游標分頁
tags:
  - 知識點
  - API
  - 資料庫
  - 分頁
  - EF Core
  - Cursor
created_at: 2026-08-10
related:
  - "[[03-架構/系統架構]]"
  - "[[05-規劃/未完成項目追蹤表]]"
---

# Cursor 分頁

## 基本概念

Cursor 分頁通常以 Keyset／Seek 查詢實作。客戶端不傳「第幾頁」，而是傳上一頁最後一筆資料的位置，API 再取這個位置之後的資料。

這裡的 Cursor 是 API 位置憑證，不是 SQL Server 的 `CURSOR` 資料庫物件；一般實作仍是一段可使用索引的普通 `SELECT ... WHERE ... ORDER BY` 查詢。

```text
頁碼／Offset：跳過前 N 筆，再取 limit 筆
Cursor／Keyset：從最後排序鍵之後，再取 limit 筆
```

例如依 `Id` 遞增：

```csharp
var page = await db.Products
    .Where(x => x.SequenceId > lastSequenceId)
    .OrderBy(x => x.SequenceId)
    .Take(limit + 1)
    .ToListAsync();
```

`limit + 1` 用來判斷是否還有下一頁；回傳時只保留前 `limit` 筆。

## API 契約

```http
GET /api/products?limit=20&after=eyJpZCI6NTV9
```

```json
{
  "items": [],
  "nextCursor": "opaque-value-or-null",
  "hasNextPage": true
}
```

Cursor 對客戶端應是 opaque value，前端只保存並原樣傳回，不依賴其內部格式。

## 複合排序鍵

只依非唯一欄位排序會漏資料或重複。例如多筆訂單可以有相同建立時間，因此需要唯一且可排序的 tie-breaker。若領域主鍵是無法直接在 LINQ 使用範圍比較的識別碼，可另使用受索引保護的數值 `SequenceId`：

```text
ORDER BY CreatedAtUtc DESC, SequenceId DESC
```

上一頁最後一筆為 `(lastCreatedAt, lastSequenceId)` 時，下一頁條件為：

```text
CreatedAtUtc < lastCreatedAt
OR (CreatedAtUtc = lastCreatedAt AND SequenceId < lastSequenceId)
```

EF Core 範例：

```csharp
query
    .Where(x =>
        x.CreatedAtUtc < cursor.CreatedAtUtc ||
        (x.CreatedAtUtc == cursor.CreatedAtUtc &&
            x.SequenceId < cursor.SequenceId))
    .OrderByDescending(x => x.CreatedAtUtc)
    .ThenByDescending(x => x.SequenceId)
    .Take(limit + 1);
```

排序必須完全唯一且固定，資料庫也要建立對應複合索引，例如 `(CreatedAtUtc DESC, SequenceId DESC)`。

## Cursor 內容與安全

Cursor 可以包含：

```json
{
  "createdAtUtc": "2026-08-10T08:30:00Z",
  "sequenceId": 55821,
  "sort": "createdAtUtc_desc",
  "filterHash": "...",
  "version": 1
}
```

再序列化為 Base64URL。Base64 只是編碼，不是加密；Cursor 不應放個資或秘密。若不希望客戶端修改排序位置、突破查詢限制或製造昂貴查詢，可使用 HMAC 簽章或伺服器端狀態 Cursor。

Cursor 必須與目前排序、篩選、搜尋及租戶／權限條件綁定。使用者改變篩選後應從第一頁重新查詢，不能沿用舊 Cursor。

## 優點

- 不需要資料庫掃描並丟棄大量前置資料，深頁效能通常優於大 Offset。
- 在前方插入或刪除資料時，較不容易因整體位移造成漏筆或重複。
- 適合無限捲動、時間軸、活動紀錄與「載入更多」。

## 限制

- 不適合直接跳到第 37 頁。
- 不容易提供精確總頁數；另外執行 `COUNT(*)` 可能昂貴。
- 上一頁需要保存 previous cursor、反轉排序查詢或另設 `before` 契約。
- 動態支援許多排序欄位時，每一種排序都需要對應條件與索引。
- Cursor 不是資料庫 Snapshot；若資料的排序鍵本身被更新，仍可能移動位置。
- 任意搜尋分數排序或跨多來源聚合，Cursor 設計會更複雜。

## 與頁碼分頁的選擇

| 使用情境 | 適合方式 |
|---|---|
| 後台表格需要總筆數、頁碼及跳頁 | 頁碼／Offset |
| 商品瀑布流、活動記錄、聊天訊息 | Cursor／Keyset |
| 既要跳頁又要改善下一頁效能 | 混合策略，但契約較複雜 |

> [!warning] 專案決策邊界
> DEC-P53 已確認「頁碼分頁、預設 20、上限 100、回傳總筆數」，不是 Cursor 分頁。本頁是替代方案知識；若未來高資料量列表改用 Cursor，需另開決策並更新 OpenAPI 契約、前端元件與索引設計。

## 參考資料

- [Microsoft Learn：EF Core Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [[03-架構/系統架構]]
- [[05-規劃/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
