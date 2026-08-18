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
  shared/
  customer-web/
  admin-web/
tests/
  DoSelect.Domain.Tests/
  DoSelect.Application.Tests/
  DoSelect.Infrastructure.Tests/
  DoSelect.Api.IntegrationTests/
evals/ai/v1/
  dataset.zh-TW.v1.jsonl
  context-fixtures.v1.json
  grader-contract.v1.json
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

`frontend/shared` 是兩個 Vue 應用透過本機 npm dependency 共用的來源套件；不需另外安裝。customer-web 的 `npm run lint` 會同時檢查共享來源，兩個應用的 Typecheck、Test 與 Build 都會實際解析共享套件。

兩個 Vue 應用預設呼叫 `http://localhost:5126`。需要覆寫時，將各自的 `.env.example` 複製為未追蹤的 `.env.local`，再設定非機密的 `VITE_API_BASE_URL`；值必須是沒有帳密、Query 或 Fragment 的絕對 HTTP／HTTPS URL。

## 開發啟動

建議在 `FP.dev` 執行一鍵腳本。預設使用 `Development`，固定網址為 API `http://localhost:5126`、消費者前台 `http://localhost:5173`、管理後台 `http://localhost:5174/admin/`：

```powershell
.\scripts\start-all.ps1
.\scripts\status.ps1
.\scripts\health-check.ps1
.\scripts\stop-all.ps1
```

展示環境需明確指定：

```powershell
.\scripts\start-all.ps1 -Environment Demo
```

啟動前會驗證 `dotnet`、Node、npm、`sqlcmd`、SQL Server `\.\SQL2025` Windows Authentication 與三個固定 Port。PID、程序啟動時間及 stdout／stderr 保存在已忽略版控的 `.run/`；停止腳本只終止身分與啟動時間吻合的本專案程序，不停止 SQL Server，也不批次終止電腦上的其他 Node 或 .NET 程序。

若固定 Port 已被占用，腳本會停止並顯示占用 PID，不會自動改用其他 Port。

`health-check.ps1` 不依賴 PID 判斷，會直接驗證 SQL Server、API Liveness／Readiness、消費者前台與管理後台；必要檢查失敗時回傳非零結束碼。Readiness 的 `Degraded` 代表可降級的外部依賴異常，基本服務仍可用，因此會警告但不視為整體失敗。Hangfire、OpenAI 與 Brevo 的實際檢查會在對應 Infrastructure 完成後加入，不以假檢查宣稱就緒。

### Brevo SMTP 展示設定

API 專案已設定 `UserSecretsId`。Brevo SMTP Username／SMTP Key 只由目前 Windows 使用者在本機終端輸入，不得寫入命令紀錄、聊天、Issue、文件或 Repository：

```powershell
.\scripts\configure-brevo-secrets.ps1
.\scripts\test-brevo-smtp.ps1
```

設定腳本固定使用 Brevo relay `smtp-relay.brevo.com:587`、STARTTLS 與已驗證單一寄件者 `alex <alexyang920528@gmail.com>`。測試腳本只寄送一封不含會員資料或 Token 的純文字驗證信到已確認的同一地址；SMTP 接受後仍需在收件匣與 Brevo Transactional Log 確認最終投遞。此腳本只驗證展示帳號與 SMTP 設定，正式 `IEmailSender`、Outbox、重試及失敗處理由 `SH-07` 實作。

### 個別啟動

API：

```powershell
dotnet run --project src/backend/DoSelect.Api
```

前台或後台：

```powershell
npm run dev
```

Development 環境提供 `/openapi/v1.json` 與 Scalar 互動式 API 文件；非 Development 環境不映射這兩個端點。

## AI 評估資料集

第一版繁中資料集固定 120 筆，完整格式、分組、Fixture、Grader 與審核邊界見 `evals/ai/v1/README.md`。本機與 CI 的 deterministic 檢查不呼叫 OpenAI：

```powershell
node .\scripts\build-ai-eval-dataset.mjs --check
node .\scripts\validate-ai-eval-dataset.mjs
```

只有修改 `cases-source.mjs` 後才執行不含 `--check` 的產生指令；產生檔必須與來源一起提交。Live baseline 必須等待 Prompt、Schema、Adapter 與明確成本核准，不得由一般 PR 自動呼叫。

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

- 已完成 Solution、專案參考、共用建置設定、套件鎖版、兩個 Vue 應用、Vue 共用 API／Query／狀態基礎、API 共通錯誤／驗證管線及最小測試基線。
- 尚未加入業務模組、EF Core Entity、DbContext、Migration、Seed、認證授權或資料庫連線。
- PrimeVue 已納入相依套件，但主題與實際元件由畫面設計工作包導入。
- OpenAPI TypeScript Client 的流程與共用 generic client factory 已建立；待商業 API 契約加入後再產生 `schema.d.ts` 並建立實際 typed client instance。
