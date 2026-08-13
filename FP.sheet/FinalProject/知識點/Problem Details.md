---
type: knowledge
title: Problem Details
aliases:
  - RFC Problem Details
  - application/problem+json
tags:
  - 知識點
  - API
  - 錯誤處理
  - ASP.NET Core
created_at: 2026-08-13
related:
  - "[[03-架構/API共通規範]]"
  - "[[03-架構/API錯誤碼目錄]]"
  - "[[知識點/Correlation ID與Trace ID]]"
---

# Problem Details

## 它解決什麼問題

Problem Details 是 HTTP API 的標準錯誤文件格式。它讓前端不必為每個 Endpoint 猜測不同的錯誤形狀，也保留 HTTP Status 的原始語意。

常見欄位包括 `type`、`title`、`status`、`detail`、`instance`；應用程式可加入延伸欄位，例如穩定 `code`、`traceId` 與欄位 `errors`。

```json
{
  "type": "https://example.invalid/problems/validation",
  "title": "Validation failed",
  "status": 400,
  "code": "validation_failed",
  "traceId": "...",
  "errors": { "email": ["Email 格式不正確"] }
}
```

## HTTP Status 與應用程式 Code

HTTP Status 表達通用類別，例如 400、401、403、409；`code` 表達前端可穩定處理的業務原因，例如 `inventory_insufficient`。前端應依 `code` 對應語系與流程，不依中文訊息做字串比較。

同一個 `code` 發布後不得改變意義或回收給其他錯誤。錯誤目錄應同時定義 Status、安全訊息與是否可重試。

## 安全界線

外部錯誤不得包含 Stack Trace、SQL、連線字串、Token、Cookie 或內部例外訊息。完整例外寫入受保護 Log，以 `traceId`／Correlation ID 串接查找。資源不存在與無權限有時應共同回 404，避免洩漏資源是否存在。

> [!note] 專案決策邊界
> 專案已確認所有可處理錯誤使用 Problem Details，加上穩定 `code`、`traceId` 與欄位 `errors`；正式對照見 [[03-架構/API錯誤碼目錄]]。

## 參考資料

- [Microsoft Learn：Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0)
- [RFC 9457：Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457)
