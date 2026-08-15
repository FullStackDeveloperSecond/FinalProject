---
文件狀態: 已確認
最後更新: 2026-08-14
追蹤項目:
  - TECH-03
---

# OpenAPI 與前端 Client 流程

## 選型

- API 專案採 ASP.NET Core Web API Controller 範本建立：`dotnet new webapi --use-controllers`。
- 商業 API 以 Controller 與 `[ApiController]` 實作，不以 Minimal API 作為主要端點形式。
- 使用 `Microsoft.AspNetCore.OpenApi` 產生第一方 OpenAPI 文件，預設 JSON 位址為 `/openapi/v1.json`。
- 使用 `Scalar.AspNetCore` 提供互動式 API 文件介面。
- OpenAPI JSON 與 Scalar 介面只在 `Development` 環境啟用。
- 不安裝 Swagger UI，避免同時維護兩套互動式文件介面。
- `openapi-typescript` 將 OpenAPI Schema 轉成 TypeScript 型別。
- `openapi-fetch` 提供 typed fetch client。
- 前台與後台使用同一份產生型別及共用 wrapper；產生檔不可手動修改。
- 套件精確版本由 lock file 固定，升級必須重新產生、Typecheck 並通過契約 Diff。

## API 啟動設定基準

- 服務註冊：`AddControllers()`、`AddOpenApi()`。
- 端點映射：`MapControllers()`。
- 僅在 `app.Environment.IsDevelopment()` 成立時映射 `MapOpenApi()` 與 `MapScalarApiReference()`。
- Scalar 讀取 `/openapi/v1.json`，不得另建人工維護的第二份 API 規格。
- 健康檢查、OpenAPI 等框架型基礎設施端點可使用框架提供的映射方法；商業領域端點仍使用 Controller。

## 目錄責任

```text
contracts/
└─ openapi.v1.json             # API 匯出的固定契約

frontend/shared/api/
├─ generated/schema.d.ts       # openapi-typescript 產生；禁止手改
├─ client.ts                   # openapi-fetch 初始化
├─ http-wrapper.ts             # Credentials、CSRF、Correlation ID、Problem Details
└─ errors.ts                   # 穩定業務錯誤碼轉換
```

實際 Solution 建立時可以調整根目錄，但四項責任不可混合進 Vue Component 或 Pinia Store。

## 產生流程

```text
建置 API
→ 以 Development/Contract 環境匯出 openapi.v1.json
→ 驗證 OpenAPI 文件存在且非空
→ openapi-typescript 產生 schema.d.ts
→ customer-web Typecheck
→ admin-web Typecheck
→ 檢查 Git Diff
```

建議 npm scripts 名稱固定為：

```text
api:export
api:generate
api:check
typecheck
```

實際指令需在 Solution 建立後依專案路徑落實；命名變更必須同步 CI 與本機說明。

## Wrapper 邊界

共用 wrapper 負責：

- `credentials: include`。
- 取得並附加 Anti-forgery Header。
- 產生或轉送 Correlation ID。
- 解析 `application/problem+json`。
- 將穩定 `code` 交給前端語系資源，不依中文訊息判斷流程。
- 遇到 `401`、`403`、`409`、`429` 時提供一致事件，不自行重送非冪等命令。

產生檔不得負責登入導向、Toast、重試、Vue Router 或 Pinia 狀態。

## CI 閘門

PR 中只要 API Contract、Controller、DTO 或 OpenAPI 設定改動，就執行：

1. 匯出固定 OpenAPI。
2. 重新產生 TypeScript 型別。
3. `git diff --exit-code` 必須為空，確保契約及產生檔同步提交。
4. 兩個 Vue 專案分別執行 Typecheck。
5. 任一步失敗即禁止合併。

不得在 CI 自動 Commit 產生檔，避免未審核契約直接進入分支。
