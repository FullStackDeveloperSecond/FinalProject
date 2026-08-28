---
文件狀態: 可開發
最後更新: 2026-08-28
適用對象: terry
主要覆核: kafen
最終整合: alex
---

# Terry｜商品、購物、庫存、物流、組裝與報表工程包

## 1. 你的責任與完成邊界

| 優先級 | 工作包 | 範圍 |
|---|---|---|
| M | `M-03`～`M-06` | 商品／SKU／目錄、批次與 Excel、一般搜尋、購物車 |
| M | `M-10`～`M-11` | 庫存保留／併發／逾時、物流／運費／門市／批次出貨 |
| M | `M-15`～`M-17` | 七個營運報表、自由組裝、六類相容性規則與後台 |
| S，門檻後 | `S-02` | 已購買評價與審核 |
| 整合 | `INT-04` | 退貨、退款、發票折讓、庫存與報表金額一致性 |

你也要向 alex 的 AI 模組提供受控的商品、SKU、庫存、組裝、相容性與營運報表 Application Query／DTO，並做領域覆核；不要實作 OpenAI 呼叫、Prompt 或 AI 額度。

## 2. 開始前檢查

在 `FP.dev` 執行：

```powershell
git switch dev
git pull --ff-only origin dev
git switch -c feature/catalog-search
dotnet tool restore
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci
Set-Location ../admin-web
npm ci
Set-Location ../..
```

必要版本、SQL Server `.\SQL2025`／`DoSelectDb`、Migration 套用、最小 Seed 與啟動方式完全依 `FP.dev/README.md` 與 [[03-架構/01-系統與環境/本機開發環境與版本基線]]。新電腦須套用目前 `dev` 的完整已審查 Migration，不得自行建立新 Migration。

```powershell
dotnet tool run dotnet-ef -- database update `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext

.\scripts\start-all.ps1
.\scripts\health-check.ps1
```

Migration 名稱與數量會隨整合變動，不再複製到個人工程包。唯一來源是 `FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Migrations/` 與 `DoSelectDbContextModelSnapshot.cs`；每次先同步最新 `dev`，再由不指定 Migration 名稱的命令套用完整歷程。不得只停在特定 Migration，也不得在功能分支自行 scaffold／apply 新 Migration；Schema 需求交 alex 走 Migration Gate。實作進度統一查看 [[05-規劃/01-時程與進度/M功能實作矩陣]]。
如需最小帳號／型錄 Seed，先以 Visual Studio 管理 User Secrets 的 `Seed:MemberPassword`、`Seed:AdminPassword`，再使用 PATH 中的 `dotnet` 執行 `DoSelect.Api --seed-minimal`。現有兩支 Seed PowerShell 腳本含組長本機 `.NET` 絕對路徑，其他帳號不要直接依賴，也不要在商品 PR 順便修正。

首次 Clone 若沒有 `appsettings.Development.json`，由同目錄的 `.example.json` 複製後調整非機密 `Storage:DataRoot`，本機檔案不得提交。Cookie／Policy、共用 OpenAPI Typed Client、中央 Audit、Outbox／通知與 Hangfire 維護工作均已合併。新 API 必須沿用既有能力，不得以臨時授權、同步寄信、自建 Audit 或自建排程替代。

## 3. 權威規格閱讀順序

1. [[03-架構/03-資料與一致性/資料字典索引]]、[[03-架構/03-資料與一致性/資料字典-商品庫存與組裝]]、[[03-架構/03-資料與一致性/資料字典-購物交易與售後]]。
2. [[03-架構/03-資料與一致性/狀態機設計]]、[[03-架構/02-API與前端契約/API Endpoint目錄]]、[[03-架構/02-API與前端契約/API DTO與Schema契約]]、[[03-架構/02-API與前端契約/API錯誤碼目錄]]。
3. [[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]、[[02-領域需求/02-商品庫存與組裝/庫存規則]]、[[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]、[[02-領域需求/05-AI與報表/報表與展示資料]]。
4. [[03-架構/09-資料表實作交付/Terry-商品庫存物流組裝與報表最終Schema]]。

批次匯入另讀 [[03-架構/03-資料與一致性/匯入暫存與庫存調整設計]]；相容規則另讀 [[03-架構/07-領域設計/相容性規則後台設計]]；報表口徑不得只看 UI，必須以資料字典與報表需求的公式為準。

## 4. 現有程式落點

| 能力 | Domain | Infrastructure Configuration |
|---|---|---|
| 商品／規格／翻譯 | `Catalog/` | `Configurations/Catalog/` |
| 匯入 | `Imports/` | `Configurations/Imports/` |
| 購物車 | `Shopping/` | `Configurations/Shopping/` |
| 庫存 | `Inventory/` | `Configurations/Inventory/` |
| 物流 | `Shipping/` | `Configurations/Shipping/` |
| 組裝／相容性 | `Builds/` | `Configurations/Builds/` |
| 評價 | `Reviews/` | `Configurations/Reviews/` |
| 報表定義 | `Reports/` | `Configurations/Reports/` |

實際根目錄分別是 `FP.dev/src/backend/DoSelect.Domain/` 與 `FP.dev/src/backend/DoSelect.Infrastructure/Persistence/`。Application Use Case／Query／DTO 放 `DoSelect.Application`，Controller 放 `DoSelect.Api`；Controller 不含商業規則或直接協調大量 DbContext 寫入。

前台 feature 目標：`catalog`、`builds`、`cart`、`checkout`；後台：`catalog`、`inventory`、`shipping`、`reports`。共用 API wrapper 位於 `frontend/shared`，不得另建平行 fetch client。

## 5. 你必須交付的 API 與頁面

API 群組：

- 公開 `/api/v1/products`、`catalog/filter-options`、`compatibility-checks`、`build-lists`／`build-shares`。
- 購物車 `/api/v1/cart*`、配送選項與便利商店。
- 後台 `/api/v1/admin/products*`、brands／categories／tags／specification-definitions、product-imports、compatibility-rules。
- 後台 inventory、inventory-imports、shipping providers／stores／batches。
- `/api/v1/admin/reports/{reportKey}` 七個白名單報表。

對應前台 Page ID：`C-01`～`C-07`、`C-13`～`C-15` 的商品／購物／配送部分、`C-24`。後台：`A-04`～`A-18`、`A-27`。完整 Route 與狀態見 [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]。

角色重點：型錄 `CatalogManager`、庫存 `InventoryManager`、物流／訂單 `OrderManager`、報表依 Report Policy；SuperAdmin 不是開發時省略細部 Policy 的理由。

## 6. 不可破壞的領域規則

- Cart 不保留庫存；Checkout 建立 Order 後才在同一交易建立 Order-only Reservation。
- 多 SKU 保留全數成功才提交；最後一件商品必須靠 SQL 交易／併發條件保護，不能只靠前端重查。
- 出貨才把 Active Reservation 轉 Consumed，並同時調整 OnHand／Reserved 與建立 InventoryMovement。
- 商品匯入與庫存調整是不同 Use Case、不同 Policy、不同暫存批次。
- 相容性六類硬規則由程式固定；後台只管理啟用、門檻與測試，不能任意建立執行碼。
- 報表使用正式快照、退款與成本公式；不得用目前商品價格回算歷史訂單。
- 對外只用 PublicId；Entity 維持公開 getter／private setter 與具名商業方法。

## 7. 跨模組契約

| 對象 | 你提供 | 你取得 |
|---|---|---|
| haru | Cart、Reservation、Shipment、COD Eligibility、SKU／價格／庫存快照 | Order／OrderItem 唯讀摘要與狀態 |
| yinyin | COD Eligibility、ShippingMethod、Cart 金額與 SKU 預付摘要 | Coupon 計算、Payment／Refund／Invoice 結果 |
| kafen | Carrier／ShippingMethod Lookup、Order Item／Delivery 摘要 | 退貨檢查的回補庫存決策、去識別案件指標 |
| alex | 商品／庫存／組裝／相容 Query／DTO、報表彙總 | 共通授權、Outbox、OpenAPI 產生與 AI 呼叫結果 |

跨模組只使用公開 Application Query／DTO。不要取得他人 Repository、直接查他人表，或把退貨寄回物流放進 outbound `Shipment` Aggregate。

## 8. 建議切片順序

1. 商品／SKU／分類／規格讀取與一般搜尋，再完成管理 CRUD。
2. 購物車與登入合併契約。
3. 自由組裝與確定性相容檢查。
4. 庫存 Balance／Movement／Reservation 與併發測試。
5. 配送選項、門市、包裹限制與出貨。
6. 商品／庫存匯入 Preview／Confirm。
7. 七個報表與 `INT-04` 金額一致性。
8. `S-02` 只在 S 門檻通過後實作。

每個切片用獨立 Branch／PR；不要以「商品模組」為名一次提交全部工作包。

## 9. 必要測試

至少覆蓋 `UC-CART-01`～`02`、`UC-CHECKOUT-01`、`UC-CHECKOUT-COD-01`、`UC-SEARCH-01`、`UC-IMPORT-01`、`UC-BUILD-01`、`UC-COMPAT-01`、`UC-ADM-PROD-01`～`02`、`UC-ADM-INV-01`、`UC-ADM-SHIP-01`～`02`、`UC-ADM-STORE-01`、`UC-REPORT-01`。

庫存與結帳測試必須使用 SQL Server Provider-backed 情境驗證最後一件、全單回滾、RowVersion、重試與冪等；只用 InMemory Provider 不算完成。固定由 kafen 做第一線交叉覆核，退款數字另由 yinyin 覆核。

提交前在 `FP.dev` 執行：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

修改前台或後台時，在對應 App 執行 `npm run typecheck`、`npm run lint -- --max-warnings 0`、`npm test`、`npm run build`、`npm audit --omit=dev`。

## 10. PR、日誌與停止條件

- 遵守 [[Git協作規範]] 與 [[日誌/README]]；API、DTO、錯誤碼、狀態、資料庫、跨模組 Query／Event 均需同 PR 日誌。
- 共用 OpenAPI schema、產生的 Typed Client 型別與 wrapper 已在 `dev`；契約變更後須執行 `api:generate`／`api:check`，禁止 Vue 手寫另一套 DTO。
- Schema 變更只在日誌提出，不自行建立第二個 DbContext 或 Migration；交 alex 走 Gate。
- 新增 Package、文件衝突、公式未定、跨模組 DTO 缺少、必須改共用 Policy／Outbox／檔案服務時，停止並詢問 alex。
- 完成證據包含：成功與失敗流程、權限、分頁／排序、併發、金額或數量不變量、前端共同狀態、kafen 覆核與可接手日誌。
