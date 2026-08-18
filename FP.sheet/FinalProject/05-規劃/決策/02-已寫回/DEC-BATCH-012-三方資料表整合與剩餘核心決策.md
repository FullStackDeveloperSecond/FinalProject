---
type: decision-record
batch_id: DEC-BATCH-012
title: 三方資料表整合與剩餘核心決策
status: applied
decision_range: DEC-P250～DEC-P262
submitted_at: 2026-08-17
consolidated_at: 2026-08-17
applied_at: 2026-08-17
source: Meta Bind 互動表單；13 項全部採建議方案
---

# DEC-BATCH-012｜三方資料表整合與剩餘核心決策

本快照收束 Kafen、Terry、Yinyin 最新資料表提案的剩餘核心問題。13 題答案完整，沒有自主輸入覆寫，也未發現答案間互斥；已於 2026-08-17 寫回正式文件、決策紀錄與未完成項目追蹤表。

> [!success]
> 狀態為 `applied`。本批定版規格已生效，但三位 Owner 的提案仍須完成 DES-17～DES-19 並通過 Migration Review Gate；本次沒有建立 EF Core Migration。

## 決策結果

| ID | 正式決策 |
|---|---|
| DEC-P250 | V1 移除 `InventoryReservations.CartId`。一般 Cart 不建立庫存保留；Checkout 在同一 SQL Transaction 先建立 Order，再以 `OrderId` 原子建立 Reservation。Cart 只保存購物意圖，價格、庫存與相容性在 Checkout 重驗。 |
| DEC-P251 | 商品評價採完整狀態機：`Draft → PendingReview → Approved／Rejected`、`Approved → Hidden`、`Rejected → PendingReview`。已核准評價修改後建立版本歷程並重新進入 `PendingReview`；只有 `Approved` 可公開。 |
| DEC-P252 | `ImportBatches` 採專用三來源檔案契約：`CreatedByAdminUserId`、`SourceFileHash1～3`、`SourceFileNameDisplay1～3`、`RowCount`、`ResultSummaryJson`、`NormalizedContentVersion`、`CorrelationId`。Product 匯入對應 Products／Skus／Specifications 三份來源，Inventory Adjustment 使用第一份來源，其餘為 Null。一般資料字典不得保留舊的單一 `ContentHash` 第二版 Schema。 |
| DEC-P253 | 現行 Owner／Assignee 使用正式 Identity FK；append-only History 的 `ActorUserId` 使用 Nullable Identity FK。Identity 採停權／軟刪除／匿名化，不因匿名化實體刪除交易依賴列；不可變 Actor PublicId、角色、動作、理由與結果快照統一由中央 `AuditLogs` 保存，不在每張 History 複製完整角色或個資快照。 |
| DEC-P254 | COD 限制品來源設於 SKU：新增 `RequiresPrepayment bit NOT NULL default 0`。訂單任一 SKU 為 true，或含組裝電腦時，不提供 COD；Terry 透過受控 COD Eligibility Query 提供 Checkout／Yinyin 使用，不允許 Yinyin 直接讀 Terry Repository。 |
| DEC-P255 | 建立獨立 `ReturnShipments`／`ReturnShipmentEvents` Aggregate，由 Kafen 擁有；不重用 Terry 的出貨 `Shipments`。V1 每個 ReturnRequest 同時只允許一個有效寄回批次，事件以 `Source + ExternalEventId` 唯一並採 append-only；Terry 只提供物流商／方式 Lookup。 |
| DEC-P256 | 退貨取件地址直接保存於 `ReturnShipment` 的不可變快照，不建立共用 `AddressSnapshots` Entity，也不強制重用原訂單地址。宅配取件保存收件人、電話、郵遞區號與地址；超商／自行寄送保存相應門市或寄件資訊，並依交易個資政策授權。 |
| DEC-P257 | V1 不建立 `ReturnRequests.SupportTicketId`，也不讓 SupportTicket 成為退貨狀態必要條件。補件要求、ReturnAttachment、審核備註、Email／站內通知留在 Return 流程；會員另開客服案件只作一般參考，訪客仍以 GuestOrderAccessToken 操作退貨。 |
| DEC-P258 | V1 統一案件工作台只保留正式 12 欄，不加入 `CustomerReplyState`、工作台 `RowVersion` 或額外 Assignment State。開啟來源案件後再取得 RowVersion、權限與可執行動作；工作台仍是唯讀投影。 |
| DEC-P259 | 檢舉狀態維持 `Open → Assigned → InReview → Actioned／Rejected → Closed`。補件以 `RequestMoreInfo` Action、StatusHistory 與通知表達，案件維持 `InReview`；成立後以 `Actioned + ResolutionCode` 表達實際處置，不新增 `Submitted/NeedMoreInfo/Founded/Unfounded` 狀態。 |
| DEC-P260 | 一般檢舉可由 `CustomerService` 在指定檢舉佇列承接；涉及個資、資安、詐騙、法律或高風險處置者，必須由 `CustomerServiceSupervisor` 指派或覆核。沿用既有角色與後端 Policy，不新增 `ReportReviewer`。 |
| DEC-P261 | `CouponRedemption` 只在 Checkout 建單交易中建立，`OrderId` 必填。Cart 預覽不占優惠名額；交易內以 Serializable／等價原子條件取得最後名額，建單失敗整筆回滾；付款逾時或合法取消再依正式規則 Release／Expire。 |
| DEC-P262 | 訪客優惠券每人次數以伺服器 Secret 對正規化訂購 Email 計算 HMAC，保存為 `GuestUsageKeyHash binary(32)`，不保存第二份 Email。`MemberUserId` 與 Guest Hash 恰一存在；Hash 不可輸出或還原。V1 使用固定 Secret 版本 1，Secret 不進版控；未來輪替需引入版本與相容讀取規則。 |

## 與既有決策的關係

| 既有決策／規格 | 檢查結果 |
|---|---|
| DEC-P243 分享期限 | 無關聯衝突 |
| DEC-P244 COD 能力與資格分工 | DEC-P254 補齊「限制品」資料來源；仍維持 Shipping Method 只表達基礎能力 |
| DEC-P245 SalePrices 唯一來源 | 無衝突；Yinyin 仍透過 Effective Price Query 讀取 |
| DEC-P246 ImportRows Schema | 無衝突；DEC-P252 補齊同 Aggregate 的 ImportBatch Schema，`Dataset` 仍為 `varchar(32)` |
| DEC-P247～P248 優惠券範圍與 Promotions | 無衝突；DEC-P261～P262只補名額保留與訪客識別 |
| DEC-P249 Shipment 狀態 | 無衝突；ReturnShipment 是不同 Aggregate，不重用出貨狀態機 |
| 客服只開放會員 | DEC-P257 避免讓訪客退貨依賴 SupportTicket，因此維持既有規則 |
| 退貨核准／退款執行分離 | DEC-P255～P257 不改變責任；Kafen 不寫 Refund，Yinyin 不寫 Return |

## 已有正式答案、組員必須直接修正

以下不是待決策，寫回時應同步列入組員修正與 Migration Review Gate：

### Kafen

- 移除 ReturnStatus 的 `Refunding`。
- 所有 Entity／MutableEntity 補齊固定 Profile。
- 私有附件使用 `binary(32)` SHA-256、Nullable DeletedAtUtc、UpdatedAtUtc 與 RowVersion。
- 檢舉防重包含 Reporter、TargetType、TargetPublicId、ReasonCode 與未結案範圍。
- 完成工作台 12 欄的三來源映射。
- SupportMessage Reply／Attachment Message 關聯不得跨 Ticket。
- 403 與 409 使用情境固定化。

### Terry

- 移除不存在的 `ReportViewer`／`OperationsManager` 角色。
- 報表使用正式 Route、ReportQuery、ReportResultDto 與錯誤碼。
- 庫存周轉、關聯分析及預測／異常公式對齊正式報表規格。
- `ImportRows.Dataset` 固定 `varchar(32)`。
- 跨模組報表只能使用 Owner 提供的 Application Query／DTO。

### Yinyin

- MutableEntity 的 `UpdatedAtUtc` 改為 NOT NULL，Coupon Junction 補 CreatedAtUtc。
- Coupon、OrderCoupon 欄位名稱、長度、百分比與 Unique 約束對齊正式資料字典。
- PaymentStatus 補 `Processing`；PaymentEvent 的時間、Hash、摘要長度與 Unique 對齊正式契約。
- Refund 起始狀態改 `PendingReview`，欄位名稱與長度對齊正式契約。
- 模擬發票補 `Pending`、固定 DemoMarker、OrderId 唯一；折讓 RefundId 唯一。

## 寫回影響文件

- [[00-專案概述/DoSelect完整系統規格書-v1.0]]
- [[01-需求/角色與權限]]
- [[02-領域需求/優惠券規則]]
- [[02-領域需求/評價收藏檢舉與模擬發票規格]]
- [[02-領域需求/購物車、訂單、付款與物流]]
- [[02-領域需求/退貨與退款政策]]
- [[02-領域需求/客服與AI功能]]
- [[03-架構/資料模型與ERD]]
- [[03-架構/資料字典索引]]
- [[03-架構/資料字典-商品庫存與組裝]]
- [[03-架構/資料字典-購物交易與售後]]
- [[03-架構/資料字典-會員客服AI與治理]]
- [[03-架構/狀態機設計]]
- [[03-架構/匯入暫存與庫存調整設計]]
- [[03-架構/統一案件工作台設計]]
- [[03-架構/API Endpoint目錄]]
- [[03-架構/API DTO與Schema契約]]
- [[03-架構/API錯誤碼目錄]]
- [[03-架構/設定與Secrets管理規範]]
- [[05-規劃/開發分工與交接]]
- [[05-規劃/未完成項目追蹤表]]
- [[05-規劃/決策紀錄]]

## 寫回後的組員交付

| Owner | 必須補交 |
|---|---|
| Kafen | ReturnShipment／Event、地址快照、正式 ReportStatus、工作台映射、附件與 Entity Profile 修正版 |
| Terry | Order-only Reservation、完整 ReviewStatus、ImportBatch 正式 Schema、SKU RequiresPrepayment、正式報表契約與公式修正版 |
| Yinyin | Order-bound CouponRedemption、Guest HMAC Key、正式 Payment／Refund／Invoice Schema 修正版 |
| Alex | 更新共用資料字典、Policy、Audit Actor 規則、HMAC Secret 設定契約及跨模組 DTO Review |

寫回後新增追蹤項目：Kafen `DES-17`、Terry `DES-18`、Yinyin `DES-19`。決策完成不代表三份組員 Schema 已驗收；三項都完成並通過下列 Gate 後才可建立正式 Migration。

## Migration Review Gate

1. 三位 Owner 的 Entity／Configuration／ERD 已使用同一正式欄位名稱與狀態值。
2. Cross-module FK 目標及 Delete Behavior 已由雙方確認。
3. Return、Refund、Inventory、Coupon、Payment 的交易與冪等測試案例已具體列出。
4. API DTO、Endpoint、錯誤碼與資料表欄位沒有第二套名稱。
5. EF Core Migration 只在上述條件完成後建立；Migration 仍需獨立 Review，不因本決策快照自動核准。

## 2026-08-18 後續落實紀錄

三位 Owner 的原始上繳稿已依本批決策與正式資料字典收束為欄位級最終實作交付：

- Kafen：[[03-架構/資料表實作交付/Kafen-客服售後與檢舉最終Schema]]。
- Terry：[[03-架構/資料表實作交付/Terry-商品庫存物流組裝與報表最終Schema]]。
- Yinyin：[[03-架構/資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]。
- 共用入口與權威順序：[[03-架構/資料表實作交付/README]]。

因此 DES-17～DES-19 已關閉。此狀態只表示 Schema 文件修正與跨文件對齊完成；Entity、Fluent Configuration、整合測試與 Migration 仍須依上方 Review Gate 個別完成，本輪沒有建立或套用 Migration。
