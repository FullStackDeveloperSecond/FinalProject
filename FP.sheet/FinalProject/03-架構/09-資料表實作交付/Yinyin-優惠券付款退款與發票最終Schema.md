---
文件狀態: 已確認
最後更新: 2026-08-19
負責人: yinyin
追蹤項目:
  - DES-19
  - DES-21
  - DES-22
依據決策:
  - DEC-P250
  - DEC-P261
  - DEC-P262
  - DEC-P271
  - DEC-P272
  - DEC-P275
  - DEC-P276
  - DEC-P278
  - DEC-P279
  - DEC-P280
  - DEC-P285
---

# Yinyin｜優惠券、付款、部分退款與模擬發票最終 Schema 實作交付

> 依據：原上繳修正版、DEC-BATCH-012、DEC-BATCH-014 與正式領域資料字典。
>
> 範圍：M-07 優惠券與促銷計算、M-09 模擬付款、M-13 部分退款、M-20 模擬發票與折讓、INT-02 跨模組協調
>
> 本版針對重新繳交驗收條件再次修正：
> 1. 明確定義所有 Entity 的共同欄位基線。
> 2. 補強最後一個優惠名額的併發控制。
> 3. 每一個 FK 明確標示 `Delete Behavior = Restrict`。
> 4. 不建立 Cart、Shipment、SalePrices、Promotions 的第二套真實來源。

> 權威順序：本文件是 Owner 欄位級實作交付；若與 [[03-架構/03-資料與一致性/資料字典索引]]、[[03-架構/03-資料與一致性/資料字典-購物交易與售後]]、[[03-架構/03-資料與一致性/狀態機設計]] 或 API 正式目錄衝突，以正式文件為準。本文件完成不代表核准建立或套用 Migration。

---

# 一、共同欄位基線

本專案 Entity 統一採以下基線：

| 類型 | 統一規則 |
|---|---|
| 內部主鍵 | `Id bigint IDENTITY(1,1)` |
| 對外資源識別 | `PublicId uniqueidentifier NOT NULL UNIQUE` |
| 建立時間 | `CreatedAtUtc datetime2(3) NOT NULL`，由應用層寫入 UTC |
| 更新時間 | `UpdatedAtUtc datetime2(3) NOT NULL`，所有 MutableEntity 必填 |
| 併發控制 | `RowVersion rowversion`，可修改／有狀態轉移的 Entity 必須使用 |
| 金額 | `decimal(18,2)` |
| Identity 使用者 FK | `nvarchar(450)` |
| FK 刪除行為 | 預設 `Delete Behavior = Restrict` |
| 時區 | 一律 UTC，不使用 `SYSDATETIME()` 當共同預設 |

## 共同欄位適用說明

為符合專案共同基線，同時保留資料模型語意：

- 一般 Aggregate / 可修改 Entity：需包含 `Id`、`PublicId`、`CreatedAtUtc`、`UpdatedAtUtc`、`RowVersion`。
- Append-only Event / 不可變快照：需包含 `Id`、`PublicId`、`CreatedAtUtc`；不回寫既有內容，因此可不使用 `UpdatedAtUtc`。
- 純 Junction Table：採 Composite PK，不另外建立 `Id` / `PublicId`；若專案 BaseEntity 強制要求所有 Entity 一律具備，則實作階段依共用基底補齊。
- 所有 FK 均採 `Restrict`，避免刪除父資料時連帶破壞歷史交易資料。

---

# 二、M-07 優惠券與促銷計算

## 1. Coupons

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 內部主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| Code | `nvarchar(64)` | NO | — | — | UNIQUE | 優惠碼，Trim＋NFKC＋統一大寫 |
| NameZhTw | `nvarchar(160)` | NO | — | — | INDEX | 中文名稱 |
| DiscountType | `varchar(16)` | NO | — | — | INDEX | `FixedAmount`、`Percentage`、`FreeShipping`、`AssemblyFreeShipping` |
| DiscountValue | `decimal(18,2)` | YES | `NULL` | — | — | 固定金額／百分比數值；免運型可為 Null |
| MinimumSpend | `decimal(18,2)` | YES | `NULL` | — | — | 最低消費門檻 |
| MaximumDiscount | `decimal(18,2)` | YES | `NULL` | — | — | 百分比券最大折抵 |
| StartsAtUtc | `datetime2(3)` | NO | — | — | INDEX | 開始時間 |
| EndsAtUtc | `datetime2(3)` | NO | — | — | INDEX | 結束時間 |
| TotalUsageLimit | `int` | YES | `NULL` | — | — | 全站總名額 |
| PerMemberLimit | `int` | YES | `NULL` | — | — | 每會員使用上限 |
| MemberOnly | `bit` | NO | `0` | — | INDEX | 是否限會員 |
| ExcludeSaleItems | `bit` | NO | `0` | — | — | 是否排除 SalePrices 商品 |
| ScopeType | `varchar(16)` | NO | `'All'` | — | INDEX | `All` / `Restricted` |
| Status | `varchar(16)` | NO | `'Draft'` | — | INDEX | Draft、Scheduled、Active、Paused、Expired、Exhausted、Disabled |
| RuleVersion | `int` | NO | `1` | — | — | 規則版本 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | INDEX | 建立時間 |
| UpdatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 修改時間 |
| RowVersion | `rowversion` | NO | DB 自動 | — | — | 併發控制 |

### 限制
- `EndsAtUtc > StartsAtUtc`
- `ScopeType IN ('All','Restricted')`
- `Status IN ('Draft','Scheduled','Active','Paused','Expired','Exhausted','Disabled')`
- `DiscountType IN ('FixedAmount','Percentage','FreeShipping','AssemblyFreeShipping')`
- 固定金額 `DiscountValue >= 0`；百分比券固定使用 0～1，不使用 0～100
- `TotalUsageLimit > 0` 或 Null
- `PerMemberLimit > 0` 或 Null
- `ScopeType='Restricted'` 時，至少需一筆 `CouponCategories` 或 `CouponProducts`

---

## 2. CouponRedemptions

> 取代舊版 `CouponUsages`，處理名額「保留 → 釋放 → 消耗」。

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| CouponId | `bigint` | NO | — | FK → Coupons.Id | INDEX | Delete Behavior = Restrict |
| OrderId | `bigint` | NO | — | 跨模組 FK → Orders.Id | UNIQUE / INDEX | 只在 Checkout 建單交易建立；Restrict |
| MemberUserId | `nvarchar(450)` | YES | `NULL` | FK → AspNetUsers.Id | INDEX | 訪客可為 Null；Restrict |
| GuestUsageKeyHash | `binary(32)` | YES | `NULL` | — | INDEX | HMAC-SHA-256 原始 32 bytes；不得保存 Email 副本 |
| Status | `varchar(16)` | NO | `'Reserved'` | — | INDEX | Reserved、Released、Consumed、Expired |
| ReservedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | INDEX | 保留時間 |
| ReleasedAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 釋放時間 |
| ConsumedAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 消耗時間 |
| ExpiresAtUtc | `datetime2(3)` | YES | `NULL` | — | INDEX | 保留逾時 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |
| UpdatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 更新時間 |
| RowVersion | `rowversion` | NO | DB 自動 | — | — | 狀態轉移併發控制 |

### 建議索引
- `(CouponId, Status)`
- `(CouponId, MemberUserId, Status)`
- `(CouponId, GuestUsageKeyHash, Status)`
- `UX_CouponRedemptions_CouponId_OrderId`

### Checkout 與訪客識別不變量

- `MemberUserId` 與 `GuestUsageKeyHash` 必須恰一非 Null；會員不得同時保存 Guest Hash。
- `CouponRedemptions` 只在 Checkout 建立 Order 的同一 SQL Transaction 建立，不在購物車階段占用名額。
- 訪客 Hash＝以伺服器 Secret Version 1 對正規化訂單 Email 計算 HMAC-SHA-256；Secret 不進儲存庫，Hash 不回傳且不可反解。

---

## 3. OrderCoupons

> 不可變訂單優惠快照；退款不得回查目前 `Coupons` 重算。

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| OrderId | `bigint` | NO | — | 跨模組 FK → Orders.Id | UNIQUE / INDEX | 每張訂單最多一筆；Restrict |
| CouponId | `bigint` | YES | `NULL` | FK → Coupons.Id | INDEX | Restrict |
| RedemptionId | `bigint` | YES | `NULL` | FK → CouponRedemptions.Id | UNIQUE / INDEX | Restrict |
| CouponCodeSnapshot | `nvarchar(64)` | NO | — | — | — | 優惠碼快照 |
| NameSnapshot | `nvarchar(160)` | NO | — | — | — | 名稱快照 |
| DiscountType | `varchar(16)` | NO | — | — | — | 類型快照 |
| RuleVersion | `int` | NO | — | — | — | 規則版本快照 |
| DiscountValue | `decimal(18,2)` | YES | `NULL` | — | — | 優惠值快照；百分比使用 0～1 |
| MinimumSpendAmount | `decimal(18,2)` | YES | `NULL` | — | — | 下單時最低消費門檻；Null 表示無門檻 |
| AppliedAmount | `decimal(18,2)` | NO | `0` | — | — | 實際折抵 |
| EligibleSubtotal | `decimal(18,2)` | NO | `0` | — | — | 適用商品小計 |
| IsFreeShipping | `bit` | NO | `0` | — | — | 是否實際免運 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

`UX_OrderCoupons_OrderId` 必須實體建立。若 `RedemptionId` 有值，其 `OrderId`／`CouponId` 必須與本快照相符，由同一交易驗證與整合測試保證。

最低消費只比對商品特價後、優惠券折扣前的適用商品小計。部分退貨使用 `MinimumSpendAmount` 與 Haru `OrderItems.IsCouponEligible` 快照，不回查目前 Coupon 或商品分類；既有 Initial Migration 尚未包含兩欄，依 DES-21 建立後續 Migration。

---

## 4. CouponCategories

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| CouponId | `bigint` | NO | — | PK, FK → Coupons.Id | Composite PK | Restrict |
| CategoryId | `bigint` | NO | — | PK, 跨模組 FK → Categories.Id | Composite PK / INDEX | Restrict |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

Composite PK：`(CouponId, CategoryId)`

---

## 5. CouponProducts

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| CouponId | `bigint` | NO | — | PK, FK → Coupons.Id | Composite PK | Restrict |
| ProductId | `bigint` | NO | — | PK, 跨模組 FK → Products.Id | Composite PK / INDEX | Restrict |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

Composite PK：`(CouponId, ProductId)`

---

## 6. CouponExcludedProducts

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| CouponId | `bigint` | NO | — | PK, FK → Coupons.Id | Composite PK | Restrict |
| ProductId | `bigint` | NO | — | PK, 跨模組 FK → Products.Id | Composite PK / INDEX | Restrict |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

### 範圍規則
- 排除商品優先
- 同商品不得同時存在 `CouponProducts` 與 `CouponExcludedProducts`
- 驗證與寫入需於同一 Transaction 完成
- `ScopeType=All` 不建立包含範圍
- `ScopeType=Restricted` 至少需一筆分類或商品

---

# 三、優惠券最後一個名額的併發控制

不得只做：

```text
SELECT COUNT(...)
↓
INSERT CouponRedemption
```

因為兩個交易可能同時讀到「剩 1 張」。

## 正式策略

採 **Serializable Transaction 或等價的條件式原子操作**。

### 建議流程

```text
BEGIN TRANSACTION
IsolationLevel = Serializable
        ↓
鎖定指定 Coupon 的名額計算範圍
        ↓
重新計算：
Consumed
+
尚未過期的 Reserved
        ↓
若 >= TotalUsageLimit
    → 失敗，回傳 409 Conflict
否則
    → INSERT CouponRedemption(Status='Reserved')
        ↓
COMMIT
```

補充：

- `RowVersion` 用於 Coupon 本身與 Redemption 狀態轉移的 optimistic concurrency。
- 「最後一個名額」不能只依賴 `CouponRedemptions.RowVersion`，因為競爭雙方新增的是不同資料列。
- 最終是否成功取得名額，以資料庫交易結果為準，不以前端剩餘數量顯示為準。

---

# 四、M-09 模擬付款

## 7. PaymentAttempts

> 每次付款嘗試新增一筆，不覆蓋舊失敗紀錄。

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| OrderId | `bigint` | NO | — | 跨模組 FK → Orders.Id | INDEX | Restrict |
| Method | `varchar(24)` | NO | — | — | INDEX | CreditCard、ATM、ConvenienceCode、CashOnDelivery、LinePay、ApplePay、GooglePay |
| Status | `varchar(24)` | NO | `'Pending'` | — | INDEX | Pending、Processing、AwaitingPayment、Paid、Failed、Expired、Cancelled |
| Amount | `decimal(18,2)` | NO | — | — | — | 本次付款金額 |
| ProviderCode | `nvarchar(64)` | YES | `NULL` | — | INDEX | 模擬金流／通路代碼 |
| ExternalReference | `nvarchar(128)` | YES | `NULL` | — | UNIQUE / INDEX | 外部交易／ATM／超商代碼 |
| InstructionExpiresAtUtc | `datetime2(3)` | YES | `NULL` | — | INDEX | ATM／超商代碼期限 |
| PaidAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 付款成功時間 |
| FailedAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 付款失敗時間 |
| FailureCode | `nvarchar(64)` | YES | `NULL` | — | INDEX | 失敗代碼 |
| IdempotencyKey | `nvarchar(128)` | NO | — | — | UNIQUE | 防重複付款請求 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | INDEX | 建立時間 |
| UpdatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 更新時間 |
| RowVersion | `rowversion` | NO | DB 自動 | — | — | 併發控制 |

`InstructionExpiresAtUtc` 取付款方式期限與 Order 原 `PaymentDueAtUtc` 較早者；即時付款最長 15 分鐘，ATM／超商代碼最長 3 天，重試不得延長訂單期限。COD 不建立線上付款指示期限。

---

## 8. PaymentEvents

> Append-only，每次模擬回呼新增，不覆蓋。

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| PaymentAttemptId | `bigint` | NO | — | FK → PaymentAttempts.Id | INDEX | Restrict |
| ExternalEventId | `nvarchar(128)` | NO | — | — | UNIQUE | 防止重複回呼 |
| EventType | `nvarchar(64)` | NO | — | — | INDEX | 事件類型 |
| OccurredAt | `datetimeoffset(3)` | NO | — | — | INDEX | 外部事件原始時間與時區偏移 |
| ReceivedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | INDEX | 系統接收時間 |
| PayloadHash | `binary(32)` | NO | — | — | INDEX | SHA-256 原始 32 bytes |
| PayloadSummaryJson | `nvarchar(4000)` | YES | `NULL` | — | — | 必要摘要，不保存卡號、個資或敏感原文 |
| ProcessingStatus | `varchar(24)` | NO | `'Received'` | — | INDEX | Received、Processed、IgnoredDuplicate、Failed |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

### 重試與重複回呼
- 每次重試建立新的 `PaymentAttempt`
- 每次回呼新增 `PaymentEvent`
- `ExternalEventId` 必須 Unique
- 已存在同一 `ExternalEventId` 時不可再次變更付款狀態

---

# 五、ATM／超商代碼與 COD 狀態規則

## ATM／超商代碼

- `InstructionExpiresAtUtc` 保存付款指示期限
- 訂單另有自己的付款期限
- 真正可付款期限取兩者較早者
- 逾期：
  - `PaymentAttempt → Expired`
  - Order 模組依合法狀態處理
  - Coupon Reservation 合法釋放
  - Inventory Reservation 由 Inventory 模組釋放

## CashOnDelivery

- 建立 `PaymentAttempt(Method=CashOnDelivery)`
- 出貨前維持待收款
- 配送成功且模擬收款完成後才標記 `Paid`
- 配送失敗先由 Order / Shipment 決定退回、重送或取消
- 最終合法取消後，才釋放 Coupon / Inventory Reservation

---

# 六、M-13 部分退款

## 9. Refunds

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| OrderId | `bigint` | NO | — | 跨模組 FK → Orders.Id | INDEX | Restrict |
| ReturnRequestId | `bigint` | YES | `NULL` | 跨模組 FK → ReturnRequests.Id | INDEX | Restrict |
| PaymentAttemptId | `bigint` | NO | — | FK → PaymentAttempts.Id | INDEX | Restrict |
| RefundNumber | `nvarchar(32)` | NO | — | — | UNIQUE | 退款編號 |
| Status | `varchar(24)` | NO | `'PendingReview'` | — | INDEX | PendingReview、Approved、Rejected、Processing、Succeeded、Failed、Cancelled |
| RequestedAmount | `decimal(18,2)` | NO | — | — | — | 申請退款 |
| ApprovedAmount | `decimal(18,2)` | YES | `NULL` | — | — | 核准金額 |
| SucceededAmount | `decimal(18,2)` | YES | `NULL` | — | — | 實際成功退款 |
| ReasonCode | `varchar(64)` | NO | — | — | INDEX | 原因 |
| RequestedBy | `nvarchar(450)` | YES | `NULL` | FK → AspNetUsers.Id | INDEX | 系統／訪客代理流程可為 Null；Restrict |
| ApprovedBy | `nvarchar(450)` | YES | `NULL` | FK → AspNetUsers.Id | INDEX | Restrict |
| ExecutedByAdminUserId | `nvarchar(450)` | YES | `NULL` | FK → AspNetUsers.Id | INDEX | Restrict |
| IdempotencyKey | `nvarchar(128)` | NO | — | — | UNIQUE | 防重複退款 |
| SucceededAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 成功退款時間 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | INDEX | 建立時間 |
| UpdatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 更新時間 |
| RowVersion | `rowversion` | NO | DB 自動 | — | — | 併發控制 |

### 規則
- 退貨核准 ≠ 退款執行
- 一張訂單可多次部分退款
- 成功退款累計不得超過原已付款金額
- 相同 `IdempotencyKey` 不可重複退款

---

## 10. RefundAllocations

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| RefundId | `bigint` | NO | — | FK → Refunds.Id | INDEX | Restrict |
| OrderItemId | `bigint` | YES | `NULL` | 跨模組 FK → OrderItems.Id | INDEX | Restrict |
| AllocationType | `varchar(24)` | NO | — | — | INDEX | ItemRefund、DiscountClawback、ShippingClawback、OriginalShipping、ReturnShipping、AssemblyFee；OtherAdjustment 第一版禁止寫入 |
| Amount | `decimal(18,2)` | NO | — | — | — | 此分攤金額 |
| OriginalDiscountAllocation | `decimal(18,2)` | NO | `0` | — | — | 原始優惠分攤 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

### 規則
- `Amount > 0`，不使用正負號表達方向。
- 增加退款：ItemRefund、OriginalShipping、ReturnShipping、AssemblyFee。
- 從退款扣回：DiscountClawback、ShippingClawback。
- 增加型合計－扣回型合計 = 核准／成功退款金額；優惠追回、運費、退貨運費與組裝費必須獨立表達。

---

# 七、退款核准與執行流程

```text
ReturnRequest 核准
    ↓
建立 Refund(Status=PendingReview)
    ↓
人工審核
    ↓
Refund.Status = Approved
    ↓
另一個執行操作
    ↓
退款執行
    ↓
Succeeded / Failed
```

- `ApprovedBy` 保存核准者
- `ExecutedByAdminUserId` 保存實際退款執行者
- 兩者不可混為同一概念

---

# 八、M-20 模擬發票與折讓

> 計算契約（DEC-P280、DEC-P285）：`BusinessTaxRate = 0.05m`。發票表頭 `IssuedAmount = Round(Sum(Line.GrossAmount), 0, AwayFromZero)`、`NetAmount = Round(IssuedAmount / 1.05, 0, AwayFromZero)`、`TaxAmount = IssuedAmount - NetAmount`，三者為整數元；發票明細可保留兩位小數，並依三條核對口徑與表頭一致。欄位維持 `decimal(18,2)`，尾差由表頭總額與明細加總推導，不新增欄位。折讓仍依 DEC-P280 的整數元規則。

## 11. SimulatedInvoices

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| OrderId | `bigint` | NO | — | 跨模組 FK → Orders.Id | UNIQUE / INDEX | 每張訂單最多一張；Restrict |
| InvoiceNumber | `nvarchar(32)` | NO | — | — | UNIQUE | 模擬發票號碼 |
| BuyerType | `varchar(20)` | NO | — | — | INDEX | Individual、Company |
| BuyerEmail | `nvarchar(320)` | YES | `NULL` | — | — | 必要時保存 |
| CarrierType | `varchar(30)` | YES | `NULL` | — | — | 模擬載具類型 |
| CarrierValueMasked | `nvarchar(100)` | YES | `NULL` | — | — | 載具遮罩 |
| CompanyTaxId | `varchar(20)` | YES | `NULL` | — | INDEX | 統編 |
| CompanyName | `nvarchar(200)` | YES | `NULL` | — | — | 公司抬頭 |
| NetAmount | `decimal(18,2)` | NO | — | — | — | 未稅 |
| TaxAmount | `decimal(18,2)` | NO | — | — | — | 稅額 |
| IssuedAmount | `decimal(18,2)` | NO | — | — | — | 模擬開立含稅總額 |
| Currency | `char(3)` | NO | `'TWD'` | — | — | 幣別 |
| Status | `varchar(16)` | NO | `'Pending'` | — | INDEX | Pending、Issued、Voided、PartiallyAllowed、FullyAllowed |
| IssuedAtUtc | `datetime2(3)` | YES | `NULL` | — | INDEX | 開立時間 |
| VoidedAtUtc | `datetime2(3)` | YES | `NULL` | — | — | 作廢時間 |
| DemoMarker | `nvarchar(32)` | NO | `'DEMO-NOT-A-TAX-INVOICE'` | — | INDEX | 固定值；明示非真實稅務憑證 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |
| UpdatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 更新時間 |
| RowVersion | `rowversion` | NO | DB 自動 | — | — | 併發控制 |

---

## 12. SimulatedInvoiceItems

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| SimulatedInvoiceId | `bigint` | NO | — | FK → SimulatedInvoices.Id | INDEX | Restrict |
| OrderItemId | `bigint` | YES | `NULL` | 跨模組 FK → OrderItems.Id | INDEX | Restrict |
| ProductNameSnapshot | `nvarchar(200)` | NO | — | — | — | 商品快照 |
| SkuCodeSnapshot | `varchar(100)` | NO | — | — | INDEX | SKU 快照 |
| Quantity | `int` | NO | — | — | — | 數量 |
| UnitPrice | `decimal(18,2)` | NO | — | — | — | 單價 |
| DiscountAmount | `decimal(18,2)` | NO | `0` | — | — | 折扣 |
| NetAmount | `decimal(18,2)` | NO | — | — | — | 未稅小計 |
| TaxAmount | `decimal(18,2)` | NO | — | — | — | 稅額 |
| GrossAmount | `decimal(18,2)` | NO | — | — | — | 含稅小計 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

---

## 13. SimulatedInvoiceAllowances

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| SimulatedInvoiceId | `bigint` | NO | — | FK → SimulatedInvoices.Id | INDEX | Restrict |
| RefundId | `bigint` | NO | — | FK → Refunds.Id | UNIQUE / INDEX | 每筆 Refund 最多一筆折讓；Restrict |
| AllowanceNumber | `nvarchar(32)` | NO | — | — | UNIQUE | 折讓編號 |
| NetAmount | `decimal(18,2)` | NO | — | — | — | 折讓未稅 |
| TaxAmount | `decimal(18,2)` | NO | — | — | — | 折讓稅額 |
| Amount | `decimal(18,2)` | NO | — | — | — | 折讓含稅總額 |
| IssuedAtUtc | `datetime2(3)` | NO | — | — | INDEX | 開立時間 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

---

## 14. SimulatedInvoiceAllowanceItems

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|---|---|---|---|---|
| Id | `bigint IDENTITY(1,1)` | NO | — | PK | Clustered | 主鍵 |
| PublicId | `uniqueidentifier` | NO | 應用層產生 | — | UNIQUE | 對外識別 |
| AllowanceId | `bigint` | NO | — | FK → SimulatedInvoiceAllowances.Id | INDEX | Restrict |
| SimulatedInvoiceItemId | `bigint` | NO | — | FK → SimulatedInvoiceItems.Id | INDEX | Restrict |
| Quantity | `int` | NO | — | — | — | 折讓數量 |
| NetAmount | `decimal(18,2)` | NO | — | — | — | 未稅 |
| TaxAmount | `decimal(18,2)` | NO | — | — | — | 稅額 |
| GrossAmount | `decimal(18,2)` | NO | — | — | — | 折讓總額 |
| CreatedAtUtc | `datetime2(3)` | NO | 應用層寫入 | — | — | 建立時間 |

---

# 九、發票與折讓歷史保存規則

- 訂單達開立條件後建立 `SimulatedInvoices`
- 商品快照寫入 `SimulatedInvoiceItems`
- 整張發票需撤銷時使用 Void，不刪除
- 部分退款時建立 `SimulatedInvoiceAllowances`
- 折讓明細建立於 `SimulatedInvoiceAllowanceItems`
- 不得因退款回寫或刪除原始發票金額與明細
- 發票與折讓皆保留完整歷史
- 發票固定採 5% 稅率；表頭 Gross／Net／Tax 為 TWD 整數元，例如含稅 1,000 固定保存未稅 952、稅額 48、含稅 1,000
- 發票明細可保留兩位小數，每筆 Gross=Net+Tax 且不得為負；依 `Round(Sum(Gross))=Header.Issued`、`Round(Sum(Net))=Header.Net`、`Sum(Tax)=Header.Tax` 核對，最後一筆合法明細吸收稅額尾差
- 折讓仍依 DEC-P280 的 5% 整數元與尾差規則；小數發票明細的部分折讓若需不同取位規則，另案裁定

---

# 十、已移除／不得重複建立的真實來源

本工作包不得建立：

- `ShoppingCarts`
- `ShoppingCartItems`
- `ShippingMethods`
- `Shipments`
- `Promotions`
- `PromotionProducts`
- SKU 第二套 SalePrice 欄位

責任邊界：

| 能力 | 唯一真實來源 |
|---|---|
| 購物車 | Cart 模組 |
| 商品特價 | `SalePrices` |
| 訂單 | Order 模組 |
| 庫存 | Inventory 模組 |
| 物流 | Shipment 模組 |
| 退貨申請 | ReturnRequest 模組 |
| 優惠券 | 本工作包 |
| 付款 | 本工作包 |
| 退款 | 本工作包 |
| 模擬發票／折讓 | 本工作包 |

---

# 十一、正式優惠計算順序

1. 商品原價
2. 商品特價（由 `SalePrices` 提供）
3. 判斷優惠券適用商品
4. 以適用商品小計比對最低消費並套用固定金額或百分比優惠券
5. 加入每台 NT$300 組裝費
6. 判斷配送方式與滿額免運
7. 套用免運券
8. 產生最終總額

> 不包含會員點數。

---

# 十二、INT-02 跨模組協調

本工作包透過 Service / API / Domain Contract 協調：

```text
Cart
Order
Inventory
SalePrices
Shipment
ReturnRequest
AuditLog
Outbox
IdempotencyRecords
```

原則：

- 不直接建立第二套對方資料表
- 不跨模組任意直接寫入對方表
- 本地交易成功後，如需跨模組通知，透過 Outbox 發送事件
- 高風險操作寫 AuditLog
- 重試操作透過 IdempotencyRecords / 業務唯一鍵防重複

---

# 十三、Outbox、AuditLog、IdempotencyRecords

## Outbox
適用事件：

- PaymentSucceeded
- PaymentExpired
- CouponReserved
- CouponReleased
- CouponConsumed
- RefundSucceeded
- InvoiceIssued
- InvoiceAllowanceIssued

先與本地資料交易一同寫入 Outbox，再由背景工作發布。

## AuditLog
記錄：

- 優惠券啟用／停用
- 人工退款核准
- 退款執行
- 發票作廢
- 折讓建立

## IdempotencyRecords
適用：

- 建立付款
- 退款
- 其他高風險重試操作

---

# 十四、資料表總覽

| 模組 | 資料表 |
|---|---|
| 優惠券 | Coupons |
|  | CouponRedemptions |
|  | OrderCoupons |
|  | CouponCategories |
|  | CouponProducts |
|  | CouponExcludedProducts |
| 付款 | PaymentAttempts |
|  | PaymentEvents |
| 部分退款 | Refunds |
|  | RefundAllocations |
| 模擬發票 | SimulatedInvoices |
|  | SimulatedInvoiceItems |
| 模擬折讓 | SimulatedInvoiceAllowances |
|  | SimulatedInvoiceAllowanceItems |

**本工作包共 14 張資料表。**

---

# 十五、重新繳交驗收條件對照

- [x] 只保留自己負責的資料表及跨模組引用
- [x] 核心 Entity 採 `bigint Id`
- [x] 對外 Entity 採 `PublicId uniqueidentifier`
- [x] 時間採 UTC `datetime2(3)`
- [x] 可修改 Entity 採 `UpdatedAtUtc`
- [x] 可修改／狀態轉移 Entity 採 `RowVersion`
- [x] 金額統一 `decimal(18,2)`
- [x] Identity FK 使用 `nvarchar(450)`
- [x] 所有 FK 預設 `Delete Behavior = Restrict`
- [x] M-07：Coupons、CouponRedemptions、OrderCoupons 完整
- [x] 優惠券 Scope：All／Restricted、分類、商品、排除商品完整
- [x] 最後一個優惠名額具 Serializable／原子併發控制策略
- [x] M-09：PaymentAttempts＋PaymentEvents 完整
- [x] `ExternalEventId` 防止重複回呼
- [x] M-13：Refunds＋RefundAllocations 完整
- [x] 退款核准與執行分離
- [x] 多次部分退款與累計上限規則完整
- [x] M-20：發票、發票明細、折讓、折讓明細完整
- [x] 發票退款採折讓，不回寫原發票
- [x] 無會員點數
- [x] 無 Promotions 第二套價格來源
- [x] 無 Cart / Shipment 第二套真實來源
- [x] 已補跨模組、Outbox、AuditLog、Idempotency 關係
- [ ] 依 DEC-P276～P278 補入 `OrderCoupons.MinimumSpendAmount`、`OrderItems.IsCouponEligible`、`ShippingClawback` 及方向公式的 Entity／Configuration／測試與後續 Migration（DES-21）
- [ ] 依 DEC-P279～P280、DEC-P285 補齊發票 Endpoint／DTO／錯誤碼、5% 表頭整數元、明細兩位小數、付款前整數化、三條核對口徑及尾差測試（DES-22）；本文件不代表 Controller／OpenAPI 已完成

---

# 十六、實作與 Migration Gate

- Coupon 名額取得、Order、OrderCoupon 與 CouponRedemption 必須在 Checkout 同一 SQL Transaction 完成；失敗時全部回滾。
- Payment Event、Refund Execute 與 Allowance 建立必須以資料庫唯一鍵及 Idempotency-Key 抵抗併發重送，不得只採先查再新增。
- 跨模組只依公開 Application Query／DTO 取得 Order、Cart、Return、SKU 預付與 ShippingMethod 摘要，不共享 Repository／DbContext。
- MutableEntity 的 `UpdatedAtUtc` 必須 NOT NULL；Append-only Entity 不得後改事件內容。
- 本文件完成只關閉 DES-19 的「Schema 文件」缺口；建立 Migration 前仍須完成 Entity、Configuration、跨模組 FK、交易／冪等測試清單及獨立 Migration Review。
- DEC-BATCH-014 在 Initial Migration 後補強訂單優惠快照；DES-21 完成前，現有資料庫模型不得宣稱已支援退貨後優惠門檻重算。
