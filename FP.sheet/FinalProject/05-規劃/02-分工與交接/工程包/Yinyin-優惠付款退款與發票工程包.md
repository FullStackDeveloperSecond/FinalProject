---
文件狀態: 可開發
最後更新: 2026-08-31
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

依 `FP.dev/README.md` 與 [[03-架構/01-系統與環境/本機開發環境與版本基線]] 準備 .NET 10.0.303、Node 24、npm 11、SQL Server `.\SQL2025`／`DoSelectDb`。新環境套用 `dev` 目前全部已審查 Migration：

```powershell
dotnet tool run dotnet-ef -- database update `
  --project src/backend/DoSelect.Infrastructure `
  --startup-project src/backend/DoSelect.Infrastructure `
  --context DoSelectDbContext
```

Migration 名稱與數量會隨整合變動，不再複製到個人工程包。唯一來源是 `FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Migrations/` 與 `DoSelectDbContextModelSnapshot.cs`；每次先同步最新 `dev`，再由不指定 Migration 名稱的命令套用完整歷程。不得只停在特定 Migration，也不得在功能分支自行 scaffold／apply 新 Migration；Schema 需求交 alex 走 Migration Gate。實作進度統一查看 [[05-規劃/01-時程與進度/M功能實作矩陣]]。
需要最小帳號時，以 Visual Studio 管理 `Seed:MemberPassword`、`Seed:AdminPassword` User Secrets，再用 PATH 中的 `dotnet` 執行 API `--seed-minimal`。兩支 Seed 腳本目前含組長本機 `.NET` 絕對路徑，其他帳號不要直接依賴，也不要混入金流 PR 修改。

首次 Clone 若沒有 `appsettings.Development.json`，由同目錄的 `.example.json` 複製後調整非機密 `Storage:DataRoot`，本機檔案不得提交。退款執行須沿用已合併的 `IIdempotencyExecutor`、中央 `IAuditWriter`、Outbox／Hangfire 與 `Refund.Execute`／`Coupon.Manage`／`Invoice.Manage` Policy；管理員 TOTP 流程亦已進 `dev`。不要自建第二套冪等表、排程器、Audit 或授權方式。

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

根路徑為 `FP.dev/src/backend/`。現有 Entity／Mapping 依目前拉取的 `dev` 完整 Migration 歷程管理；新工作以 Application Use Case／Query／DTO、API Controller、前端 feature 與各層測試為主。

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
- 折讓來源依 DEC-P298 固定映射：`ItemRefund` 建立折讓；原發票確實收取且本次退還的 `OriginalShipping`／`AssemblyFee` 建立折讓；`ReturnShipping`、`DiscountClawback`、`ShippingClawback` 不建立折讓明細；`OtherAdjustment` 第一版禁止。不得把退貨寄回成本或退款扣回偽裝成原發票折讓。
- 依 DEC-P299，發票商品列必須有 `OrderItemId` 且不得使用保留碼；非商品列必須沒有 `OrderItemId`，並只接受 Domain 中央常數 `__INVOICE_SHIPPING__`／`__INVOICE_ASSEMBLY_FEE__`。Writer、Reader 與 DTO 映射共用常數，API 對外回 `kind = merchandise|shipping|assemblyFee`；未知非商品列與 `OtherAdjustment` 必須拒絕，不得靜默略過。
- 退款執行的 `reasonCode`／安全處理後的 `note` 只寫已合併的中央 Audit，不在 Refund 重複建欄位；PR #16 必須把 Audit、退款狀態、分攤與冪等完成紀錄納入同一 SQL Server 交易，並以 Audit 失敗整體回滾測試後才可合併。
- `requestedBy`／`approvedBy`／`executedBy` 只回 `{ publicId, maskedLabel }`；不得回傳 Internal Identity ID、完整 DisplayName 或完整 Email。
- 付款、退款、折讓 Event 採 append-only／冪等；不可修改歷史偽裝成新事件。
- 模擬發票固定採 5% 稅率；最終應付在付款前以 AwayFromZero 取整數，且 `Order.GrandTotal = PaymentAttempt.Amount = Order.PaidAmount = Invoice.IssuedAmount`。發票表頭 Gross／Net／Tax 為整數元，明細可保留兩位小數並依三條核對口徑與表頭一致，最後一筆合法明細吸收稅額尾差；1,000 元案例必須得到 952／48／1,000。折讓仍依 DEC-P280，未由 DEC-P285 覆寫。
- 開立、作廢與折讓失敗使用正式 `invoice_order_unpaid`、`invoice_order_cancelled`、`invoice_already_exists`、`invoice_state_conflict`、`invoice_allowance_required`，不得自行新增文字型拒絕代碼。

## 7. 跨模組契約

| 對象 | 你提供 | 你取得 |
|---|---|---|
| haru | PaymentAttempt、付款／退款彙總、Invoice 結果 | Order、OrderItem、付款期限、擁有者與金額快照 |
| terry | Coupon 計算結果、付款／退款狀態 | Cart、SKU、ShippingMethod、COD Eligibility、RequiresPrepayment 摘要 |
| kafen | Refund 執行與分攤結果 | 已核准 Return、Inspection 與可退款項目摘要 |
| alex | 付款／退款 Outbox payload、財務測試案例 | 已完成的 Idempotency Executor、中央 Audit 與 Policy 基線；待完成的 Outbox Dispatcher／Hangfire 與完整管理員 TOTP 流程 |

不得讀取其他模組 Repository／DbContext 或底層表。跨模組同步交易由 Application Use Case 協調同一 Unit of Work；通知與外部副作用使用版本化 Outbox Event。

> **例外（DEC-B1，alex 2026-08-28 裁定）**：退款執行取得**具名、窄範圍**的跨模組例外，
> 允許 `RefundTrustedInputsReader`、`RefundExecutor` 的分攤寫入與管理員資格重查、`RefundReader`
> 的 `RefundDto` 投影使用既有 `DoSelectDbContext`。元件、資料表、欄位與用途逐一明列於
> [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-B1-退款跨模組具名例外|DEC-B1]]。
> 交易仍由 `IIdempotencyExecutor` 擁有，Reader 不得自行 Begin／Commit；
> 守門測試以逐元件白名單掃描完整 Refund Infrastructure。
> **這是個別裁定，不是其他模組可自行類推的通則。**

## 8. 建議切片順序

1. 優惠券純計算與分攤 Domain／Application 測試。
2. Cart 套券與後台優惠券生命週期。
3. PaymentAttempt 與七類模擬付款狀態機。
4. 與 haru／terry 完成 Checkout、COD、逾時與庫存整合。
5. 核准後退款執行、冪等、失敗重試與分攤。
6. 模擬發票、作廢與退款折讓。
7. 完成 `INT-02`；再協助 terry／kafen 覆核 `INT-04` 金額。

每個切片獨立 PR，不要等四個 M 工作包一次完成才交付。

優惠券、付款與退款的即時完成狀態只維護於 [[05-規劃/01-時程與進度/M功能實作矩陣]] 與 [[05-規劃/01-時程與進度/未完成項目追蹤表]]；本工程包只保留穩定責任與契約，避免複製 PR 狀態後再次過期。

實際 rebase、重建 branch 或修改 PR base 必須另經 alex 授權。

## 9. 必要測試

至少覆蓋 `UC-PAY-01`、`UC-COUPON-01`、`UC-REFUND-01`，以及 `UC-CHECKOUT-01`／COD 中你負責的金額與付款部分。必要情境：重複 Idempotency-Key 同結果、同 Key 不同 Payload 衝突、付款重複回呼、期限取較早者、優惠券併發最後名額、適用商品小計門檻、Exhausted 返還、部分退貨門檻重算、後端從可信快照產生完整七類分攤、正值 Allocation 的折扣／免運扣回、組裝費分攤及失敗後無重複副作用。退款另須測 Request 不接受 allocations、ItemRefund 數量不變量、V1 拒絕 OtherAdjustment、Audit 寫入失敗整體回滾，以及管理員摘要不含 Internal Id／完整姓名／完整 Email。發票另測未付款、取消、重複開立、非法作廢、退款後必須折讓、商品＋原始運費＋組裝費完整退款不漏記並進入 `FullyAllowed`、ReturnShipping／兩種 Clawback 不建立折讓明細、1,000→952＋48、明細尾差及跨訂單授權。後台優惠券與發票 Endpoint 必須分別覆蓋 `Coupon.Manage`、`Invoice.Manage` 的合法角色、錯誤角色、未完成 MFA 與匿名請求；`SuperAdmin` 必須有正向案例。`CouponRuleReader`、退款／Audit 交易與折讓 Reader 必須以 SQL Server Provider-backed 整合測試驗證，不新增 InMemory／SQLite 取代。

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
- PR #6 已合併退款折讓 API／Writer／Reader 與 SQL 測試。PR #16 已轉 Ready，中央 Audit、DES-21 快照、可設定隔離策略、管理員 actor scope、遮罩摘要、最終契約／OpenAPI 與防止跨模組 DbContext 逃逸的守門均已完成靜態 review；目前與最新 `dev` 衝突。接手者須先以普通 merge 整合最新 `dev`、重新核對完整 diff，再針對 exact head 完成退款／Audit 同交易回滾、冪等、七類分攤與完整 SQL Server Provider-backed 測試，通過後才可 Approve／Merge。
- 依 [[日誌/README]] 記錄 DTO、金額公式、Idempotency-Key、狀態、Outbox、資料庫與跨模組契約；必須讓 haru 能重跑付款／退款案例。
- 共用 OpenAPI schema、產生的 Typed Client 型別與 wrapper 已在 `dev`；API 變更後依 [[03-架構/02-API與前端契約/OpenAPI與前端Client流程]] 執行 `api:generate`／`api:check`，不手寫平行 DTO。
- 不自行 scaffold／apply Migration；Schema 需求交 alex 走 Gate。
- 公式或退款政策不明、需要改其他 Owner Entity、共用 TOTP／Policy／Outbox、錯誤碼未登錄、新增套件或文件衝突時，停止並詢問 alex。
