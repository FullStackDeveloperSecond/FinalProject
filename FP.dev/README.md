# DoSelect 懂選｜開發專案

本目錄是 DoSelect 的可執行程式碼根目錄。系統採模組化單體 ASP.NET Core Web API，並由消費者前台與管理後台兩個獨立 Vue 應用共用同一個 API。

## 目錄

```text
DoSelect.slnx
src/backend/
  DoSelect.Api/
  DoSelect.Application/
  DoSelect.Domain/
  DoSelect.Infrastructure/
frontend/
  customer-web/
  admin-web/
tests/
  DoSelect.Domain.Tests/
  DoSelect.Application.Tests/
  DoSelect.Infrastructure.Tests/
  DoSelect.Api.IntegrationTests/
```

## 必要環境

- .NET SDK 10.0.302；由 `global.json` 鎖定 Feature Band。
- Node.js 24 LTS；由 `.nvmrc` 記錄 Major。
- npm 11。
- SQL Server 2025 Developer Edition；資料庫實作開始後使用 `DoSelectDb`。

## 第一次安裝

```powershell
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci

Set-Location ../admin-web
npm ci
```

## 建置與測試

在本目錄執行後端驗證：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

在 `frontend/customer-web` 與 `frontend/admin-web` 分別執行前端驗證：

```powershell
npm run typecheck
npm run lint -- --max-warnings 0
npm test
npm run build
npm audit --omit=dev
```

## 開發啟動

API：

```powershell
dotnet run --project src/backend/DoSelect.Api
```

前台或後台：

```powershell
npm run dev
```

Development 環境提供 `/openapi/v1.json` 與 Scalar 互動式 API 文件；非 Development 環境不映射這兩個端點。

第一次啟動前可將 `src/backend/DoSelect.Api/appsettings.Development.example.json` 複製為未追蹤的 `appsettings.Development.json`，再依本機環境調整非敏感設定；OpenAI 與 SMTP Secret 使用 .NET User Secrets 或環境變數，不得填入範例檔。AI 與 Email 預設停用，因此 Fresh Clone 不需要 Secret 即可啟動；若明確啟用但缺少必要 Key，API 會在啟動時失敗。

健康檢查：

- `GET /health/live`：確認 API 程序可處理請求。
- `GET /health/ready`：目前確認本機 `Storage:DataRoot` 可寫；SQL Server、Migration 與 Hangfire 檢查待其 Infrastructure 完成後加入。
- 公開回應只包含 `status`，不輸出實體路徑、連線資訊或例外。

Serilog 會將結構化 JSON 輸出到 Console，並在 `{Storage:DataRoot}/logs` 建立每日 Rolling File；單檔 100 MB、最長保存 14 天且最多 20 個檔案。可在測試設定 `Observability:FileLoggingEnabled=false` 停用檔案輸出。

API 共通管線已提供：

- `X-Correlation-ID` Request／Response Header；缺少或不合法時由 API 產生。
- RFC Problem Details，固定包含穩定 `code`、`traceId` 與 `correlationId`。
- `[ApiController]` DataAnnotations／Model Binding 驗證失敗的統一 400 回應。
- 全域未處理例外安全轉換與無 Body HTTP 錯誤的 Problem Details 回應。
- Serilog 結構化 Request Log，包含 Path Template、狀態碼、耗時、Correlation ID、Trace ID 與使用者類型。
- 啟動時條件式設定驗證；錯誤只列 Configuration Key，不輸出 Secret 值。

## 目前邊界

- 已完成 Solution、專案參考、共用建置設定、套件鎖版、兩個 Vue 應用、API 共通錯誤／驗證管線及最小測試基線。
- 尚未加入業務模組、EF Core Entity、DbContext、Migration、Seed、認證授權或資料庫連線。
- PrimeVue 已納入相依套件，但主題與實際元件由畫面設計工作包導入。
- OpenAPI TypeScript Client 的流程已定義；待商業 API 契約加入後再產生實際 Client。
