---
文件狀態: 已確認
最後更新: 2026-08-28
負責人: haru
覆核人: yinyin
最終整合驗收: alex
追蹤項目:
  - DES-20
  - DES-21
依據決策:
  - DEC-BATCH-012
  - DEC-BATCH-013
  - DEC-P60
  - DEC-P277
---

# Haru｜會員、登入、訂單與訪客存取最終 Schema 實作交付

本文件收束 Haru 兩次上交內容、驗收報告及 DEC-P263～DEC-P270，作為會員 Identity／Profile、地址、收藏、訂單、訪客訂單存取與組裝工作的欄位級實作依據。

> [!important]
> alex 已完成決策與跨文件整合；固定備援人 `yinyin` 已完成欄位、索引、狀態、授權、交易與跨模組交叉覆核。Schema 文件完成不代表 Entity、Configuration、測試或 Migration 已完成；若本文件與正式資料字典、狀態機或 API 契約衝突，以正式文件為準並回報，不得由實作者自行選擇。

## 1. 共通基線

| Profile | 固定欄位 |
|---|---|
| `Entity` | `Id bigint IDENTITY(1,1)` PK、`PublicId uniqueidentifier NOT NULL` UX（應用層產生 UUID v7）、`CreatedAtUtc datetime2(3) NOT NULL` |
| `MutableEntity` | `Entity` 全部欄位＋`UpdatedAtUtc datetime2(3) NOT NULL`＋`RowVersion rowversion NOT NULL` |

- Identity FK 使用 `nvarchar(450)`；所有 FK 預設 `Restrict`。
- 時間一律 UTC `datetime2(3)`；應用層明確寫入，不使用隱藏現在時間 Default。
- 對外只使用 PublicId；不得在 API、URL、跨模組 DTO 或一般 Log 暴露 bigint Id。
- Email 使用 Identity Normalizer；其他文字輸入 Trim＋Unicode NFKC。
- 本文件沒有 Cascade 白名單資料表。

## 2. ASP.NET Core Identity 與 ApplicationUsers

`AspNetUsers`、`AspNetRoles`、`AspNetUserRoles`、Claims、Logins、Tokens 及 RoleClaims 由 Identity Migration 產生，本工作包不得建立第二套帳密、角色、Session 或 Refresh Token 資料表。

### 2.1 ApplicationUsers 擴充欄位

| 欄位 | 型別 | 規則 |
|---|---|---|
| `PublicId` | `uniqueidentifier` | UX；UUID v7；API 唯一公開識別 |
| `AccountType` | `varchar(16)` | `Member/Admin`；建立後不可切換 |
| `AccountStatus` | `varchar(24)` | `PendingEmailVerification/Active/Suspended/Anonymized/Disabled` |
| `PreferredLocale` | `varchar(10)` | `zh-TW/ja-JP/ko-KR`；第一版預設 `zh-TW` |
| `CreatedAtUtc/UpdatedAtUtc` | `datetime2(3)` | 必填 |
| `AnonymizedAtUtc` | `datetime2(3) NULL` | 匿名化後不可登入 |
| `RowVersion` | `rowversion` | 帳號狀態併發控制 |

- `NormalizedEmail` 採非 Null Filtered Unique Index；手機只作選填聯絡資料，不作登入名稱或簡訊驗證。
- 密碼、Security Stamp、Concurrency Stamp、AccessFailedCount、LockoutEnd、TOTP Authenticator Key 與 Recovery Code 使用 Identity Store，不另存明文。
- Email 驗證與密碼重設使用 Identity Token Provider；Token 不落地明文，也不寫入一般 Log。

### 2.2 帳號狀態不變條件

| 事件 | 同一 Use Case／交易內效果 |
|---|---|
| Email 驗證成功 | `EmailConfirmed=true`，且 `PendingEmailVerification → Active` |
| 管理員停用 | `AdminProfiles.IsActive=0`、`AccountStatus=Suspended`、更新 Security Stamp、寫 AuditLog |
| 管理員重新啟用 | `IsActive=1`、`AccountStatus=Active`、寫 AuditLog；舊 Cookie 不恢復 |
| 密碼／角色／2FA 變更或可疑登入 | 更新 Security Stamp，使既有 Cookie 失效 |

```text
允許登入 = AccountStatus == Active
          AND Identity Lockout 未鎖定
          AND（Admin 時 AdminProfiles.IsActive == true 且 TwoFactorEnabled == true）
```

- `PendingEmailVerification` 可重寄驗證信與申請密碼重設，但不可正常登入。
- `Suspended` 可由管理員復權；`Disabled` 不可登入；`Anonymized` 是不可逆終態。
- 所有 Endpoint／Policy 必須共用同一 Application 層帳號狀態判斷。

### 2.3 Cookie 與 Lockout

- 前台會員與後台管理員使用兩個 Cookie Scheme；Cookie 固定 HttpOnly，正式環境 Secure，狀態變更請求採 CSRF 防護。
- 會員 Session 閒置 8 小時滑動延長，絕對最長 7 天；管理員 Session 絕對期限 2 小時，不滑動。
- 所有登入入口呼叫同一登入 Application Service。第五次失敗時沿用 Identity `AccessFailedCount`／`LockoutEnd`，依 `AccountType` 原子設定：會員 `UtcNow+15m`、管理員 `UtcNow+30m`。
- 單一 `IdentityOptions.Lockout.DefaultLockoutTimeSpan` 不作為差異化期限的唯一來源；整合測試必須分別驗證兩種帳號。
- 管理員帳密成功後只核發 TOTP 驗證用臨時憑證；TOTP 成功後才核發完整後台 Cookie。

## 3. Profile、角色、地址與收藏

### 3.1 MemberProfiles

| 欄位 | 型別 | 規則 |
|---|---|---|
| `UserId` | `nvarchar(450)` | PK/FK → AspNetUsers.Id；Restrict |
| `PublicId` | `uniqueidentifier` | UX |
| `DisplayName` | `nvarchar(100)` | 必填 |
| `BirthDate` | `date NULL` | 選填 |
| `CreatedAtUtc/UpdatedAtUtc` | `datetime2(3)` | 必填 |
| `RowVersion` | `rowversion` | 併發控制 |

不重複保存 Email／電話。匿名化時清除選填個資與顯示名稱，但依法及政策保留的訂單、付款、物流、退貨與退款快照不刪除。

### 3.2 AdminProfiles

| 欄位 | 型別 | 規則 |
|---|---|---|
| `UserId` | `nvarchar(450)` | PK/FK → AspNetUsers.Id；Restrict |
| `PublicId` | `uniqueidentifier` | UX |
| `EmployeeCode` | `nvarchar(64)` | UX |
| `DisplayName` | `nvarchar(100)` | 必填 |
| `IsActive` | `bit` | default 1；INDEX |
| `CreatedAtUtc/UpdatedAtUtc` | `datetime2(3)` | 必填 |
| `RowVersion` | `rowversion` | 併發控制 |

`MemberProfiles` 與 `AdminProfiles` 依 AccountType 互斥；管理員若要購物需另建會員帳號。

### 3.3 正式角色

`SuperAdmin`、`CatalogManager`、`InventoryManager`、`OrderManager`、`FinanceManager`、`CustomerService`、`CustomerServiceSupervisor`、`MarketingAnalyst`、`PrivacyAdmin`、`SecurityAdmin`。

- 一位管理員可有多角色；精確操作使用 Authorization Policy。
- 完整個資只允許 `PrivacyAdmin`／`SuperAdmin`，每次查閱寫 AuditLog。
- 角色或高風險權限異動同交易更新 Security Stamp 與 AuditLog。

### 3.4 MemberAddresses（MutableEntity）

| 欄位 | 型別 | Null／規則 |
|---|---|---|
| `MemberUserId` | `nvarchar(450)` | NOT NULL；FK → AspNetUsers.Id |
| `Label` | `nvarchar(50)` | NOT NULL；僅供會員辨識，不進訂單快照 |
| `RecipientName` | `nvarchar(100)` | NOT NULL |
| `Phone` | `nvarchar(32)` | NOT NULL |
| `PostalCode` | `nvarchar(16)` | NOT NULL |
| `City` | `nvarchar(50)` | NOT NULL |
| `District` | `nvarchar(50)` | NOT NULL |
| `AddressLine1` | `nvarchar(300)` | NOT NULL；不重複包含 City／District |
| `AddressLine2` | `nvarchar(300)` | NULL |
| `IsDefault` | `bit` | default 0 |
| `DeletedAtUtc` | `datetime2(3)` | NULL；軟刪除 |

- `UX_MemberAddresses_MemberUserId_Default`：每會員最多一筆未刪除預設地址。
- API 必須驗證 `MemberUserId` 是目前登入會員；修改地址不得回寫歷史訂單。

### 3.5 Favorites

| 欄位 | 型別 | 規則 |
|---|---|---|
| `MemberUserId` | `nvarchar(450)` | PK/FK → AspNetUsers.Id |
| `ProductId` | `bigint` | PK/FK → Products.Id |
| `CreatedAtUtc` | `datetime2(3)` | 必填 |

複合 PK `(MemberUserId, ProductId)`；新增／移除冪等；只開放登入會員，訪客不可使用。

## 4. 訂單與交易快照

### 4.1 Orders（MutableEntity）

| 群組 | 欄位與型別 | 規則 |
|---|---|---|
| 識別 | `OrderNumber nvarchar(32)`、`MemberUserId nvarchar(450) NULL`、`GuestEmailNormalized nvarchar(320) NULL` | OrderNumber UX；會員／訪客識別至少一者存在 |
| 狀態 | `OrderStatus/PaymentStatus/FulfillmentStatus/AssemblyStatus/OrderRefundStatus varchar(32)` | 只接受正式狀態機值；不得混用維度 |
| 金額 | `MerchandiseSubtotal/ItemDiscountTotal/ShippingFee/AssemblyFee/GrandTotal/PaidAmount/RefundedAmount decimal(18,2)`、`Currency char(3)` | 非負；第一版 TWD；明細與分攤可兩位小數，GrandTotal 在付款前以 AwayFromZero 取整數且等於 PaymentAttempt.Amount／PaidAmount；尾差由總額減明細加總推導，不新增欄位 |
| 收件快照 | `RecipientName nvarchar(100)`、`RecipientPhone nvarchar(32)`、`RecipientEmail nvarchar(320)`、`PostalCode nvarchar(16) NULL`、`RecipientCity nvarchar(50) NULL`、`RecipientDistrict nvarchar(50) NULL`、`AddressLine1 nvarchar(300) NULL`、`AddressLine2 nvarchar(300) NULL` | 宅配時 PostalCode／City／District／AddressLine1 必填；不保存地址簿 Label |
| 物流快照 | `ShippingMethodCode nvarchar(64)`、`ShippingProviderProfileVersionId bigint`、`StoreCode/StoreName/StoreAddress nvarchar(...) NULL`、`ShippingConstraintPolicyVersion int` | 成立後不可被目前設定覆寫 |
| 政策／時間 | `ReturnPolicyVersion int`、`CouponPolicyVersion int NULL`、各狀態時間 `datetime2(3) NULL` | 依合法轉移填入 |
| 冪等 | `CheckoutIdempotencyKey nvarchar(128)`、`SourceCartPublicId uniqueidentifier NULL` | Idempotency UX |

索引：OrderNumber UX、CheckoutIdempotencyKey UX、`(MemberUserId,CreatedAtUtc)`、`(OrderStatus,PaymentDueAtUtc)`、CompletedAtUtc。

- 訂單建立、優惠券保留與庫存保留由 Checkout Application Use Case 在同一 SQL Transaction 完成，任一失敗整筆回滾。
- `OrderStatus` 由 Haru 模組維護；Payment／Fulfillment／OrderRefund 投影由各 Owner 經同交易或 Outbox 更新。
- `AssemblyStatus=NotRequired` 只存在於無組裝工作的訂單投影；有多台時取最不完成狀態，全部 ReadyToShip 才視為可出貨。

### 4.2 OrderItems（Entity）

`OrderId bigint`、`SkuId bigint NULL`、`SkuCodeSnapshot nvarchar(64)`、`ProductNameSnapshot/SkuNameSnapshot nvarchar(160)`、`Quantity int`、`ListUnitPrice/SaleUnitPrice/FinalUnitPrice/UnitCostSnapshot/LineSubtotal/DiscountAllocation/LineTotal decimal(18,2)`、`IsCouponEligible bit`、`AssemblyGroupKey uniqueidentifier NULL`、`ReturnableQuantity/ReturnedQuantity int`。

- Quantity > 0；`ReturnedQuantity <= ReturnableQuantity <= Quantity`。
- `IsCouponEligible` 為下單時不可變快照，第一版每張訂單最多一張優惠券；不得以 `DiscountAllocation > 0` 反推。欄位已由 `20260825171312_AddDes21RefundSnapshots` 納入現行 Migration 歷程，Checkout 必須保存可信快照，不回查目前商品分類。
- 索引：OrderId、SkuId、`(OrderId,AssemblyGroupKey)`。
- 商品改名／改價不得改變快照；退款依 OrderItem 與 Yinyin 的 RefundAllocation 計算。

### 4.3 OrderStatusHistories（Entity，append-only）

`OrderId bigint`、`StateDimension varchar(32)`、`FromStatus varchar(32) NULL`、`ToStatus varchar(32)`、`ReasonCode varchar(64) NULL`、`ActorUserId nvarchar(450) NULL`、`OccurredAtUtc datetime2(3)`、`TraceId nvarchar(64)`。

- StateDimension=`OrderStatus/PaymentStatus/FulfillmentStatus/AssemblyStatus/OrderRefundStatus`。
- `IX_OrderStatusHistories_OrderId_OccurredAtUtc`；所有訂單投影變更都 append 一筆。
- 每台組裝工作內部歷程不放在本表，使用獨立 `AssemblyJobStatusHistories`。

## 5. 訪客訂單兩階段存取

```text
訂單編號＋購買 Email
→ 一律 202
→ 有效時寄六位數驗證碼
→ requestPublicId＋code 驗證
→ 成功後核發限單、30 分鐘 HttpOnly Cookie
```

### 5.1 GuestOrderAccessRequests（MutableEntity）

| 欄位 | 型別 | 規則 |
|---|---|---|
| `OrderId` | `bigint NULL` | 有效訂單＋Email 才填；FK → Orders.Id |
| `CodeHash` | `binary(32) NULL` | HMAC-SHA-256；無效請求為 Null |
| `RequesterIpHash` | `binary(32)` | 伺服器 Secret HMAC；限流用途 |
| `EmailKeyHash` | `binary(32)` | 正規化輸入 Email HMAC；限流用途 |
| `OrderLookupKeyHash` | `binary(32)` | 正規化輸入訂單編號 HMAC；限流用途 |
| `ExpiresAtUtc` | `datetime2(3)` | 建立後 10 分鐘 |
| `AttemptCount` | `int` | default 0；最多 5 次，原子更新 |
| `SendCount` | `int` | default 0；初次寄送計入，最多 3 封 |
| `LastSentAtUtc` | `datetime2(3) NULL` | 兩封至少相隔 60 秒 |
| `LockedAtUtc` | `datetime2(3) NULL` | 達錯誤門檻時寫入 |
| `ConsumedAtUtc` | `datetime2(3) NULL` | 驗證成功後寫入，Challenge 單次使用 |
| `RevokedAtUtc` | `datetime2(3) NULL` | 撤銷標記 |

- PublicId 即 `requestPublicId`。有效條件：未過期、未消耗、未鎖定、未撤銷、AttemptCount < 5。
- Request 不論訂單是否存在都維持相同 202 與等效延遲；無效資料不寄信。
- 15 分鐘視窗：每 IP Hash 最多 10 次、每 Email HMAC 最多 5 次、每訂單 Lookup Hash 最多 5 次；三者同時通過才建立／寄送。
- 索引：PublicId UX、ExpiresAtUtc，以及三個 `(限流Hash,CreatedAtUtc)`。
- 有效 Challenge 建立／重寄時，Request（含限流事件）與中央 Outbox 必須在同一 SQL transaction commit；payload 只帶 Request PublicId 與 SendCount 版本，不保存 Email、驗證碼或 Token 明文。
- 六位碼以伺服器 Guest pepper 對 `RequestPublicId + SendCount` 做 HMAC 後決定性重建，Request 仍只保存 `CodeHash`；consumer 僅寄送仍有效且版本等於目前 SendCount 的事件，舊版本不得寄出已失效驗證碼。

### 5.2 GuestOrderAccessTokens（Entity）

| 欄位 | 型別 | 規則 |
|---|---|---|
| `OrderId` | `bigint` | FK → Orders.Id；限單 Scope |
| `RequestId` | `bigint` | FK → GuestOrderAccessRequests.Id；UX，一個 Challenge 最多核發一個 Token |
| `TokenHash` | `binary(32)` | UX；高熵 Token 的 SHA-256／HMAC-SHA-256，不存明文 |
| `ExpiresAtUtc` | `datetime2(3)` | 核發後 30 分鐘 |
| `RevokedAtUtc` | `datetime2(3) NULL` | 撤銷即失效 |
| `ScopeViolationCount` | `int` | default 0；異常跨訂單操作計數 |

- Access Cookie 在 30 分鐘內可多次使用，但只能查詢／取消綁定訂單、查看物流、申請單項退貨及查看退款進度。
- 不設 `ConsumedAtUtc`；失效由 ExpiresAtUtc、RevokedAtUtc 及安全 Policy 決定。
- 會員使用登入身分＋訂單所有權，不使用 Guest Token。
- Cookie／Token 明文不得進 Local Storage、Session Storage、一般 Log 或 AuditLog。

### 5.3 保存與清理

- Guest Request／Token 在 ExpiresAtUtc 後保存 30 天；每日背景工作依主鍵排序分批硬刪，每批最多 500 筆，具冪等與共用重試。
- 需要長期調查的異常事件寫入 AuditLogs；Request／Token 不因稽核而無限保存。

## 6. 組裝工作與歷程

### 6.1 AssemblyJobs（MutableEntity）

| 欄位 | 型別 | 規則 |
|---|---|---|
| `OrderId` | `bigint` | FK → Orders.Id |
| `AssemblyGroupKey` | `uniqueidentifier` | 對應一台組裝電腦的 OrderItems |
| `Status` | `varchar(24)` | `Pending/Started/Testing/ReadyToShip/Failed/Cancelled` |
| `StartedAtUtc/CompletedAtUtc` | `datetime2(3) NULL` | 狀態轉移填入 |
| `AssignedAdminUserId` | `nvarchar(450) NULL` | FK → AspNetUsers.Id |
| `Note` | `nvarchar(1000) NULL` | 內部備註 |

- `UX_AssemblyJobs_OrderId_AssemblyGroupKey`；只為有組裝服務且每台收 NT$300 的群組建立。
- 不需要組裝時不建立資料列，由 `Orders.AssemblyStatus=NotRequired` 表達。

### 6.2 AssemblyJobStatusHistories（Entity，append-only）

| 欄位 | 型別 | 規則 |
|---|---|---|
| `AssemblyJobId` | `bigint` | FK → AssemblyJobs.Id |
| `FromStatus` | `varchar(24) NULL` | 初次歷程可 Null |
| `ToStatus` | `varchar(24)` | 正式 AssemblyJob 狀態 |
| `ReasonCode` | `varchar(64) NULL` | 失敗、重工、取消等原因 |
| `ActorUserId` | `nvarchar(450) NULL` | 系統事件可 Null |
| `OccurredAtUtc` | `datetime2(3)` | 必填 |
| `TraceId` | `nvarchar(64)` | 請求／事件追蹤 |

- `IX_AssemblyJobStatusHistories_AssemblyJobId_OccurredAtUtc`。
- Job 狀態更新與 History 新增在同一交易；再重新計算 Orders.AssemblyStatus 投影及必要的 OrderStatusHistory。

## 7. 安全期限與通知

| 項目 | 規則 |
|---|---|
| 未驗證帳號 | 保存 7 天，期間可重寄，期滿依安全條件清理 |
| Email 驗證 Token | 24 小時、單用途 |
| 密碼重設 Token | 1 小時、單用途 |
| Guest 驗證碼 Challenge | 10 分鐘、單次使用 |
| Guest Access Cookie | 30 分鐘、限單且期限內可多次使用 |
| 會員登入失敗 | 5 次鎖定 15 分鐘 |
| 管理員登入失敗 | 5 次鎖定 30 分鐘 |

- 註冊驗證、忘記密碼、訪客驗證碼、訂單成立／付款／出貨／退款透過 Outbox 交給共用 Notifications／EmailDeliveries；交易不等待 SMTP。
- 異常登入、TOTP 重設、角色調整、完整個資查閱及驗證碼濫用寫共用 AuditLogs，不建立第二套 LoginLog。

## 8. 跨模組契約

| Consumer／Producer | 固定邊界 |
|---|---|
| Terry | 使用 Order／OrderItem／AssemblyGroupKey 公開 Query；物流與 BuildList 不直接寫 Orders |
| Yinyin | Payment／Refund 經 Application 契約更新 Orders 投影；退款使用 OrderRefundStatus |
| Kafen | 有效 GuestOrderAccessToken 可授權綁定訂單的單項退貨；不直接讀 Haru Repository |
| Alex／AI | 只取得去識別化且授權後的本人訂單 DTO；AI 不直接存取 Entity／DbContext |

跨模組不得共用 Repository／DbContext；同一同步交易由 Application Use Case 協調，非同步副作用使用版本化 Outbox Event。

## 9. 自建資料表總覽

| 群組 | 資料表 |
|---|---|
| Profile | MemberProfiles、AdminProfiles、MemberAddresses |
| 會員功能 | Favorites |
| 訂單 | Orders、OrderItems、OrderStatusHistories |
| 訪客存取 | GuestOrderAccessRequests、GuestOrderAccessTokens |
| 組裝 | AssemblyJobs、AssemblyJobStatusHistories |

共 11 張自建資料表，另擴充 Identity 的 ApplicationUsers；AuditLogs、Notifications、EmailDeliveries、OutboxMessages 為共用能力，不重複建立。

## 10. Review 與 Migration Gate

- [x] DEC-P263～DEC-P270 已由 alex 寫回本文件及正式契約。
- [x] 訪客兩階段驗證、限流、保存及 30 分鐘限單 Cookie 已定版。
- [x] 地址 City／District 與訂單快照已對齊，訂單不保存 Label。
- [x] AssemblyJob 使用獨立 append-only 歷程表。
- [x] 差異化 Lockout 有可實作的 Application Service 邊界。
- [x] yinyin 已完成欄位、索引、狀態、授權及交易交叉覆核。
- [ ] Haru 已依本文件建立 Entity／Fluent Configuration 與必要整合測試。
- [ ] Migration 已由獨立流程產生並完成 SQL／Snapshot 審查。

yinyin 覆核已完成，DES-20 可標示為 Schema 文件完成；Entity／測試／Migration 仍由各自工作項目及 Gate 管理。
