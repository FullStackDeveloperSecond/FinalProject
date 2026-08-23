---
文件狀態: 可開發
適用對象: yinyin
主要覆核: haru
最終整合: alex
---

# Yinyin｜優惠券、付款、退款與模擬發票工程包

## 1. 你的責任與完成邊界

| 優先級 | 工作包 | 範圍 |
|---|---|---|
| M | `M-07` | 優惠券、促銷計算、使用限制、併發與金額分攤 |
| M | `M-09` | 七類模擬付款、付款嘗試、重試與展示端點 |
| M | `M-13` | 核准後的部分退款、分攤、冪等與歷程 |
| M | `M-20` | 模擬發票、作廢、退款折讓與 DEMO 標記 |
| 整合 | `INT-02` | 訪客結帳、優惠／運費、庫存保留、付款逾時與出貨 |

退貨資格、收件、檢查與核准屬 kafen；你只負責退款財務執行。`ApproveReturnRequest` 與 `ExecuteRefundRequest` 是不同 Use Case，不可合併成「核准並退款」。

## 2. 開始前檢查

在 `FP.dev`：

```powershell
git switch dev
git pull --ff-only origin dev
git switch -c feature/coupon-calculation
dotnet tool restore
dotnet restore DoSelect.slnx

Set-Location frontend/customer-web
npm ci
Set-Location ../admin-web
npm ci
Set-Location ../..
```

依 `FP.dev/README.md` 與 [[03-架構/01-系統與環境/本機開發環境與版本基線]] 準備 .NET 10.0.303、Node 24、npm 11、SQL Server `.\SQL2025`／`DoSelectDb`。新環境只套用已審查的 `InitialCreate`：

```powershell
dotnet tool run dotnet-ef -- database update InitialCreate `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext
```

需要最小帳號時，以 Visual Studio 管理 `Seed:MemberPassword`、`Seed:AdminPassword` User Secrets，再用 PATH 中的 `dotnet` 執行 API `--seed-minimal`。兩支 Seed 腳本目前含組長本機 `.NET` 絕對路徑，其他帳號不要直接依賴，也不要混入金流 PR 修改。

首次 Clone 若沒有 `appsettings.Development.json`，由同目錄的 `.example.json` 複製後調整非機密 `Storage:DataRoot`，本機檔案不得提交。退款執行須沿用已完成的共用 `IIdempotencyExecutor`；Outbox／Hangfire、Audit 與 TOTP／Policy 由 alex 的共用工作包推進。你先定義並測試 Application 契約與同交易資料寫入，不要自建第二套冪等表、排程器、Audit 或授權方式。

## 3. 權威規格閱讀順序

1. [[03-架構/03-資料與一致性/資料字典索引]]、[[03-架構/03-資料與一致性/資料字典-購物交易與售後]]。
2. [[03-架構/03-資料與一致性/狀態機設計]]、[[03-架構/02-API與前端契約/API Endpoint目錄]]、[[03-架構/02-API與前端契約/API DTO與Schema契約]]、[[03-架構/02-API與前端契約/API錯誤碼目錄]]。
3. [[02-領域需求/03-交易與履約/優惠券規則]]、[[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]、[[02-領域需求/04-客服與售後/退貨與退款政策]]、[[02-領域需求/04-客服與售後/評價收藏檢舉與模擬發票規格]]。
4. [[03-架構/09-資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]。

金額一致性、冪等與 Outbox 另讀 [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]；任何金額定義衝突需回報 alex，不自行選擇對商家或顧客較有利的算法。

## 4. 現有程式落點

| 能力 | Domain | EF Configuration |
|---|---|---|
| 優惠券 | `DoSelect.Domain/Promotions/` | `Configurations/Promotions/` |
| 付款 | `DoSelect.Domain/Payments/` | `Configurations/Payments/` |
| 退款 | `DoSelect.Domain/Refunds/` | `Configurations/Refunds/` |
| 模擬發票 | `DoSelect.Domain/Invoicing/` | `Configurations/Invoicing/` |

根路徑為 `FP.dev/src/backend/`。現有 Entity／Mapping 與初始 Migration 已完成；新工作以 Application Use Case／Query／DTO、API Controller、前端 feature 與各層測試為主。

前台 feature：`cart`／`checkout`／`orders` 的優惠、付款與發票部分。後台 feature：`coupons`、`refunds`。不要把付款狀態塞回 OrderStatus，也不要讓 Controller 或 Vue 指定任意狀態。

## 5. 你必須交付的 API 與頁面

- `/api/v1/cart/coupon` 套用與移除。
- `/api/v1/orders/{orderId}/payment-attempts` 與 `/api/v1/simulated-payments/{attemptId}/actions/complete`。
- `/api/v1/admin/refunds*` 與具 Idempotency-Key 的 execute Action。
- `/api/v1/admin/coupons*` 管理與 activate／pause／disable。
- `GET /api/v1/orders/{orderId}/invoice` 提供會員本人或有效訪客限單 Scope 查詢。
- `GET /api/v1/admin/invoices`、`GET /api/v1/admin/invoices/{id}`、`POST /api/v1/admin/orders/{orderId}/invoices`、`POST /api/v1/admin/invoices/{id}/actions/void`、`POST /api/v1/admin/invoices/{id}/allowances`；開立與折讓需 Idempotency-Key。不得用付款資料表假裝發票資料。

前台重點 Page ID：`C-13`～`C-15`、`C-18`、`C-20`。後台：`A-20` 的退款預覽協作、`A-21`～`A-23`。完整 Route 與錯誤狀態見 [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]。

權限重點：退款執行使用 `Refund.Execute`（`FinanceManager`／`SuperAdmin`）；後台優惠券查詢、建立、修改與 `activate`／`pause`／`disable` 使用 `Coupon.Manage`（`FinanceManager`／`MarketingAnalyst`／`SuperAdmin`）；後台模擬發票查詢、開立、作廢與折讓使用 `Invoice.Manage`（`FinanceManager`／`SuperAdmin`）。三者均沿用管理員 TOTP／MFA 基線；前台購物車套券不使用 `Coupon.Manage`。Policy 只負責授權，狀態、冪等、RowVersion、金額／名額規則與 Audit 仍由各 Use Case 負責，前端確認框不是安全邊界。

## 6. 不可破壞的金額與狀態規則

- 優惠券的門檻、範圍、使用次數與分攤在後端重算；前端送代碼與選擇，不送可信價格。
- 最低消費只比對優惠券適用商品小計；購物車不持久化優惠碼，每次預覽由前端帶入，Checkout 再次驗證。
- 部分退貨使用 `OrderCoupon.MinimumSpendAmount` 與 `OrderItem.IsCouponEligible` 快照，不回查目前 Coupon；`RefundAllocation.Amount` 保持正值並由 AllocationType 固定加減方向，ItemRefund 另保存正整數 `Quantity` 作為折讓數量的不可變來源。
- `ExecuteRefundRequest` 不接受 `allocations`；後端 `RefundCalculator` 必須依已核准 Refund 與可信交易快照產生完整分攤，管理端不得指定會計拆分。
- CouponRedemption 在 Checkout-bound 交易處理；失敗或到期是否返還依正式狀態規則，不以刪除紀錄處理。
- PaymentAttempt 一筆代表一次嘗試；失敗、取消、到期後建立新紀錄，終態不倒退。
- 即時付款最長 15 分鐘；ATM 與超商代碼最長 3 天；實際期限取付款方式期限與訂單原付款期限較早者，COD 在交付／取貨時完成付款。
- 退款核准與執行分離；執行使用 Idempotency-Key，累計不得超過可退款餘額。
- 退款回應固定輸出 `itemRefund`、`originalShipping`、`returnShipping`、`assemblyFee`、`discountClawback`、`shippingClawback`、`otherAdjustment` 七類分攤；Amount 一律為正值並由類型決定方向，V1 新寫入禁止 `otherAdjustment`。
- 折讓數量只能取自 `RefundAllocation.Quantity`；不得依退款金額比例、固定值或目前退貨申請數量反推。
- 退款執行的 `reasonCode`／安全處理後的 `note` 只寫中央 Audit，不在 Refund 重複建欄位；Audit 與退款狀態、分攤、冪等完成紀錄同交易提交，中央 Audit 實作未完成前 PR #16 不得合併。
- `requestedBy`／`approvedBy`／`executedBy` 只回 `{ publicId, maskedLabel }`；不得回傳 Internal Identity ID、完整 DisplayName 或完整 Email。
- 付款、退款、折讓 Event 採 append-only／冪等；不可修改歷史偽裝成新事件。
- 模擬發票與折讓固定採 5% 稅率與 TWD 整數元：`Net = Round(Gross / 1.05, 0, AwayFromZero)`、`Tax = Gross - Net`，最後一筆合法明細吸收尾差；1,000 元案例必須得到 952／48／1,000。
- 開立、作廢與折讓失敗使用正式 `invoice_order_unpaid`、`invoice_order_cancelled`、`invoice_already_exists`、`invoice_state_conflict`、`invoice_allowance_required`，不得自行新增文字型拒絕代碼。

## 7. 跨模組契約

| 對象 | 你提供 | 你取得 |
|---|---|---|
| haru | PaymentAttempt、付款／退款彙總、Invoice 結果 | Order、OrderItem、付款期限、擁有者與金額快照 |
| terry | Coupon 計算結果、付款／退款狀態 | Cart、SKU、ShippingMethod、COD Eligibility、RequiresPrepayment 摘要 |
| kafen | Refund 執行與分攤結果 | 已核准 Return、Inspection 與可退款項目摘要 |
| alex | 付款／退款 Outbox payload、財務測試案例 | 已完成的 Idempotency Executor，以及待完成的 Outbox Dispatcher、Audit、TOTP／Policy 共用能力 |

不得讀取其他模組 Repository／DbContext 或底層表。跨模組同步交易由 Application Use Case 協調同一 Unit of Work；通知與外部副作用使用版本化 Outbox Event。

## 8. 建議切片順序

1. 優惠券純計算與分攤 Domain／Application 測試。
2. Cart 套券與後台優惠券生命週期。
3. PaymentAttempt 與七類模擬付款狀態機。
4. 與 haru／terry 完成 Checkout、COD、逾時與庫存整合。
5. 核准後退款執行、冪等、失敗重試與分攤。
6. 模擬發票、作廢與退款折讓。
7. 完成 `INT-02`；再協助 terry／kafen 覆核 `INT-04` 金額。

每個切片獨立 PR，不要等四個 M 工作包一次完成才交付。

## 9. 必要測試

至少覆蓋 `UC-PAY-01`、`UC-COUPON-01`、`UC-REFUND-01`，以及 `UC-CHECKOUT-01`／COD 中你負責的金額與付款部分。必要情境：重複 Idempotency-Key 同結果、同 Key 不同 Payload 衝突、付款重複回呼、期限取較早者、優惠券併發最後名額、適用商品小計門檻、Exhausted 返還、部分退貨門檻重算、後端從可信快照產生完整七類分攤、正值 Allocation 的折扣／免運扣回、組裝費分攤及失敗後無重複副作用。退款另須測 Request 不接受 allocations、ItemRefund 數量不變量、V1 拒絕 OtherAdjustment、Audit 寫入失敗整體回滾，以及管理員摘要不含 Internal Id／完整姓名／完整 Email。發票另測未付款、取消、重複開立、非法作廢、退款後必須折讓、1,000→952＋48、明細尾差及跨訂單授權。後台優惠券與發票 Endpoint 必須分別覆蓋 `Coupon.Manage`、`Invoice.Manage` 的合法角色、錯誤角色、未完成 MFA 與匿名請求；`SuperAdmin` 必須有正向案例。`CouponRuleReader` 與退款／Audit 交易必須以 SQL Server Provider-backed 整合測試驗證，不新增 InMemory／SQLite 取代。

固定由 haru 做第一線覆核；退貨案例由 kafen 提供；庫存、配送與報表金額由 terry 共同驗證。金額／退款／私人財務資料必須含未授權角色與 Actor A／B 負面測試。

提交前在 `FP.dev` 執行：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive
```

修改 Vue 時，在對應 App 執行 `npm run typecheck`、`npm run lint -- --max-warnings 0`、`npm test`、`npm run build`、`npm audit --omit=dev`。

## 10. PR、日誌與停止條件

- 遵守 [[Git協作規範]]；PR 合併至 `dev`，由 alex 核准，Squash Merge。
- 依 [[日誌/README]] 記錄 DTO、金額公式、Idempotency-Key、狀態、Outbox、資料庫與跨模組契約；必須讓 haru 能重跑付款／退款案例。
- Typed Client 由第一批 API 穩定後依 [[03-架構/02-API與前端契約/OpenAPI與前端Client流程]] 產生，不手寫平行 DTO。
- 不自行 scaffold／apply Migration；Schema 需求交 alex 走 Gate。
- 公式或退款政策不明、需要改其他 Owner Entity、共用 TOTP／Policy／Outbox、錯誤碼未登錄、新增套件或文件衝突時，停止並詢問 alex。
