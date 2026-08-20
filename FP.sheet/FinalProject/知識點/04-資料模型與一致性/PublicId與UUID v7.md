---
type: knowledge
title: PublicId 與 UUID v7
aliases:
  - Public ID
  - UUIDv7
  - Guid v7
tags:
  - 知識點
  - 識別碼
  - UUID
  - SQL Server
  - API
created_at: 2026-08-13
related:
  - "[[03-架構/03-資料與一致性/PublicId與資料完整性設計]]"
  - "[[03-架構/02-API與前端契約/API共通規範]]"
  - "[[知識點/03-API契約與可觀測性/DTO與API Schema]]"
---

# PublicId 與 UUID v7

## 為何分內部與外部 ID

內部 `bigint identity` 適合 SQL Server 叢集主鍵、Join 與儲存效率，但直接放進 URL 會暴露連號與資料量，也讓外部契約依賴資料庫實作。

PublicId 是對外穩定識別；它可使用 UUID v7，資料庫仍保留內部 Id：

```text
Id       bigint identity       內部叢集主鍵／FK
PublicId uniqueidentifier      對外非叢集唯一索引
```

## UUID v7 的特性

UUID 是 128-bit 識別碼。UUID v7 的高位包含 Unix Epoch 毫秒時間，其餘主要為隨機資訊，因此相較完全隨機 UUID 更具有大致時間順序，通常對索引區域性較友善。

它不是精確建立時間欄位，也不應取代 `CreatedAtUtc`；同一毫秒內的順序取決於產生器策略。PublicId 也不是授權 Token，取得值後仍須通過登入、角色、資源所有權與狀態檢查。

## API 與資料庫規則

- Application 建立 UUID v7，不依賴各資料庫預設值。
- Response 固定小寫 Guid `D` 格式；Request 可大小寫不敏感解析。
- OpenAPI 使用 `type: string, format: uuid`。
- Route、DTO、前端狀態與一般 Log 不暴露內部 Id。
- 純 Join 且無獨立生命週期者不必配置 PublicId。

> [!note] 專案決策邊界
> 專案已確認所有可路由、稽核或具生命週期的外部資源使用 UUID v7 PublicId；內部 bigint 維持叢集主鍵，詳見 [[03-架構/03-資料與一致性/PublicId與資料完整性設計]]。

## 參考資料

- [RFC 9562：Universally Unique IDentifiers—UUID Version 7](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.7)
- [Microsoft Learn：Guid.CreateVersion7](https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7)
