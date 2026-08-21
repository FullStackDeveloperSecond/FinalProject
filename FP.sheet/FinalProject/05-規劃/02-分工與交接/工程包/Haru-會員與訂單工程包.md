---
文件狀態: 可開發
適用對象: haru
主要覆核: yinyin
最終整合: alex
---

# Haru｜會員、登入、訪客存取與訂單工程包

## 1. 你的責任與完成邊界

你負責把下列工作包從既有 Entity／資料庫基線推進到可操作的 Application Use Case、API、前後台頁面與測試證據：

| 優先級 | 工作包 | 範圍 |
|---|---|---|
| M | `M-01` | 會員註冊、Email 驗證、登入／登出、Lockout、忘記密碼與重設 |
| M | `M-01B` | 管理員登入、TOTP、Recovery Code、Session 撤銷 |
| M | `M-02` | 訪客查單驗證、限單 Cookie、訪客取消與退貨入口 |
| M | `M-08` | 結帳訂單、金額快照、狀態與後台訂單管理 |
| S，門檻後 | `S-01` | 會員收藏 |

`SH-05` 身分、授權與隱私共用基礎由 alex 負責。你實作會員與管理員登入 Use Case，但 Cookie、共用 Policy、PublicId、遮蔽與敏感操作授權若需要改動，先提出契約並交由 alex 確認，不能建立平行認證管線。

## 2. 開始前檢查

在 Repository 的 `FP.dev` 目錄執行：

```powershell
git switch dev
git pull --ff-only origin dev
git switch -c feature/member-auth

dotnet tool restore
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci
Set-Location ../admin-web
npm ci
Set-Location ../..
```

必要版本與服務：.NET SDK `10.0.303`、Node.js 24、npm 11、SQL Server 2025 Developer、Instance `.\SQL2025`、Database `DoSelectDb`。完整安裝與連線規則見 [[03-架構/01-系統與環境/本機開發環境與版本基線]]。

新電腦先明確套用既有 Migration，不要建立新 Migration：

```powershell
dotnet tool run dotnet-ef -- database update InitialCreate `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext
```

最小 Seed 帳號為 `member@doselect.local` 與 `admin@doselect.local`。密碼只放 .NET User Secrets 的 `Seed:MemberPassword`、`Seed:AdminPassword`；不得寫入文件、聊天、Commit 或終端歷史。可用 Visual Studio「管理使用者祕密」設定後執行：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/backend/DoSelect.Api --no-launch-profile -- --seed-minimal
```

目前 `configure-seed-secrets.ps1` 與 `seed-minimal-development-data.ps1` 含組長本機 `.NET` 絕對路徑，其他帳號不要直接依賴這兩支腳本；這是已知的共用腳本可攜性缺口，不要在你的功能 PR 順便修改。

啟動與健康檢查：

```powershell
.\scripts\start-all.ps1
.\scripts\status.ps1
.\scripts\health-check.ps1
```

固定網址：API `http://localhost:5126`、前台 `http://localhost:5173`、後台 `http://localhost:5174/admin/`。

首次 Clone 若沒有本機設定檔，將 `src/backend/DoSelect.Api/appsettings.Development.example.json` 複製為已忽略版控的 `appsettings.Development.json`，再依本機修改非機密的 `Storage:DataRoot`。不要提交該本機檔案。

目前認證整合尚未完成：`Program.cs` 還沒有正式 Cookie Authentication／`UseAuthentication()`，現有註冊只有 Identity Store。你可以先做 Domain、Application、DTO 與測試，但登入 Cookie、共用 Policy 與完整 401／403 整合必須等 alex 的 `SH-05` 基礎合併；不得用 Controller 內臨時判斷或 Vue Guard 假裝完成授權。

## 3. 權威規格閱讀順序

若文件互相衝突，不自行選一份實作，依下列順序判定並回報 alex：

1. [[03-架構/03-資料與一致性/資料字典索引]]、[[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]、[[03-架構/03-資料與一致性/資料字典-購物交易與售後]]。
2. [[03-架構/03-資料與一致性/狀態機設計]]、[[03-架構/02-API與前端契約/API Endpoint目錄]]、[[03-架構/02-API與前端契約/API DTO與Schema契約]]、[[03-架構/02-API與前端契約/API錯誤碼目錄]]。
3. [[02-領域需求/01-會員與身分/會員、驗證與通知]]、[[02-領域需求/90-驗收規格/會員購物與售後驗收規格]]、[[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]、[[01-需求/角色與權限]]。
4. [[03-架構/09-資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]]。

共通實作規則另讀 [[03-架構/01-系統與環境/系統架構]]、[[03-架構/02-API與前端契約/API共通規範]]、[[03-架構/03-資料與一致性/PublicId與資料完整性設計]]、[[03-架構/04-安全與檔案/安全與供應鏈強制驗收標準]]。

## 4. 現有程式落點

| 層 | 現有位置 | 你的使用方式 |
|---|---|---|
| Domain | `FP.dev/src/backend/DoSelect.Domain/Members/`、`Orders/` | 沿用封裝型 Entity；狀態只經具名方法轉移 |
| Identity | `FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Identity/ApplicationUser.cs` | 不另建會員帳號表或第二套 Identity Store |
| EF Mapping | `FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Configurations/Members/`、`Orders/` | 已納入初始 Migration；變更先列出 Schema 影響 |
| Application | `FP.dev/src/backend/DoSelect.Application/` | 新增公開 Use Case／Query／DTO；不得讓 Controller 直接寫 DbContext |
| API | `FP.dev/src/backend/DoSelect.Api/` | 商業端點使用 `[ApiController]` Controller，沿用 Problem Details |
| 前台 | `FP.dev/frontend/customer-web/src/` | `features/auth`、`members`、`orders`；Page 只協調狀態 |
| 後台 | `FP.dev/frontend/admin-web/src/` | `features/auth`、`orders`；完成 2FA 後才載入管理資料 |
| 測試 | `HaruEntityTests.cs`、`HaruPersistenceModelTests.cs` 與四個測試專案 | 在對應層補 Domain、Application、Infrastructure、API 測試 |

全案只有一個 `DoSelectDbContext` 與一條 Migration 歷程。不得建立模組 DbContext、直接讀其他模組 Repository，或在功能分支自行 scaffold／apply 新 Migration；有 Schema 變更時在日誌列出後交 alex 走 Migration Gate。

## 5. 你必須交付的 API 與頁面

API 以 [[03-架構/02-API與前端契約/API Endpoint目錄]] 為準，重點群組：

- `/api/v1/auth/*`、`/api/v1/members/me*`、`/api/v1/admin/auth/*`。
- `/api/v1/guest-orders/access-*`。
- `/api/v1/orders`、`/api/v1/orders/{id}`、取消 Action。
- `/api/v1/admin/orders*` 與用途限定的 recipient Endpoint。

前台 Page ID：`C-08`～`C-12`、`C-16`～`C-18`、`C-21`～`C-23`。後台 Page ID：`A-01`～`A-03`、`A-14`～`A-15`。Route、Guard、Server State 與共同錯誤狀態見 [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]。

Typed Client 尚未正式產生。第一批 Controller／DTO 穩定後通知 alex 依 [[03-架構/02-API與前端契約/OpenAPI與前端Client流程]] 產生；不要在 Vue 手寫第二套 DTO 或另建 fetch client。

## 6. 跨模組契約

| 對象 | 你提供 | 你取得 | 禁止事項 |
|---|---|---|---|
| terry | Order／OrderItem 唯讀摘要、會員或 Guest Scope 結果 | Cart、Reservation、Shipment、COD Eligibility | 直接讀 Terry Repository／表 |
| yinyin | Order 金額／付款期限／擁有者摘要 | Coupon、PaymentAttempt、Refund、Invoice 結果 | 由 Order Entity 自行改付款或退款狀態 |
| kafen | 訂單擁有權、可退明細與 Guest Scope | Return 狀態摘要 | 把 ReturnRequest 放進 Order Aggregate |
| alex | Current User、本人訂單授權案例、登入／同意案例 | Policy、Email、Outbox、Audit、共通安全能力 | 自建平行 Policy／寄信／AI 呼叫 |

對外一律使用 UUID v7 PublicId。私人訂單 API 必須在查詢入口限制 Actor Scope；不得先取出他人資料再由 DTO 或 Vue 隱藏。

## 7. 建議切片順序

1. Session、註冊、Email 驗證、登入／登出與 Lockout。
2. 密碼重設、會員 Profile／Address。
3. 管理員兩階段登入、TOTP 與 Recovery Code。
4. 訪客查單 Challenge、驗證、限單 Cookie 與負面授權測試。
5. 訂單查詢／後台投影；Checkout 建單需等 terry、yinyin 的正式契約後整合。
6. `S-01` 只有 S 功能啟動門檻通過後開始。

這是依賴順序，不變更你的正式分工；每個切片用獨立 Branch／PR。

## 8. 必要測試

至少覆蓋 [[03-架構/08-測試與驗收/M功能測試案例目錄]] 的 `UC-AUTH-01`～`03`、`UC-ADMIN-AUTH-01`、`UC-GUEST-ORDER-01`、`UC-ADM-ORDER-01`～`02`。其中登入、管理權限、訪客查單與私人訂單必須有 Actor A／B 越權拒絕，以及拒絕後無資料、無狀態變更、無副作用證據。

提交前在 `FP.dev` 執行：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

只要改前台或後台，也在對應資料夾執行 `npm run typecheck`、`npm run lint -- --max-warnings 0`、`npm test`、`npm run build`、`npm audit --omit=dev`。

## 9. PR、日誌與完成定義

- 遵守 [[Git協作規範]]：從最新 `dev` 建短分支、PR 回 `dev`、Squash Merge、不得自行合併。
- 每個 API、DTO、Enum、錯誤碼、Policy、狀態轉移、資料庫或跨模組契約變更，都依 [[日誌/README]] 複製 [[日誌/日誌]] 建立同 PR 日誌。
- PR 說明列出做了什麼、如何測試、API／資料庫／跨模組影響，以及 yinyin 的覆核證據。
- 功能完成必須同時具備：成功流程、驗證／授權／併發失敗流程、前端 Loading／Empty／Error、測試通過、日誌可讓 yinyin 接手。

遇到下列情況停止實作並詢問 alex：需要修改共用 Identity／Policy、既有 Entity 欄位或 Migration；正式文件互相衝突；需要新增套件；跨模組缺少 Query／DTO；安全規則或錯誤碼未登錄。
