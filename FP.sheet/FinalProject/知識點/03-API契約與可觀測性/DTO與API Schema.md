---
type: knowledge
title: DTO 與 API Schema
aliases:
  - Data Transfer Object
  - API DTO
  - Schema契約
tags:
  - 知識點
  - API
  - DTO
  - Schema
  - ASP.NET Core
created_at: 2026-08-13
related:
  - "[[03-架構/02-API與前端契約/API DTO與Schema契約]]"
  - "[[03-架構/02-API與前端契約/API共通規範]]"
  - "[[知識點/03-API契約與可觀測性/OpenAPI與Typed Client]]"
---

# DTO 與 API Schema

## DTO 是什麼

DTO（Data Transfer Object）是跨 API 邊界傳輸的資料形狀。它不是 EF Core Entity 的別名，而是針對特定使用案例設計的 Request 或 Response 契約。

```text
資料庫 Entity：持久化、關聯、內部主鍵
Domain／Application：商業行為與規則
DTO：外部可見欄位、格式、上限與版本
```

若直接序列化 Entity，常會暴露內部 `Id`、導覽屬性、敏感欄位，並讓資料庫重構意外破壞 API。

## Request 與 Response 不必相同

- Create Request 只接受呼叫端可指定的欄位。
- Update Request 帶 `rowVersion`，但不必接受不可變 Code。
- Response 可包含計算結果、`availableActions`、遮蔽摘要與 PublicId。
- List DTO 應較精簡，Detail DTO 才提供完整明細。

「前端沒有顯示」不等於欄位可以安全回傳；最小揭露應由後端 DTO 決定。

## Schema 應描述什麼

OpenAPI／JSON Schema 至少需表達：

- 必填與可空的差異。
- 字串、陣列及數值上下限。
- Enum、UUID、日期時間及 decimal 語意。
- One-of／互斥欄位。
- 分頁容器與錯誤格式。
- 範例不得放真實 Token、TOTP、Email 或地址。

TypeScript 型別只能防止編譯期誤用，API 仍必須做執行期格式、授權及商業驗證。

## 版本與相容性

刪除欄位、改型別、把 optional 改 required、改變 Enum 意義通常是破壞性變更。新增 optional 欄位通常較相容，但仍需確認嚴格解析器與快照測試。穩定欄位名稱及錯誤碼一旦發布，不應任意改義。

> [!note] 專案決策邊界
> 本專案 Route／Request／Response 只暴露 PublicId；可編輯資源回傳並要求帶回 Base64 `rowVersion`。完整欄位、上限及敏感資料禁則見 [[03-架構/02-API與前端契約/API DTO與Schema契約]]。

## 參考資料

- [Microsoft Learn：Create web APIs with ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/web-api/)
- [OpenAPI Specification](https://spec.openapis.org/oas/)
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
