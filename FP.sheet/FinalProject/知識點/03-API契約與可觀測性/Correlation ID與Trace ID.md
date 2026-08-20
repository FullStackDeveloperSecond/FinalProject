---
type: knowledge
title: Correlation ID 與 Trace ID
aliases:
  - Correlation ID
  - Trace ID
  - traceparent
tags:
  - 知識點
  - 可觀測性
  - Logging
  - 分散式追蹤
created_at: 2026-08-13
related:
  - "[[03-架構/05-背景工作與維運/Logging與HealthCheck設計]]"
  - "[[03-架構/02-API與前端契約/API共通規範]]"
  - "[[知識點/03-API契約與可觀測性/Problem Details]]"
---

# Correlation ID 與 Trace ID

## 兩者的差異

Correlation ID 是應用程式用來把同一業務流程的 HTTP Request、Outbox、Hangfire Job、Email 或外部呼叫關聯起來的識別碼。Trace ID 則是分散式追蹤中的一條 Trace 識別；一條 Trace 由多個 Span 組成。

兩者可以相同，也可以分工：

```text
Correlation ID：一個訂單建立流程及其後續通知
Trace ID：這次 HTTP／背景執行的技術呼叫鏈
```

業務流程跨越數分鐘或重試時，可能保留同一 Correlation ID，但每次執行產生新的 Trace／Span。

## 傳遞規則

W3C Trace Context 以 `traceparent`／`tracestate` 傳播追蹤內容。自訂 Correlation Header 應限制長度與字元，不能無條件信任外部輸入；無效時由伺服器產生新值。

應把關聯值寫入：

- 結構化 Request Log。
- Problem Details 的 `traceId`。
- AuditLog、OutboxMessages 與背景 Job 參數。
- 外部服務呼叫的安全 Metadata（服務允許時）。

它不是 Secret，也不是授權憑證；知道 ID 不應取得額外資料。

## 常見錯誤

- 每一層各自產生新 ID，導致鏈路斷裂。
- 把完整 URL Query、Email 或 Token 塞進關聯欄位。
- 只記錄成功 Request，錯誤時反而無法追查。
- 把 Correlation ID 當作冪等鍵；兩者目的與生命週期不同。

> [!note] 專案決策邊界
> 本專案共用 fetch wrapper 產生或轉送 Correlation ID；HTTP、Outbox、Hangfire、Email、OpenAI 與 Log 必須延續關聯，正式欄位見 [[03-架構/05-背景工作與維運/Logging與HealthCheck設計]]。

## 參考資料

- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [Microsoft Learn：Distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
