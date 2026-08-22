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

- .NET SDK 10.0.303；由 `global.json` 精確鎖定，其他 Patch／Feature Band 皆不替代。
- Node.js 24 LTS；由 `.nvmrc` 記錄 Major。
- npm 11。
- SQL Server 2025 Developer Edition；本機資料庫固定使用 `DoSelectDb`。
- SQL Server Command Line Utilities／ODBC Driver 18；共用腳本會優先使用 ODBC 18 的 `sqlcmd.exe`，再回退 PATH。

## 第一次安裝

```powershell
dotnet tool restore
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci

Set-Location ../admin-web
npm ci
```

`dotnet tool restore` 會依 Repository 內 `.config/dotnet-tools.json` 還原固定版 `dotnet-ef 10.0.10`，不依賴每位成員的全域工具版本。

## 建置與測試

在本目錄執行後端驗證：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

Idempotency／CartMergeConflict 的 provider-backed 測試在 Windows 非 CI 環境預設使用一次性 `DoSelectIdempotencyTests` 資料庫與 `.\SQL2025`，測試結束會刪除該資料庫。CI 或非 Windows 環境沒有 SQL Server 時會明確 Skip；若 Runner 已準備專用 SQL Server，使用 Secret 設定 `DOSELECT_SQLSERVER_TEST_CONNECTION` 即可啟用，不得指向 `DoSelectDb`。

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

啟動前會驗證 `dotnet`、Node、npm、`sqlcmd`、SQL Server `\.\SQL2025` Windows Authentication 與三個固定 Port。SQL 檢查優先使用 ODBC 18 工具並以 `-C` 對齊本機 `TrustServerCertificate=True` 基線，避免 PATH 中舊 ODBC 17 工具造成錯誤判定。PID、程序啟動時間及 stdout／stderr 保存在已忽略版控的 `.run/`；停止腳本只終止身分與啟動時間吻合的本專案程序，不停止 SQL Server，也不批次終止電腦上的其他 Node／.NET 程序。

若固定 Port 已被占用，腳本會停止並顯示占用 PID，不會自動改用其他 Port。

`health-check.ps1` 不依賴 PID 判斷，會直接驗證 SQL Server、API Liveness／Readiness、消費者前台與管理後台；必要檢查失敗時回傳非零結束碼。Readiness 的 `Degraded` 代表可降級的外部依賴異常，基本服務仍可用，因此會警告但不視為整體失敗。Hangfire、OpenAI 與 Brevo 的實際檢查會在對應 Infrastructure 完成後加入，不以假檢查宣稱就緒。

### Brevo SMTP 展示設定

API 專案已設定 `UserSecretsId`。Brevo SMTP Username／SMTP Key 只由目前 Windows 使用者在本機終端輸入，不得寫入命令紀錄、聊天、Issue、文件或 Repository：

```powershell
.\scripts\configure-brevo-secrets.ps1
.\scripts\test-brevo-smtp.ps1
```

設定腳本固定使用 Brevo relay `smtp-relay.brevo.com:587`、STARTTLS 與已驗證單一寄件者 `alex <alexyang920528@gmail.com>`。測試腳本只寄送一封不含會員資料或 Token 的純文字驗證信到已確認的同一地址；SMTP 接受後仍需在收件匣與 Brevo Transactional Log 確認最終投遞。

正式應用由 Application `IEmailSender` 抽象使用 Email；Email 關閉時採明確回傳 `Suppressed` 的本機實作，啟用時採 MailKit Brevo SMTP Adapter。Adapter 每次只做一次傳輸並將結果分類為 `Sent`、`TransientFailure` 或 `PermanentFailure`；Outbox、三次遞增退避、模板、站內通知與人工重送仍由 `SH-07`／`SH-08` 隨正式資料模型及 Hangfire Consumer 實作，不可在商業交易內直接呼叫 SMTP。

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

會呼叫共用 Idempotency Executor 的功能必須以 User Secrets 或部署環境設定 `Idempotency:ActorScopePepper`；值至少 32 UTF-8 bytes，且不得寫入範例設定、Repository、Log 或資料庫。尚未呼叫冪等命令的 Fresh Clone 可不設定；第一次測試購物車合併、建立訂單、退款等冪等端點前必須完成設定。

健康檢查：

- `GET /health/live`：確認 API 程序可處理請求。
- `GET /health/ready`：確認本機 `Storage:DataRoot` 可寫，並透過 EF Core 對 `DoSelectDb` 執行最小 `SELECT 1` 讀取；Hangfire 檢查待其 Infrastructure 完成後加入。
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

- 已完成 Solution、專案參考、共用建置設定、套件鎖版、兩個 Vue 應用、Vue 共用 API／Query／狀態基礎、API 共通錯誤／驗證管線、單一 `DoSelectDbContext`、SQL Server Provider、Identity Store 與固定版 `dotnet-ef`。
- 四位 Owner 的正式業務 Entity／Fluent Configuration、跨模組 FK 與初始 `InitialCreate` Migration 已完成；本機 `DoSelectDb` 已套用並驗證 93 張資料表、315 個索引、`vw_CaseWorkbench` 12 欄契約與 Migration History。
- 已提供不自動執行的最小開發 Seed、User Secrets 密碼設定、SQL 驗證及 API SQL Readiness smoke script；10,000 筆完整展示資料產生器、認證授權流程與 Application 交易 Use Case 仍待後續實作。
- PrimeVue 已納入相依套件，但主題與實際元件由畫面設計工作包導入。
- OpenAPI TypeScript Client 的流程與共用 generic client factory 已建立；待商業 API 契約加入後再產生 `schema.d.ts` 並建立實際 typed client instance。

## EF Core 工具

全案固定使用單一 `DoSelectDbContext` 與 `DoSelect.Infrastructure` Migration Assembly。Entity／Configuration 依模組分資料夾；不得建立每模組獨立 DbContext 或 Migration。

在不建立或更新資料庫的前提下檢查工具與 Context：

```powershell
dotnet tool restore
dotnet tool run dotnet-ef -- dbcontext info `
  --project src/backend/DoSelect.Infrastructure/DoSelect.Infrastructure.csproj `
  --startup-project src/backend/DoSelect.Infrastructure/DoSelect.Infrastructure.csproj `
  --context DoSelectDbContext `
  --no-build
```

四份 Schema、Entity／Configuration、第一輪跨模組 Review 與 `20260819013357_InitialCreate` 已完成。Migration 建立 93 張應用／Identity 資料表、315 個索引及 `vw_CaseWorkbench`，Review SQL 位於 `database-deploy/initial-create/InitialCreate.review.sql`。本機 `DoSelectDb` 已套用並由 `database-deploy/initial-create/verify.sql` 驗證通過；API 啟動仍不得呼叫 `Database.Migrate()`／`MigrateAsync()`。

新開發環境需由開發者明確套用 Migration：

```powershell
dotnet tool run dotnet-ef -- database update InitialCreate `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext
```

建立最小開發資料時，先以互動腳本將兩組密碼存入 .NET User Secrets，再明確執行 Seed；重跑不會重設既有密碼、關閉已啟用的 TOTP 或建立重複資料：

```powershell
.\scripts\configure-seed-secrets.ps1
.\scripts\seed-minimal-development-data.ps1
```

驗證資料庫結構、最小 Seed 與 API 實際讀取：

```powershell
sqlcmd -S .\SQL2025 -d DoSelectDb -E -C -b -i database-deploy\initial-create\verify.sql
sqlcmd -S .\SQL2025 -d DoSelectDb -E -C -b -i database-deploy\initial-create\verify-minimal-seed.sql
.\scripts\smoke-api-database.ps1
```
