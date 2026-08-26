---
文件狀態: 已確認
最後更新: 2026-08-20
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

frontend/shared/src/
├─ api/
│  ├─ generated/schema.d.ts    # openapi-typescript 產生；禁止手改，商業契約形成後加入
│  ├─ client.ts                # openapi-fetch generic client 與 HTTP middleware
│  ├─ antiforgery.ts           # 分前後台 Scheme 的記憶體 Token Provider
│  ├─ correlation-id.ts        # Correlation ID 產生與格式檢查
│  └─ errors.ts                # Problem Details、穩定錯誤碼與追蹤識別
├─ query/                      # TanStack Query 共通重試與快取基線
└─ components/                 # Loading／Empty／Error／HTTP Status 共用狀態
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

## 目前實作狀態

- `frontend/shared` 已建立為兩個 Vue 應用共用的本機 npm package。
- 共用 generic client 已固定 `credentials: include`、合法 Correlation ID、Anti-forgery Token Provider、Problem Details 解析與 `onApiError` 回呼；Query 僅對網路或 5xx 查詢失敗重試一次，Mutation 不自動重試。
- customer-web 與 admin-web 已由 `VITE_API_BASE_URL` 建立各自的 generic client factory；預設本機 API 為 `http://localhost:5126`。兩者分別以 `X-DoSelect-Client: member`／`admin` 取得只存在記憶體的 Token，並提供 Session 改變後清除 Token 的函式。
- 正式 `schema.d.ts`、`api:export`、`api:generate`、`api:check` 與 CI Contract Diff 已隨 PR #24（第一批商業 Controller／DTO：Catalog API）加入。CI 的「Verify committed OpenAPI client is current」步驟啟動真實 API、對 `/openapi/v1.json` 輪詢直到就緒，再執行 `npm run api:check --prefix frontend/customer-web`（即本文件「產生流程」的匯出→產生→Diff 三步驟）；這是目前唯一接入 required CI 的 `api:check` 呼叫路徑。`frontend/shared` 也有一份同名 `api:check`（供本機開發者在 `frontend/shared` 目錄下直接執行），內容與 customer-web 版本一致（先 export、generate，再 diff），但不是 CI 實際執行的那一份，只是本機便利指令。
- SH-05 已建立 Token Endpoint、會員／管理員 Cookie Scheme 選擇、記憶體 Provider 與 unsafe method 全域驗證；各登入／登出 Use Case 合併時必須呼叫對應前端的 `resetAntiforgeryToken()`，不得讓登入前 Token 跨 Session 沿用。
