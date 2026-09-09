# 後端與 API 核心

## 必會觀念

### ASP.NET Core 請求管線

Middleware 依註冊順序處理請求與回應，適合 Correlation ID、例外轉換、HTTPS、CORS、Authentication 與 Rate Limiting。Filter 進入 MVC 後運作，適合 Controller/Action 層的 Antiforgery 或輸入規則。兩者不能只因「都能攔截」就互換。

本專案典型順序包含 request observability、API foundation、CORS、HTTPS（依環境）、Authentication、特定路由 scheme 解析、Authorization、Rate Limiter，再映射 Controllers、health 與 Hangfire dashboard。

### DI lifetime

- Singleton：全程一份；不可直接持有 scoped `DbContext`，且必須 thread-safe。
- Scoped：每個 request 一份；`DbContext` 與多數用例服務在此層。
- Transient：每次解析建立，適合輕量無狀態物件。

要能回答 captive dependency、thread safety 與為何 `DbContext` 通常 scoped。

### async/await

資料庫、HTTP、SMTP 是 I/O-bound，非同步能在等待時歸還 thread，但不會自動讓 CPU 工作更快。`CancellationToken` 應沿鏈傳遞；不要吞掉取消或未知例外。

### DTO、Entity 與契約

Entity 表示持久化／領域狀態，DTO 是對外契約。直接回 Entity 容易過度揭露、循環序列化與資料庫結構綁定。DoSelect 由 OpenAPI 產生 TypeScript schema；產生檔不可手改，認證、Antiforgery、Correlation ID 與錯誤處理放 wrapper。

### HTTP 與錯誤語意

| 狀態 | 面試說法 |
|---|---|
| 400 | 格式或驗證錯誤 |
| 401 | 尚未通過身分驗證 |
| 403 | 已驗證但權限不足 |
| 404 | 不存在；有時也避免洩漏資源存在性 |
| 409 | 併發、狀態或冪等 payload 衝突 |
| 429 | 超過限流，尊重 `Retry-After` |
| 500 | 未預期伺服器錯誤 |
| 503 | 依賴尚未 ready 或暫時不可用 |

Problem Details 統一 `status`、`code`、`detail`、欄位錯誤、`traceId`、`correlationId`，前端按穩定 code 處理，不解析人類文字。

## 高機率題

### `IEnumerable` 與 `IQueryable`？

`IQueryable` 保存 expression tree，EF Core 可翻成 SQL；切成 `IEnumerable` 後通常在記憶體執行。篩選、投影、排序與分頁前過早 materialize 會多載資料。

### `record` 與 `class`？

DTO、command、result 等重視值語意與不可變性的載體適合 `record`；有身分、生命週期與可變狀態的 Entity 通常用 `class`。仍要注意集合的深層可變性。

### Application 為何不直接使用 `DbContext`？

讓用例依賴能力介面，資料庫與外部服務留在 Infrastructure，降低耦合並方便測試。代價是介面與映射增加，簡單查詢不必為形式堆抽象。

### OpenAPI client 為何不能手改？

重新產生會覆蓋手改，也會掩蓋 server contract 漂移。客製行為放 wrapper；CI 重產並比較 diff，契約改了卻未提交產物就失敗。

### 如何避免洩漏內部例外？

全域處理把未知錯誤轉通用 Problem Details，server 以 trace/correlation 記細節；不回 stack、SQL、秘密。可預期領域錯誤明確映射成 400/404/409。

## 現場追問

1. Middleware 順序錯誤如何影響 CORS preflight 或 Authentication？
2. Singleton 注入 `DbContext` 會怎樣？
3. Mutation 為何不預設自動 retry？
4. PATCH 與 PUT 語意差異？如何查證專案實作？
5. OpenAPI nullable 與後端驗證不一致，應改哪裡？

## 專案證據入口

- `FP.dev/src/backend/DoSelect.Api/Program.cs`
- `FP.dev/src/backend/DoSelect.Api/Common`
- `FP.dev/frontend/shared/src/api/client.ts`
- `FP.dev/frontend/shared/src/api/errors.ts`
