---
文件狀態: 可開發
適用對象: kafen
主要覆核: terry
最終整合: alex
---

# Kafen｜客服、退貨、檢舉、案件工作台與 RWD 工程包

## 1. 你的責任與完成邊界

| 優先級 | 工作包 | 範圍 |
|---|---|---|
| M | `M-12` | 單項退貨、附件、期限、審核、寄回物流、收貨與檢查 |
| M | `M-14A` | 客服案件、七類分類、回覆、取消、佇列、自領／指派／轉派、附件 |
| M | `M-14B` | 四級 SLA、暫停／重開、提醒／逾時與統一案件工作台 |
| S，門檻後 | `S-03` | 商品、評價、客服等檢舉與後台處理 |
| S，門檻後 | `S-07` | 消費者前台 360／768 RWD |
| 專案產出 | `PM-02` | DoSelect 懂選 Logo、品牌色與 UI 視覺規範 |

AI 客服、Prompt、OpenAI、Token／成本與摘要內容產生由 alex 負責。你提供受控客服 Query／DTO、摘要寫入 Use Case 與客服領域覆核；不得在本模組直接呼叫 OpenAI。

## 2. 開始前檢查

在 `FP.dev`：

```powershell
git switch dev
git pull --ff-only origin dev
git switch -c feature/support-tickets
dotnet tool restore
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci
Set-Location ../admin-web
npm ci
Set-Location ../..
```

環境、SQL Server `.\SQL2025`／`DoSelectDb`、既有 `InitialCreate`、啟動與檢查完全依 `FP.dev/README.md` 與 [[03-架構/01-系統與環境/本機開發環境與版本基線]]。不要建立新資料庫設計或每模組 Migration。

```powershell
dotnet tool run dotnet-ef -- database update InitialCreate `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext

.\scripts\start-all.ps1
.\scripts\health-check.ps1
```

需要最小帳號時，以 Visual Studio 管理 User Secrets 後，用 PATH 中的 `dotnet` 執行 API `--seed-minimal`。現有 Seed PowerShell 腳本含組長本機 `.NET` 絕對路徑；不要直接依賴或在客服 PR 順便修正。

首次 Clone 若沒有 `appsettings.Development.json`，由同目錄的 `.example.json` 複製後調整非機密 `Storage:DataRoot`，本機檔案不得提交。正式 Cookie／Policy、私有檔案掃描與儲存、Outbox／Hangfire 及 Typed Client 仍由 alex 的共用工作包推進；你可以先完成案件／退貨規則、Application 契約與 Stub 邊界，但不得把檔案放 `wwwroot`、同步寄通知或自建排程器。

## 3. 權威規格閱讀順序

1. [[03-架構/03-資料與一致性/資料字典索引]]、[[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]、[[03-架構/03-資料與一致性/資料字典-購物交易與售後]]。
2. [[03-架構/03-資料與一致性/狀態機設計]]、[[03-架構/02-API與前端契約/API Endpoint目錄]]、[[03-架構/02-API與前端契約/API DTO與Schema契約]]、[[03-架構/02-API與前端契約/API錯誤碼目錄]]。
3. [[02-領域需求/04-客服與售後/客服與AI功能]]、[[02-領域需求/04-客服與售後/退貨與退款政策]]、[[02-領域需求/04-客服與售後/評價收藏檢舉與模擬發票規格]]、[[02-領域需求/90-驗收規格/商品組裝客服與報表驗收規格]]。
4. [[03-架構/09-資料表實作交付/Kafen-客服售後與檢舉最終Schema]]。

案件查詢另讀 [[03-架構/07-領域設計/統一案件工作台設計]]；附件另讀 [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]；SLA 定義可先讀 [[知識點/01-電商與組裝/SLA]]。RWD／品牌不得改變已定 Route、授權與資料契約。

## 4. 現有程式落點

| 能力 | 現有位置 |
|---|---|
| 客服、SLA、檢舉、工作台模型 | `FP.dev/src/backend/DoSelect.Domain/Support/` |
| 退貨與寄回物流 | `FP.dev/src/backend/DoSelect.Domain/Returns/` |
| EF Mapping | `DoSelect.Infrastructure/Persistence/Configurations/Support/`、`Returns/` |
| 唯讀工作台 | `DoSelectDbContext.CaseWorkbench`、Migration 中的 `vw_CaseWorkbench` |
| 模型測試 | `KafenEntityTests.cs`、`KafenPersistenceModelTests.cs` |

Application 層新增客服／退貨 Use Case、授權 Query 與 DTO；API 使用 Controller；前台 feature 使用 `returns`、`support`，後台使用 `returns`、`cases`。Page 只協調 Query／Mutation 與 UI 狀態，不直接承載 SLA 或退貨規則。

附件第一版為網站根目錄之外的私有目錄，下載一律經授權 API。實際掃描與儲存共用能力 `SH-06` 由 alex 負責；你負責案件側數量、關聯、Actor Scope 與狀態，不可把附件放 `wwwroot`。

## 5. 你必須交付的 API 與頁面

- 顧客 `/api/v1/support-tickets*`、messages、cancel、attachments 與授權下載。
- 後台 `/api/v1/admin/support-tickets/{id}`、internal-notes、claim／assign／transfer／priority／status／cancel／reopen。
- `/api/v1/admin/support-tickets/sla`、`/api/v1/admin/case-workbench`。
- 顧客 `/api/v1/orders/{orderId}/returns`、`/api/v1/returns/{id}` 與附件。
- 後台 `/api/v1/admin/returns*`、receive／inspect／extend／review、ReturnShipment 與 Event。
- S-03 啟動後才加入正式檢舉 API；不得因資料表已存在提前把 S 功能混入 M 導覽。

前台 Page ID：`C-19`～`C-20`、`C-26`、`C-28`～`C-30`；`C-27` AI 頁由 alex 主責、你提供案例與領域覆核。後台：`A-19`～`A-20`、`A-24`～`A-26`。完整規格見 [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]。

角色重點：客服 `CustomerService`、主管 `CustomerServiceSupervisor`；退貨由 `OrderManager`／相應 Return Policy；不同角色在工作台只看到獲授權分支。前端隱藏卡片不等於後端分支授權。

- `SupportTicket.Handle`：允許 `CustomerService`、`CustomerServiceSupervisor`；套用於 internal-notes、claim、一般 priority 調整、status、cancel、reopen 及日常案件處理。
- `SupportTicket.Supervise`：允許 `CustomerServiceSupervisor`、`SuperAdmin`；套用於 assign、transfer、priority 覆核／覆寫。
- `SuperAdmin` 單一角色不得通過 `SupportTicket.Handle`；需要日常客服處理時必須另授客服角色。所有 Policy 仍須檢查狀態、RowVersion、理由與 Audit。
- `409 support_ticket_assignment_conflict` 只回標準 Problem Details，不加入最新承辦人欄位；前端顯示衝突提示後，失效並重新查詢該案件明細與目前佇列，以新 DTO 更新 Assignee、RowVersion、AvailableActions。

## 6. 不可破壞的領域規則

- SupportTicket、ReturnRequest、ReportCase 是三個獨立 Aggregate；工作台只是 `UNION ALL` 唯讀投影，不能提供共用寫入 Entity。
- 工作台固定 12 欄，以 `LastActivityAtUtc／CasePublicId` Cursor；RowVersion 與 AvailableActions 回原領域詳情取得。
- 客服 Closed 是終態；後續問題建立關聯新案件。WaitingForCustomer 最多暫停 SLA 3 天；WaitingForInternal 不暫停。
- AI 回答不算首次人工回覆；逾時不自動轉派或升級優先級。
- Return 只表達退貨流程，退款成功由 yinyin 的 Refund 狀態表達。
- ReturnShipment 是退貨領域 Aggregate，不共用 terry 的 outbound Shipment；事件 append-only 且去重。
- 只有完成 ReturnInspection 且結果 `Resellable` 才要求 terry 建立 ReturnToStock。
- 私人案件、附件與訂單必須在查詢入口限制 Actor Scope；不得先載入他人資料再遮蔽。

## 7. 跨模組契約

| 對象 | 你提供 | 你取得 |
|---|---|---|
| haru | Return／Ticket 狀態摘要 | 訂單擁有權、可退明細、GuestOrderAccessToken 驗證 |
| terry | ReturnInspection 的回補決策、去識別案件指標 | Carrier／ShippingMethod Lookup、OrderItem／交付摘要 |
| yinyin | 核准 Return 與可退款項目摘要 | Refund 執行／分攤／折讓結果 |
| alex | 去識別客服 Query／DTO、SupportSummary 寫入 Use Case、客服案例 | 檔案掃描、Policy、Outbox、AI 摘要內容與 AI 客服結果 |

跨模組不得共用 Repository／DbContext。Alex 寫入 SupportSummary 時也只能呼叫你提供的 Application Use Case。

## 8. 建議切片順序

1. SupportTicket 建立、列表、明細、Message 與 Actor Scope。
2. 後台佇列、自領／指派／轉派、狀態與內部備註。
3. SLA 計算、提醒資料與 Cursor 列表。
4. `vw_CaseWorkbench` 的授權查詢與三分支案例。
5. Return 建立、附件關聯、審核、寄回、收貨與檢查；再接 yinyin 退款。
6. PM-02 可平行產出，但不得阻塞功能殼層；`S-03`、`S-07` 只在 S 門檻通過後進入功能驗收。

RWD 啟動後，覆蓋整個消費者前台核心流程，不代表你接手其他 Owner 的商業邏輯；畫面問題回原 feature Owner，斷點／版面／導覽由你主責。

## 9. 必要測試

至少覆蓋 `UC-RETURN-01`、`UC-SUPPORT-01`～`02`、`UC-SLA-01`、`UC-WORKBENCH-01`。必要負面案例：Actor A／B 案件與附件隔離、Handle／Supervise 各角色允許與拒絕、SuperAdmin 單角色不可日常處理、多角色聯集、工作台分支洩漏、重複自領／轉派衝突、409 無承辦人擴充欄位且前端重查、SLA 暫停上限、Closed 不可重開、退貨數量／期限、寄回 Event 去重、未完成 Inspection 不回補庫存。

固定由 terry 做第一線覆核；退款與分攤由 yinyin 協作；本人訂單／Guest Scope 由 haru 提供；AI 客服與摘要由 alex 最終驗收。

提交前在 `FP.dev` 執行：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

修改 Vue 時，在對應 App 執行 `npm run typecheck`、`npm run lint -- --max-warnings 0`、`npm test`、`npm run build`、`npm audit --omit=dev`。S-07 啟動後另依 [[03-架構/02-API與前端契約/多語系與RWD驗收矩陣]] 驗證 360／768。

## 10. PR、日誌與停止條件

- 遵守 [[Git協作規範]]；每個切片獨立 PR，由 alex 核准並 Squash Merge。
- 依 [[日誌/README]] 記錄 Query／DTO、狀態、SLA 時間、附件授權、跨模組資料取得與測試帳號角色，不記錄真實個資或實體檔案路徑。
- Typed Client 依 [[03-架構/02-API與前端契約/OpenAPI與前端Client流程]] 由正式 OpenAPI 產生，不手寫第二套 DTO。
- 不自行建立 Migration；View／Entity／Schema 變更交 alex 走 Gate。
- 需要改共用檔案服務／Policy／Outbox、文件狀態衝突、退款或庫存規則不明、新增套件、工作台 12 欄不足時，停止並詢問 alex。
