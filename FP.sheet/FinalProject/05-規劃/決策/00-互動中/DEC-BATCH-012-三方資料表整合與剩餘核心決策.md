---
type: decision-interaction
batch_id: DEC-BATCH-012
title: 三方資料表整合與剩餘核心決策
status: applied
submission_feedback: ✅ 本批 13 項決策已於 2026-08-17 寫回正式文件、決策紀錄與未完成項目追蹤表；三位 Owner 的 Schema 補交由 DES-17～DES-19 追蹤。
created_at: 2026-08-17
submitted_at: 2026-08-17
applied_at: 2026-08-17
decision_count: 13
decision_range: DEC-P250～DEC-P262
q01_choice: order_only_remove_cart_id
q01_custom: ""
q02_choice: full_review_lifecycle
q02_custom: ""
q03_choice: dedicated_three_source_contract
q03_custom: ""
q04_choice: nullable_fk_plus_central_audit_snapshot
q04_custom: ""
q05_choice: sku_requires_prepayment_flag
q05_custom: ""
q06_choice: separate_return_shipment_single_active
q06_custom: ""
q07_choice: return_shipment_embedded_snapshot
q07_custom: ""
q08_choice: no_return_support_fk_v1
q08_custom: ""
q09_choice: official_twelve_only
q09_custom: ""
q10_choice: keep_confirmed_report_states
q10_custom: ""
q11_choice: risk_tier_supervisor_assignment
q11_custom: ""
q12_choice: order_bound_in_checkout_transaction
q12_custom: ""
q13_choice: hmac_normalized_email_hash
q13_custom: ""
---

# DEC-BATCH-012｜三方資料表整合與剩餘核心決策

目前狀態：`VIEW[{status}]`

> [!important]
> 本批比對 Kafen、Terry、Yinyin 最新資料表提案與 DoSelect 正式需求、資料字典、狀態機、API 契約及角色權限。未送出前，本頁只是一份互動表單，不會改變正式規格，也不得作為 Migration 依據。

## 審查來源

- Kafen：`C:/Users/alexy/Downloads/客服售後與檢舉資料表修正版_Table_Schema_SQLServer.md`
- Terry：`C:/Users/alexy/Downloads/商品庫存物流組裝資料表修正版.md`
- Yinyin：`C:/Users/alexy/.codex/attachments/18374590-3b90-48af-bdb5-4f8896a5fb02/DoSelect_yinyin_資料表設計_V2_最終修正版.md`
- 正式基準：需求、資料字典、狀態機、API Endpoint／DTO／錯誤碼、角色與權限，以及 DEC-P243～DEC-P249。

## 整合結論

三份提案的 Aggregate 責任已大致正確：Terry 不寫入付款／退款，Yinyin 不建立 Cart／Shipment／SalePrice 第二來源，Kafen 不執行退款，M-15 由 Terry 主責。現在仍有兩種缺口：

1. 已有正式答案的契約偏差：不需要再次決策，組員必須直接修正。
2. 正式文件仍存在歧義，或會改變核心流程／資料責任：列為本批 13 項決策。

## 不需決策，直接依正式規格修正

### Kafen

| 缺失 | 建議修正 | Migration 影響 |
|---|---|---|
| `ReturnStatus` 多出 `Refunding` | 移除；退貨維持 `AwaitingRefund → Completed`，退款進度由 Yinyin 的 `RefundTransactionStatus` 表達 | 阻塞 |
| Entity Profile 不完整 | `SupportMessages`、Assignment／Status History、SLA Event、ReturnItem、ReturnInspection 等依 `Entity` 補齊 `PublicId`、`CreatedAtUtc`；可修改表依 `MutableEntity` 補 `UpdatedAtUtc NOT NULL`、`RowVersion` | 阻塞 |
| 私有附件 Profile 不完整 | 三張附件表補 `UpdatedAtUtc NOT NULL`、`RowVersion`、`DeletedAtUtc NULL`；SHA-256 統一 `binary(32)` | 阻塞 |
| 檢舉防重漏掉理由 | 唯一性範圍固定為 Reporter＋TargetType＋TargetPublicId＋ReasonCode＋未結案 | 阻塞 |
| 工作台來源映射未定義 | 明列 Support／Return／Report 的 12 欄來源；Return 的 Title、Priority、LastActivity 與 Report 的 Title 不得留給實作者猜測 | 阻塞 |
| 訊息／附件跨案件引用 | `ReplyToMessageId`、`SupportMessageId` 必須屬於相同 SupportTicket；Application 驗證並加入負面測試 | 阻塞 |
| 403／409 寫成二選一 | 無 Policy／資源權限回 403；具權限但承接或 RowVersion 衝突回 409 | 不改 Schema |
| ReturnInspection／ReturnItem current state 重複 | 若 `InspectionStatus`、`RestockDisposition` 保留在 ReturnItem，必須定義與最新 Inspection 同交易更新及核對方式 | 阻塞 |

### Terry

| 缺失 | 建議修正 | Migration 影響 |
|---|---|---|
| 報表使用不存在角色 | 移除 `ReportViewer`、`OperationsManager`；使用既有 MarketingAnalyst、FinanceManager、CustomerServiceSupervisor、SuperAdmin 與正式 Policy | 不改 Schema |
| 報表 Route／DTO 漂移 | 使用 `GET /api/v1/admin/reports/{reportKey}`、`/export`、`ReportQuery`、`ReportResultDto`；不得另拆 summary／series／rows 正式 Route | 不改 Schema |
| 報表錯誤碼漂移 | 使用 `report_key_invalid`、`report_range_invalid`、`authorization_forbidden`；不得新增未登錄別名 | 不改 Schema |
| 庫存周轉公式錯誤 | 改為期間銷貨成本 ÷ 平均庫存成本，不以出貨數量 ÷ 平均 OnHand 取代 | 不改 Schema |
| 關聯分析門檻不完整 | 固定共同訂單 ≥5、Support ≥1%、Confidence ≥20%、Lift >1 | 不改 Schema |
| 預測方法錯誤 | 最近 30 天簡單線性迴歸預測 7 天；少於 14 天不預測；異常使用 `|z|>2` | 不改 Schema |
| `ImportRows.Dataset` 使用 `varchar(24)` | 依正式資料字典與 DEC-P246 改為 `varchar(32)` | 阻塞 |
| 報表跨模組直接查詢風險 | Orders／Payments／Refunds／Cases 只能經各 Owner 的 Application Query／DTO，不使用他組 Repository／DbContext | 不改 Schema |

### Yinyin

| 缺失 | 建議修正 | Migration 影響 |
|---|---|---|
| `MutableEntity.UpdatedAtUtc` 設為 Nullable | 統一 `UpdatedAtUtc datetime2(3) NOT NULL` | 阻塞 |
| Coupon 欄位型別與名稱漂移 | 對齊 `Code nvarchar(64)`、`NameZhTw nvarchar(160)`、比例 0～1、正式快照欄位名稱 | 阻塞 |
| Coupon Junction 缺建立時間 | `CouponCategories`、`CouponProducts`、`CouponExcludedProducts` 補 `CreatedAtUtc` | 阻塞 |
| OrderCoupon 缺每訂單唯一 | 建立 `UX_OrderCoupons_OrderId`，並驗證 Redemption 與 Order 屬於同一筆交易 | 阻塞 |
| Payment 狀態漏 `Processing` | 使用完整 `Pending/AwaitingPayment/Processing/Paid/Failed/Cancelled/Expired` | 阻塞 |
| PaymentEvent 型別漂移 | `OccurredAt datetimeoffset(3)`、`PayloadHash binary(32)`、摘要 `nvarchar(4000)`、正式長度與 Unique Index | 阻塞 |
| Refund 起始狀態錯誤 | `Requested` 改 `PendingReview`，完整採正式 RefundTransactionStatus | 阻塞 |
| Refund 欄位長度／名稱漂移 | 對齊 RefundNumber、ReasonCode、IdempotencyKey 與操作者欄位正式契約 | 阻塞 |
| 模擬發票缺 Pending 與固定聲明 | 增加 `Pending` 生命週期與 `DemoMarker='DEMO-NOT-A-TAX-INVOICE'`；OrderId 唯一 | 阻塞 |
| 折讓缺每 Refund 唯一 | `SimulatedInvoiceAllowances.RefundId` 建立 Unique，避免重試重複折讓 | 阻塞 |

## 跨模組固定交接

| Producer | Consumer | 固定契約 |
|---|---|---|
| Yinyin | Terry | 已認列 Payment／Refund 去識別化 Query，供 M-15 報表使用 |
| Kafen | Terry | 通過驗收且 `RestockDisposition=Resellable` 的 Return-to-stock Command/Event；需 Idempotency Key |
| Terry | Yinyin | Effective Sale Price、Coupon-eligible subtotal、COD eligibility Query，不提供資料表直接寫入 |
| Kafen | Yinyin | 已核准退貨、ReturnItem、可退款數量與原因 DTO；Yinyin 回傳 Refund 狀態投影 |
| Terry／Yinyin／Kafen | Alex | 只提供去識別化 Application Query／DTO；AI 不直接存取 Repository、DbContext 或 Entity |

## 待你決策的 13 項摘要

| ID | 主題 | 建議方案 |
|---|---|---|
| DEC-P250 | Reservation 是否保留 CartId | 移除 CartId，只綁 OrderId |
| DEC-P251 | 商品評價狀態機 | 採完整 Draft／PendingReview／Approved／Rejected／Hidden |
| DEC-P252 | ImportBatch 唯一 Schema | 採專用匯入設計的三來源檔案模型 |
| DEC-P253 | 歷程 Actor 保存 | Nullable Identity FK＋中央 Audit 快照 |
| DEC-P254 | COD 限制品來源 | SKU `RequiresPrepayment` 明確旗標 |
| DEC-P255 | 退貨物流 Aggregate | 獨立 ReturnShipment，由 Kafen 擁有，V1 單一有效批次 |
| DEC-P256 | 退貨取件地址 | ReturnShipment 直接保存不可變快照 |
| DEC-P257 | ReturnRequest 與 SupportTicket | V1 不建立 FK，補件留在 Return 流程 |
| DEC-P258 | 工作台額外欄位 | V1 只保留正式 12 欄 |
| DEC-P259 | 檢舉狀態機 | 沿用已確認 Open／Assigned／InReview／Actioned／Rejected／Closed |
| DEC-P260 | 高風險檢舉分流 | 一般 CS 處理；高風險由 CSS 指派與覆核 |
| DEC-P261 | CouponRedemption 建立時機 | Checkout 建單交易內建立，OrderId 必填 |
| DEC-P262 | 訪客優惠券次數識別 | 保存正規化 Email 的 HMAC Hash，不保存 Email 複本 |

---

### 1. DEC-P250｜InventoryReservation 是否保留 `CartId`

一般購物車不保留庫存；若保留 `CartId`，容易讓實作者誤在加車時建立 Reservation，也會讓 Cart／Order 雙來源增加不變量。

> [!tip] 建議
> V1 移除 `InventoryReservations.CartId`，Checkout 在同一交易先建立 Order，再以 `OrderId` 原子建立 Reservation。Cart 只做價格、庫存與相容性預覽。

```meta-bind
INPUT[select(option(order_only_remove_cart_id, '只綁 OrderId，移除 CartId（建議）'), option(checkout_transient_cart_id, '保留 CartId，但只允許 Checkout 短期過渡'), option(cart_can_reserve, '允許 Cart 建立庫存保留'), option(custom_only, '完全採自主方案')):q01_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：Reservation 建立時機、Cart／Order 關聯與交易順序')):q01_custom]
```

### 2. DEC-P251｜商品評價正式狀態機

領域需求與資料字典目前使用兩套名稱。這會影響 S-02 的 Entity Enum、審核 Endpoint、公開條件及版本歷程。

> [!tip] 建議
> 採 `Draft → PendingReview → Approved | Rejected`、`Approved → Hidden`、`Rejected → PendingReview`；完整表達會員暫存、送審、核准、拒絕、隱藏與重新送審。

```meta-bind
INPUT[select(option(full_review_lifecycle, 'Draft／PendingReview／Approved／Rejected／Hidden（建議）'), option(compact_persistence_states, 'Pending／Published／Rejected／Hidden'), option(no_server_draft, '無 Draft，送出即 PendingReview'), option(custom_only, '完全採自主方案')):q02_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：狀態名稱、合法轉移、公開條件與修改後處理')):q02_custom]
```

### 3. DEC-P252｜`ImportBatches` 唯一 Schema

一般資料字典仍是單一 ContentHash 與彙總欄位；專用匯入設計需要 Product／SKU／Specification 三份來源檔案各自 Hash、顯示檔名與合計列數。

> [!tip] 建議
> 採專用匯入模型：`CreatedByAdminUserId`、`SourceFileHash1~3`、`SourceFileNameDisplay1~3`、`RowCount`、`ResultSummaryJson`、`NormalizedContentVersion`、`CorrelationId`，並回寫一般資料字典。

```meta-bind
INPUT[select(option(dedicated_three_source_contract, '採專用三來源檔案契約（建議）'), option(generic_single_content_hash, '維持一般單一 ContentHash 契約'), option(split_product_and_inventory_batches, '商品與庫存使用不同 Batch Entity'), option(custom_only, '完全採自主方案')):q03_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：欄位名稱、來源檔數、Hash、統計與保存期限')):q03_custom]
```

### 4. DEC-P253｜Append-only 歷程的 Actor 保存方式

歷程需保留操作者，但會員／管理員可能停權或匿名化；同時不應在每張 History 重複保存大量角色與個資快照。

> [!tip] 建議
> 現行主表 Owner／Assignee 使用正常 Identity FK；append-only History 的 `ActorUserId` 使用 Nullable FK。Identity 採軟刪除／匿名化不實體刪除；不可變 PublicId、角色、動作與理由快照統一由 AuditLog 保存。

```meta-bind
INPUT[select(option(nullable_fk_plus_central_audit_snapshot, 'Nullable FK＋中央 Audit 快照（建議）'), option(history_fk_only, 'History 只存 Identity FK'), option(snapshot_only_no_fk, 'History 只存 PublicId／角色快照，不建 FK'), option(custom_only, '完全採自主方案')):q04_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：哪些欄位建 FK、匿名化後保留內容、Audit 快照範圍')):q04_custom]
```

### 5. DEC-P254｜COD 限制品的正式資料來源

目前只有「限制品不得 COD」的規則，沒有可供 Checkout 與 Yinyin 付款模組共同查詢的持久化來源。

> [!tip] 建議
> 在 SKU 增加 `RequiresPrepayment bit NOT NULL default 0`。任一購買 SKU 為 true，或訂單含組裝電腦時，不提供 COD；Terry 透過受控 Eligibility Query 提供 Yinyin／Checkout 使用。

```meta-bind
INPUT[select(option(sku_requires_prepayment_flag, 'SKU RequiresPrepayment 旗標（建議）'), option(product_level_flag, 'Product 層級限制旗標'), option(versioned_restriction_policy_table, '獨立版本化限制政策表'), option(custom_only, '完全採自主方案')):q05_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：限制層級、欄位／表名、組裝例外與 Query 契約')):q05_custom]
```

### 6. DEC-P255｜是否建立獨立退貨物流 Aggregate

退貨物流是入庫方向，生命週期與 Terry 的訂單出貨 Shipment 不同；但完全不建物流資料會使 M-12 無法表達交寄與收貨事件。

> [!tip] 建議
> 建立獨立 `ReturnShipments`／`ReturnShipmentEvents`，由 Kafen 擁有；V1 每筆 ReturnRequest 只允許一個有效寄回批次，模擬物流事件且以 Source＋ExternalEventId 唯一。Terry 只提供物流商／方式 Lookup。

```meta-bind
INPUT[select(option(separate_return_shipment_single_active, '獨立 ReturnShipment，V1 單一有效批次（建議）'), option(return_status_fields_only, '不建 Aggregate，只在 ReturnRequest 保存物流欄位'), option(reuse_outbound_shipments, '擴充並重用 Terry 的 Shipments'), option(custom_only, '完全採自主方案')):q06_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：資料表、Owner、多次寄回、物流事件與出貨 Shipment 關係')):q06_custom]
```

### 7. DEC-P256｜退貨取件地址快照

退貨取件地址可能與原訂單地址不同；目前也沒有正式共用 `AddressSnapshots` Entity。

> [!tip] 建議
> 直接在 ReturnShipment 保存本次取件所需的不可變地址快照欄位。只有宅配取件需要地址；超商／自行寄送保存相對應門市或寄件資訊。不得回指會員目前地址作歷史顯示。

```meta-bind
INPUT[select(option(return_shipment_embedded_snapshot, 'ReturnShipment 直接保存地址快照（建議）'), option(shared_address_snapshot_entity, '建立共用 AddressSnapshot Entity'), option(reuse_original_order_snapshot, '固定重用原訂單地址快照'), option(custom_only, '完全採自主方案')):q07_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：地址可否不同、保存欄位、超商／自行寄送與個資清理')):q07_custom]
```

### 8. DEC-P257｜`ReturnRequest` 是否連結 `SupportTicket`

客服只開放會員，但訪客也能退貨。若把退貨補件綁到 SupportTicket，會產生訪客無法使用及兩個 Aggregate 狀態互相依賴的問題。

> [!tip] 建議
> V1 不建立 `ReturnRequests.SupportTicketId`。補件要求、附件、審核備註、Email／站內通知留在 Return 流程；會員若另開客服案件，只視為一般關聯，不作退貨狀態必要條件。

```meta-bind
INPUT[select(option(no_return_support_fk_v1, 'V1 不建立 FK，補件留在 Return 流程（建議）'), option(optional_one_support_ticket, '需要時建立一張 SupportTicket 並回填 FK'), option(return_messages_entity, '建立 ReturnMessages 專用對話'), option(custom_only, '完全採自主方案')):q08_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：會員／訪客補件、對話、附件、通知與結案關係')):q08_custom]
```

### 9. DEC-P258｜統一案件工作台額外欄位

正式工作台已有 12 欄。`RowVersion`、`CustomerReplyState` 與 Assignment State 會擴張 View、DTO、API 與前端契約。

> [!tip] 建議
> V1 工作台只保留正式 12 欄；開啟來源案件後再取得 RowVersion 與可執行動作。需要「待顧客／待客服」時先由既有 Status 篩選，不新增第二狀態來源。

```meta-bind
INPUT[select(option(official_twelve_only, 'V1 只保留正式 12 欄（建議）'), option(add_customer_reply_state, '增加 CustomerReplyState'), option(add_reply_and_rowversion, '增加 CustomerReplyState 與 RowVersion'), option(custom_only, '完全採自主方案')):q09_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：額外欄位、來源、篩選與是否持久化')):q09_custom]
```

### 10. DEC-P259｜檢舉案件正式狀態機

已確認需求使用 `Open/Assigned/InReview/Actioned/Rejected/Closed`；Kafen 提出另一套 Submitted／NeedMoreInfo／Founded／Unfounded。

> [!tip] 建議
> 保留既有正式狀態。補件使用 `RequestMoreInfo` Action＋StatusHistory，但案件仍維持 `InReview`；成立使用 `Actioned` 並以 ResolutionCode 表達實際處置，不再建立 `Founded` 狀態。

```meta-bind
INPUT[select(option(keep_confirmed_report_states, '保留 Open／Assigned／InReview／Actioned／Rejected／Closed（建議）'), option(kafen_proposed_states, '改採 Submitted／UnderReview／NeedMoreInfo／Founded／Unfounded／Closed'), option(hybrid_add_need_more_info, '保留正式狀態但新增 NeedMoreInfo'), option(custom_only, '完全採自主方案')):q10_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：狀態、補件、成立／不成立、重開、結案與會員顯示')):q10_custom]
```

### 11. DEC-P260｜高風險檢舉案件的承接方式

角色清單已有 CustomerService 與 CustomerServiceSupervisor，但尚未定義個資外洩、詐騙、法律或資安案件是否能由一般客服自由自領。

> [!tip] 建議
> 一般檢舉可由 CustomerService 在指定檢舉佇列承接；個資、資安、詐騙、法律與高風險處置必須由 CustomerServiceSupervisor 指派或覆核。不得新增 ReportReviewer 角色。

```meta-bind
INPUT[select(option(risk_tier_supervisor_assignment, '一般 CS、高風險 CSS 指派／覆核（建議）'), option(all_reports_supervisor_assigns, '所有檢舉都由 CSS 指派'), option(all_cs_self_claim, '所有檢舉都允許 CS 自領'), option(custom_only, '完全採自主方案')):q11_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：高風險分類、自領、指派、覆核與升級規則')):q11_custom]
```

### 12. DEC-P261｜`CouponRedemption` 建立時機與 `OrderId`

正式流程要求建立訂單、優惠使用與庫存保留同交易；Yinyin 提案允許 `OrderId=NULL`，會形成脫離訂單的優惠名額保留。

> [!tip] 建議
> Cart 預覽不保留優惠名額。Checkout 交易先配置 Order，再建立 `CouponRedemption(OrderId NOT NULL, Status=Reserved)`；建單失敗整筆回滾，付款逾時再依規則 Release／Expire。

```meta-bind
INPUT[select(option(order_bound_in_checkout_transaction, 'Checkout 交易內建立且 OrderId 必填（建議）'), option(preorder_coupon_reservation, '允許 OrderId 為 Null 的預先保留'), option(no_reserved_state, '不保留名額，建單時直接 Consumed'), option(custom_only, '完全採自主方案')):q12_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：建立時機、OrderId、Reserved 期限、付款逾時與返券')):q12_custom]
```

### 13. DEC-P262｜訪客優惠券每人次數識別

訪客沒有 MemberUserId，但公開優惠碼仍可能限制每人次數；直接在 CouponRedemption 複製 Email 會增加個資副本。

> [!tip] 建議
> 保存以伺服器 Secret 對正規化訂購 Email 計算的 `GuestUsageKeyHash binary(32)`；MemberUserId 與 GuestUsageKeyHash 恰一存在。它只用於次數限制，不可還原或輸出，Key 輪替需保留版本或在專案期間固定。

```meta-bind
INPUT[select(option(hmac_normalized_email_hash, 'HMAC 正規化 Email Hash（建議）'), option(query_order_email_only, '不另存 Hash，每次透過 Order Email 查詢'), option(no_guest_per_person_limit, '訪客只限制總量，不限制每人次數'), option(custom_only, '完全採自主方案')):q13_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：訪客識別來源、Hash／Key 規則、限制範圍與保存期限')):q13_custom]
```

## 批次操作

`BUTTON[submit-decision-batch-012,restore-draft-012]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 13 項決策
style: primary
id: submit-decision-batch-012
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
  - type: updateMetadata
    bindTarget: submitted_at
    evaluate: false
    value: "2026-08-17"
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "✅ 已送出本批 13 項決策；答案已保存，可交由 Codex 收束。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-012
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: drafting
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "📝 已退回草稿；可繼續修改答案。"
```

## 寫回門檻

- 13 題皆有選項，或自主輸入提供完整可實作方案。
- Codex 完成三份提案間的衝突檢查後，才轉為 `ready-to-apply`。
- 只有收到「寫回正式文件」授權後，才更新資料字典、狀態機、API 契約、角色權限、未完成項目追蹤表與三位組員的修正紀錄。
- 在 `applied` 前，Kafen／Terry／Yinyin 不得依本批未定答案建立正式 Migration。
