---
type: knowledge
title: OpenAPI 與 Typed Client
aliases:
  - OpenAPI typed client
  - openapi-typescript
  - openapi-fetch
tags:
  - 知識點
  - OpenAPI
  - TypeScript
  - API契約
  - 前端
created_at: 2026-08-13
related:
  - "[[03-架構/OpenAPI與前端Client流程]]"
  - "[[03-架構/API DTO與Schema契約]]"
  - "[[知識點/DTO與API Schema]]"
  - "[[知識點/Problem Details]]"
---

# OpenAPI 與 Typed Client

## 核心概念

OpenAPI 是描述 HTTP API 路徑、參數、Request／Response、錯誤及資料 Schema 的機器可讀契約。`openapi-typescript` 可把契約產生為 TypeScript 型別，`openapi-fetch` 再利用這些型別提供 typed fetch client。

```text
ASP.NET Core API
→ 匯出 OpenAPI JSON
→ openapi-typescript 產生 schema.d.ts
→ openapi-fetch 在編譯期檢查路徑、參數與回應
```

這能降低前後端各自手寫介面而漂移的風險，但型別安全只涵蓋契約描述的內容；授權、商業規則及執行期外部資料仍需後端驗證。

## 為何產生碼不可手改

產生檔是 OpenAPI 的衍生物，不是另一份真實來源。手改後下一次重新產生就會消失，也會掩蓋真正的後端契約缺漏。

正確修正位置：

1. 修改後端 DTO、Endpoint Metadata 或 OpenAPI 設定。
2. 重新匯出 OpenAPI。
3. 重新產生 TypeScript 型別。
4. 修正因契約變更而出現的 Typecheck 錯誤。

產生檔可提交 Git 供審查與建置重現，但檔頭應標明 generated／do not edit。

## Wrapper 的責任

共用 wrapper 或 middleware 處理跨 Endpoint 的執行期行為：

- `credentials: "include"`。
- 取得及附加 Anti-forgery Header。
- 產生或轉送 Correlation ID。
- 解析 `application/problem+json` 與穩定錯誤碼。
- 對 `401`、`403`、`409`、`429` 提供一致事件。

它不應包含 Vue Router 導向、Toast 文案或特定頁面狀態；這些屬於 UI 層。也不可自動重送非冪等命令。

## 契約變更閘門

API Contract、DTO 或 Controller 改動時，CI 應重新匯出與產生，確認 Git Diff 已同步提交，再分別 Typecheck 前台及後台。CI 不應自動 Commit 產生碼，否則未經人工審查的 API 變更會直接進入分支。

> [!note] 專案決策邊界
> 專案已確認使用 `openapi-typescript`＋`openapi-fetch`，前後台共用產生型別與 wrapper；產生檔禁止手改。正式目錄責任與 CI 流程見 [[03-架構/OpenAPI與前端Client流程]]。

## 參考資料

- [OpenAPI Specification](https://spec.openapis.org/oas/)
- [OpenAPI TypeScript：openapi-fetch Middleware & Auth](https://openapi-ts.dev/openapi-fetch/middleware-auth)
- [[03-架構/API共通規範]]
