---
文件狀態: 執行中
最後更新: 2026-09-01
基準分支: dev@e77edfbb
負責人: alex（暫時接手 yinyin 範圍）
來源工程包: Yinyin-優惠付款退款與發票工程包
---

# Yinyin 負責範圍補全推進計畫

## 1. 決定與目標

**Readiness：READY。** 依現有 Domain、Application、SQL Server Provider、中央冪等、Audit、Outbox、OpenAPI 與雙前端架構補齊既有垂直切片；不新增套件、服務、資料表或平行框架。

本計畫的完成目標是讓 `M-07`、`M-09`、`M-13`、`M-20` 及 yinyin 擁有的 `INT-02` 部分，從「底層能力或零散端點已合併」推進到可由 UI 經 API 到 SQL Server 重跑的完整功能。合併 PR、一般 CI 綠燈或只有 Application Service，不視為功能完成。

### 範圍內

- 訪客優惠券每人使用鍵、購物車套券、付款嘗試重試、退款查詢／執行、模擬發票查詢／開立／作廢／折讓。
- 顧客與管理端必要頁面、OpenAPI／Typed Client、SQL Server Provider-backed 測試及核心交易 E2E。
- yinyin 與 Shopping、Orders、Returns、Shipping 的既有公開邊界接線。

### 範圍外

- 新付款 Provider、真實金流、真實電子發票服務、全新資料表或 Migration。
- 改寫 Terry／Haru／Kafen 的模組內部架構。
- 部署、Production SQL、PR 建立、Merge、分支刪除或自動擴大 S 功能。

## 2. 目前證據與文件飄移

基準為 `dev@e77edfbb`、.NET SDK `10.0.303`、EF Core SQL Server、Vue `3.5.41`、TypeScript `5.9.3`、Playwright `1.62.1`。

| 工作包 | 已存在 | 仍缺 |
|---|---|---|
| M-07 優惠券 | 計算、生命週期、SQL Reader、後台 CRUD/API/A-23、購物車套券 Application 契約 | 訪客 HMAC 正確性、Shopping Reader 實作／DI、正式 Cart Controller、顧客 UI、E2E |
| M-09 模擬付款 | 七類政策、Checkout 初始 Attempt、付款重試 Writer/API、Demo complete、Owner/Guest 授權、SQL Writer、Audit/Outbox | 付款／重試 UI、COD 正式物流命令接線、E2E |
| M-13 部分退款 | PR #16 execute、可信七類分攤、冪等、中央 Audit、SQL Server 測試；WP-05 已在接手 worktree 完成後台退款清單／明細 API 與 A-21/A-22 | WP-05 待 commit／push；完整 E2E 仍缺 |
| M-20 模擬發票 | 前台／後台查詢、折讓、付款成功發票 Outbox Consumer | 管理端手動開立、作廢、前後台 UI、完整 E2E |
| INT-02 | Checkout Application/Gateway、同交易建單／庫存／優惠／Attempt、`POST /api/v1/orders`、SQL Server 測試 | 結帳頁、付款頁、最後名額／完整交易 E2E |

已確認兩項追蹤文件飄移，後續需校正：

1. `M功能實作矩陣` 把 PR #63 稱為正式購物車套券 Endpoint，但 `dev` 沒有對應 Controller／OpenAPI route；PR #63 實際只交付 Application 契約與服務。
2. `M功能實作矩陣` 仍把 PR #16 寫成未合併，並把付款成功發票 Outbox 列為缺口；兩者已存在於目前 `dev`。

## 3. 最低成本分析

本計畫屬跨 API、交易與前端邊界的 material change，但不需要新架構。

| 層級 | 判斷 |
|---|---|
| 1. 接受現況 | 不採用；核心交易路由、頁面與 E2E 缺失，且訪客優惠券每人上限可被新購物車規避。 |
| 2. 只改文件／流程 | 不採用；無法修正執行中的 HMAC 與缺少的 HTTP/UI 行為。 |
| 3. 只改設定／資料 | 不足；Secret 設定已裁定，但目前 Checkout 根本未讀取它。 |
| 4. 重用／擴充既有路徑 | **採用。** 沿用 Checkout Gateway、CouponRuleReader、IIdempotencyExecutor、中央 Audit、Outbox、既有 Controller／Vue／OpenAPI 模式。 |
| 5. 最小 bounded code change | 僅在工作包無法靠既有 extension point 完成時使用，例如補缺少的 Controller、Writer 或 Vue feature。 |
| 6. 新依賴／服務／Schema | 不採用；目前沒有證據顯示需要。若後續發現 Schema 缺口，停止並另走 Migration Gate。 |

## 4. Business Impact

| 項目 | 判斷 |
|---|---|
| 受影響角色 | 訪客／會員買家、FinanceManager、MarketingAnalyst、SuperAdmin、客服／退貨協作者 |
| 現況風險 | 訪客優惠券每人限制可能被換購物車規避；多個功能只有底層服務，使用者無正式入口；財務與交易流程缺完整跨層證據 |
| 觸及頻率 | 所有使用優惠券的訪客 Checkout；所有付款重試、退款管理及發票管理操作。實際流量未知 |
| 預期結果 | 同一正規化 Email 穩定對應同一 HMAC；各工作包具有正式 API/UI；核心交易旅程可重跑 |
| 建置／維運成本 | 重用既有架構，建置成本分散於數個 bounded vertical slices；新增的固定維運成本僅為 V1 Secret 設定與既有測試執行 |
| 風險成本 | 主要為交易金額、使用次數、冪等、授權或狀態接線錯誤；以 SQL Server Provider、API integration 與 E2E Gate 控制 |
| 信心 | 高：缺口已由最新程式樹、OpenAPI、DI、頁面與測試交叉核對；實際展示資料與多工作站環境仍需最後驗證 |
| 成功指標 | 下列 REQ 全部有對應實作與測試；Required CI 綠；核心交易 Playwright 綠；無未登錄 public route／錯誤碼 |
| 停止／回復條件 | 需要新 Schema、破壞既有 API、改其他 Owner 內部模型、缺少 Secret、SQL rollback／冪等／授權證據失敗時停止；各工作包可獨立回復 |

## 5. 需求與驗收

| ID | 優先級 | 需求 | 可觀察驗收 |
|---|---|---|---|
| REQ-01 | P0 | 訪客優惠券每人使用鍵符合 DEC-P262 | 相同正規化 Email、不同 cart key 產生相同 HMAC；不同 Email 不同；不保存／輸出 Email 或 Secret；缺少 V1 Secret 時 guest coupon fail closed |
| REQ-02 | P0 | 正式購物車套券 | POST／DELETE `/api/v1/cart/coupon` 只讀可信 Cart line、驗證 RowVersion、回標準 CartDto；不建立 CouponRedemption |
| REQ-03 | P0 | 正式 Checkout 入口 | POST `/api/v1/orders` 解析 Member/Guest actor、要求 Idempotency-Key、呼叫既有 CheckoutService，保持同 SQL Transaction |
| REQ-04 | P0 | 付款重試／新增 Attempt | POST `/api/v1/orders/{orderId}/payment-attempts` 使用可信訂單金額、RowVersion、冪等、Owner Scope；終態 Attempt 不倒退 |
| REQ-05 | P1 | 退款管理查詢 | FinanceManager／SuperAdmin 可分頁查詢退款與檢視明細；未授權、未 MFA、跨資源資料不得外洩 |
| REQ-06 | P1 | 模擬發票管理命令 | `Invoice.Manage` 下可手動開立／作廢，沿用五個正式錯誤碼、RowVersion、Audit／Outbox／冪等規則 |
| REQ-07 | P1 | 前後台使用入口 | Cart、Checkout、Payment、Order Invoice／Refund、Admin Refund、Admin Invoice 具 loading／empty／error／conflict／success 狀態 |
| REQ-08 | P0 | 核心跨層證據 | SQL Server 證明使用次數、最後名額、交易回滾、付款 replay、退款分攤、發票一致性；Playwright 證明主要顧客與管理旅程 |
| REQ-09 | P0 | 契約與進度一致 | OpenAPI、Typed Client、Endpoint 目錄、實作矩陣及追蹤表與實際 `dev` 一致 |

## 6. 架構決定

1. 使用既有 vertical slice；Controller 只處理 HTTP、Actor、Header 與結果映射，交易規則留在 Application／Domain／Infrastructure。
2. 不新增 Repository family、Mediator、狀態管理套件或第二套 Audit／Idempotency／Outbox。
3. 訪客優惠券 V1 Secret 使用 typed options；HMAC 在 Infrastructure 內產生，輸入為 Checkout 已正規化的訂單 Email。
4. 因 `CouponRedemptions` 約束要求訪客 Redemption 必有 `GuestUsageKeyHash`，任何 guest coupon Checkout 缺少／過短 Secret 都 fail closed；會員與未使用 coupon 的 guest Checkout 不依賴此 Secret。
5. Shopping Reader 由 Terry-owned Infrastructure 提供；yinyin 範圍只消費窄介面，不直接跨模組查表。
6. API 變更採 additive；既有 route／DTO 不移除或改名。沒有 Schema 變更。

## 7. 工作包與依賴

| WP | Outcome | 需求 | 依賴 | 驗證 | 大小／不確定性 |
|---|---|---|---|---|---|
| WP-01 | 修正 guest coupon HMAC 與 Secret fail-closed | REQ-01 | 無 | Infrastructure focused tests、build、format | S／低 |
| WP-02 | Cart coupon Reader＋Controller＋DI＋OpenAPI | REQ-02,09 | Terry Shopping line source | Application、API integration、SQL Reader、api:check | M／中 |
| WP-03 | Checkout Controller | REQ-03,09 | WP-01；Cart/Guest actor 已存在 | API integration、Checkout SQL、replay／rollback | M／中 |
| WP-04 | Payment Attempt Writer＋Endpoint | REQ-04,09 | Orders owner query、IIdempotencyExecutor | Application、API integration、SQL Provider | M／中 |
| WP-05 | Refund list/detail API＋A-21/A-22 | REQ-05,07,09 | PR #16 已合併 | API/SQL、Vue component/typecheck/build | M／中 |
| WP-06 | Invoice issue/void API＋管理 UI | REQ-06,07,09 | 既有 Issue service/query/allowance/outbox | Domain/Application、API/SQL、Vue | L／中 |
| WP-07 | Customer Cart/Checkout/Payment/Invoice/Refund UI | REQ-07 | WP-02～04、既有 Order/Return routes | Vue unit/typecheck/lint/build | L／中 |
| WP-08 | INT-02 SQL＋Playwright E2E | REQ-08 | WP-01～07 | isolated SQL DB、Playwright traces/screenshots | L／高（環境／跨模組） |
| WP-09 | 校正進度文件與 Gate | REQ-09 | 每個 WP 完成證據 | 文件 diff 對照 OpenAPI／測試 | S／低 |

執行波次：

```text
Wave 1: WP-01
Wave 2: WP-02 -> WP-03 -> WP-04
Wave 3: WP-05 || WP-06
Wave 4: WP-07
Wave 5: WP-08 -> WP-09
```

只有明確不改相同檔案且契約穩定的 WP-05／WP-06 可平行；目前由單一接手者依序執行。

## 8. 驗證與 Gate

| Gate | 必要證據 |
|---|---|
| GATE-01 WP checkpoint | 聚焦測試先通過；相關專案 build 0 warning；final diff 無秘密、debug、平行 DTO 或無關格式變更 |
| GATE-02 API contract | OpenAPI 與 generated TypeScript 無 drift；舊客戶端仍可使用既有 route |
| GATE-03 SQL Server | 真實 SQL Server 證明交易、約束、併發、冪等與 rollback；不得以 InMemory/SQLite 代替 |
| GATE-04 Security | Owner／Policy／MFA／Guest Scope 正負向案例；Secret、Email、Internal Id 不進 log/response/snapshot |
| GATE-05 E2E | 顧客 Cart→Checkout→Payment→Order/Invoice 與管理端 Refund/Invoice 主要旅程可重跑 |
| GATE-06 Merge readiness | Required CI、獨立 review、exact remote head 測試完成後才可 Approve／Merge |

Definition of Done：REQ-01～09 全部有實作與對應證據；沒有未處理 P0/P1 review finding；無 Schema／Production mutation；進度文件與 `dev` 一致。

No-Go：任何金額／身份規則不明、需要新 Migration、跨模組資料未經公開介面、HMAC Secret 被保存或輸出、SQL Server／E2E 無法證明關鍵交易時，不宣告完成。

## 9. 執行記錄

| 日期 | 工作包 | 狀態 | 證據／下一步 |
|---|---|---|---|
| 2026-09-01 | WP-01 | 進行中 | 已確認 Checkout 使用 `SHA256(GuestCartKey)` 與 DEC-P262 不符；開始改為 V1 Secret + 正規化訂單 Email HMAC |
| 2026-09-01 | WP-01 | 完成（已推送） | HMAC 單元測試 4/4、Checkout/HMAC 聚焦測試 12/12（含 SQL Server）、Solution Build 0 warning；隨 `e77edfbb` 推送至 `origin/dev` |
| 2026-09-01 | WP-03 | 進行中（A1） | 已裁定遵守 Endpoint 目錄的 `201 OrderDto`；擴充 Checkout transaction/replay projection，讓首次建立與冪等重播回傳相同且遮蔽安全的完整 DTO |
| 2026-09-01 | WP-03 | 完成（已推送） | 新增正式 `POST /api/v1/orders`；Member/Guest actor、Idempotency-Key 與完整 `OrderDto` 已接線；首次／replay 共用單一 mapper；API 2/2、Application 2/2、Checkout/HMAC SQL 12/12、既有 Orders SQL 7/7、build/format/typecheck 全綠；隨 `e77edfbb` 推送至 `origin/dev` |
| 2026-09-01 | WP-04 | 完成（已推送） | 新增 `POST /api/v1/orders/{id}/payment-attempts`、Owner actor、中央冪等、Serializable Writer、可信訂單金額與 RowVersion；SQL 3/3（含跨會員負向）、API 2/2、Application 14/14，既有模擬付款 Writer 回歸亦全綠；SQL `datetime2` UTC 邊界已由 Reader 正規化；隨 `e77edfbb` 推送至 `origin/dev` |
| 2026-09-01 | WP-02 | 外部依賴待處理 | `ICartCouponLineReader` 明定由 Terry-owned Shopping Infrastructure 實作；本接手不跨 owner 直接查 Shopping／Catalog 表，待該窄介面落地後接 Controller／DI／OpenAPI |
| 2026-09-01 | WP-05 | 完成（未提交） | 新增退款分頁／篩選清單與明細 API、A-21/A-22、角色路由、可信分攤正負顯示、TOTP 提示、確認門檻與穩定 Idempotency-Key；Application 5/5、API 26/26、SQL Server 1/1、Refund 白名單 2/2、Vue 聚焦 5/5、router 組合 24/24、typecheck/lint/build 全綠；OpenAPI／Typed Client 已同步，待 commit／push |

## 10. 待裁定

### DEC-EXEC-01｜Checkout 建單回應契約

**裁定：A1（2026-09-01）**。維持正式 public contract，不以文件降級配合現有 slim result；實作須重用既有 Orders DTO 與遮蔽規則，且不得要求訪客先取得額外權限才能收到建單結果。

| 選項 | 內容 | 成本／影響 |
|---|---|---|
| A1（建議） | 遵守現行 Endpoint 目錄，擴充 Checkout transaction/replay 的結果為完整且遮蔽安全的 `OrderDto` | 契約一致、前端一次取得完整訂單；需要擴充既有 Gateway replay projection 與測試，工作量較高 |
| A2 | 保留既有 `CheckoutCreatedOrder` 作 201 Response，並更新 Endpoint／DTO 文件 | 最低程式成本，但會改變已定版 public contract；顧客端需另設計如何取得完整訂單，訪客尤其受影響 |

裁定已由 WP-03 落地：`POST /api/v1/orders`、transaction result、冪等 replay、OpenAPI／Typed Client 與 API／SQL 測試均完成，並隨 `e77edfbb` 推送至 `origin/dev`。
