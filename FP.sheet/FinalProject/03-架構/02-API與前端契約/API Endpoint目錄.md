---
文件狀態: 已確認
最後更新: 2026-08-26
追蹤項目:
  - DES-10
  - DES-16
  - DES-20
  - DES-22
  - DES-23
  - REQ-02
  - REQ-03
---

# API Endpoint 目錄

本目錄把 37 個 M 使用案例及完成 M 桌面頁面所需的支撐操作，統一回連至第一版 API Route。Method、HTTP Status、分頁、Problem Details、冪等與 RowVersion 遵守 [[03-架構/02-API與前端契約/API共通規範]]；錯誤碼遵守 [[03-架構/02-API與前端契約/API錯誤碼目錄]]；DTO 欄位與上限見 [[03-架構/02-API與前端契約/API DTO與Schema契約]]。

所有 Route 參數 `{id}`、`{orderId}`、`{productId}`、`{attemptId}` 等皆代表資源 `PublicId`，不得接受資料庫 `bigint Id`。產生、索引、授權與錯誤隱匿規則見 [[03-架構/03-資料與一致性/PublicId與資料完整性設計]]。

## Route 命名原則

- 一般資源使用 `GET`、`POST`、`PUT／PATCH`、`DELETE` 表達查詢、建立、修改及刪除。
- 領域狀態轉移或高風險命令固定使用 `POST /.../actions/{action}`；`action` 必須是 Endpoint 白名單，不接受任意狀態名稱。
- Action Token 不在該 Endpoint 白名單時固定回 `400 validation_failed`；Token 合法但目前狀態不允許時回對應領域的 `409 *_state_conflict`。
- 管理 API 全部位於 `/api/v1/admin/*`；Vue 的 `/admin/*` 頁面前綴不是 API 授權邊界。
- 登入、登出、Token 確認、付款嘗試、附件、匯入預覽等協定或子資源操作可使用具名子資源，不強迫包成通用 Action。
- 同一資源若有前台與後台投影，使用不同 DTO 與 Policy；不得因 Route 相似而擴張資料可見範圍。

## 公開商品、搜尋與組裝

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-SEARCH-01 | `GET /api/v1/products` | Public | `ProductSearchQuery` → `PageResult<ProductCardDto>` | `validation_failed`、`search_sort_unsupported`、`search_filter_unsupported` |
| M 商品明細支撐 | `GET /api/v1/products/{id}` | Public | `ProductDetailDto`；只回已發布且可公開內容 | `resource_not_found` |
| M 搜尋篩選支撐 | `GET /api/v1/catalog/filter-options` | Public | `CatalogFilterOptionsQuery` → `CatalogFilterOptionsDto` | `validation_failed`、`search_filter_unsupported` |
| UC-AI-SEARCH-01～03 | `POST /api/v1/ai/product-search/recommendations` | Public＋額度 | `AiProductSearchRequest` → `AiProductSearchResultDto`；內含 Intent、補問、降級與用量 | `ai_usage_limit_exceeded`、`ai_output_invalid`、`ai_service_unavailable` |
| UC-BUILD-01 建立／清單 | `GET /api/v1/build-lists`；`POST /api/v1/build-lists` | Member | `PageResult<BuildListSummaryDto>`；`CreateBuildListRequest` → `201 BuildListDto` | `validation_failed` |
| UC-BUILD-01 明細／修改／刪除 | `GET /api/v1/build-lists/{id}`；`PUT /api/v1/build-lists/{id}`；`DELETE /api/v1/build-lists/{id}` | Owner Member | `BuildListDto`；修改與刪除帶 RowVersion | `resource_not_found`、`concurrency_conflict` |
| UC-BUILD-01 分享 | `POST /api/v1/build-lists/{id}/share`；`DELETE /api/v1/build-lists/{id}/share`；`GET /api/v1/build-shares/{token}` | 建立／撤銷為 Owner；讀取為 Public | `BuildShareDto`；公開讀取回去識別化 `SharedBuildDto` | `resource_not_found`、`concurrency_conflict` |
| UC-BUILD-01 加入購物車 | `POST /api/v1/build-lists/{id}/actions/add-to-cart` | Owner Member 或有效分享清單複本 | `AddBuildToCartRequest` → `CartDto`；需 Idempotency-Key | `build_incomplete`、`build_unavailable_item`、`inventory_insufficient`、`build_incompatible` |
| UC-COMPAT-01 | `POST /api/v1/compatibility-checks` | Public | `CompatibilityCheckRequest` → `CompatibilityCheckDto` | `validation_failed`、`build_incompatible` |

訪客組裝草稿保存在瀏覽器；登入後以前述 `POST /build-lists` 建立新的會員清單，不建立會覆寫既有清單的特殊匯入 Route。

## 會員、驗證、個人資料與通知

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| SH-05 Anti-forgery 支撐 | `GET /api/v1/security/antiforgery-token` | Public；可依指定 Scheme 綁定既有 Session | Header `X-DoSelect-Client: member\|admin` → `AntiforgeryTokenResponse{requestToken}`；`Cache-Control: no-store`；Token 只存前端記憶體 | `validation_failed` |
| M 會員 Session 支撐 | `GET /api/v1/auth/session` | Public／Member | 未登入回 `200 AuthSessionDto{isAuthenticated:false}`；登入回會員摘要 | — |
| UC-AUTH-01 | `POST /api/v1/auth/register`；`POST /api/v1/auth/email-verifications`；`POST /api/v1/auth/email-verifications/confirm` | Public | 註冊／驗證 Request → `202` 或會員摘要；註冊回應不可枚舉，已註冊與未註冊 Email 回傳完全相同的 `202` 形狀（相同 Shape，PublicId 為不指向真實帳號的合成值） | `email_token_invalid`（無效、已使用、撤銷、過期統一回應，決議 B1）、`rate_limit_exceeded` |
| UC-AUTH-02 | `POST /api/v1/auth/login`；`POST /api/v1/auth/logout` | Public／Member | Cookie Session；帳號鎖定、密碼錯誤與帳號不存在統一回 `invalid_credentials`，不對外區分（避免枚舉與鎖定攻擊 oracle） | `invalid_credentials`、`account_suspended`、`rate_limit_exceeded` |
| UC-AUTH-03 | `POST /api/v1/auth/password-resets`；`POST /api/v1/auth/password-resets/confirm` | Public | Request 不洩漏帳號是否存在 | `password_reset_token_invalid`（無效、已使用、撤銷、過期統一回應，決議 B1）、`rate_limit_exceeded` |
| M 會員資料支撐 | `GET /api/v1/members/me`；`PUT /api/v1/members/me` | Member | `MemberProfileDto`／`UpdateMemberProfileRequest`＋RowVersion | `concurrency_conflict` |
| M 收件地址支撐 | `GET /api/v1/members/me/addresses`；`POST /api/v1/members/me/addresses`；`PUT /api/v1/members/me/addresses/{id}`；`DELETE /api/v1/members/me/addresses/{id}` | Owner Member | `MemberAddressDto` 與 Create／Update Request；刪除不改歷史訂單快照 | `resource_not_found`、`concurrency_conflict` |
| M 站內通知支撐 | `GET /api/v1/notifications`；`POST /api/v1/notifications/{id}/actions/read`；`POST /api/v1/notifications/actions/read-all` | Member | `PageResult<NotificationDto>`；讀取命令冪等 | `resource_not_found` |
| M 管理 Session 支撐 | `GET /api/v1/admin/auth/session` | Public／Admin | 未登入或未完成 2FA 均不回管理權限；成功回角色／Policy 摘要 | — |
| UC-ADMIN-AUTH-01 | `POST /api/v1/admin/auth/login`；`POST /api/v1/admin/auth/totp/verify`；`POST /api/v1/admin/auth/recovery-codes/use` | Admin | 兩階段管理員 Cookie | `admin_two_factor_required`、`admin_two_factor_invalid`、`admin_recovery_code_invalid` |
| UC-GUEST-ORDER-01 | `POST /api/v1/guest-orders/access-requests`；`POST /api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend`；`POST /api/v1/guest-orders/access-verifications` | Public | `GuestOrderAccessRequest` → 永遠 202 `GuestOrderAccessRequestAcceptedDto`；Resend 維持安全回應；Verification → 30 分鐘限單 HttpOnly Cookie | `guest_order_verification_invalid`、`guest_order_access_expired`、`guest_order_scope_mismatch`、`rate_limit_exceeded` |

## 購物車、配送、訂單、付款與售後

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-CART-01 購物車明細 | `GET /api/v1/cart`；`POST /api/v1/cart/items`；`PATCH /api/v1/cart/items/{id}`；`DELETE /api/v1/cart/items/{id}` | Public Cart／Member | `CartDto`；Add／Update 帶 SKU、數量及必要 RowVersion | `sku_unavailable`、`cart_quantity_exceeded`、`cart_item_limit_exceeded`、`resource_not_found`、`concurrency_conflict` |
| UC-CART-01 重驗 | `POST /api/v1/cart/actions/revalidate` | Public Cart／Member | `CartValidationDto` | `cart_item_requires_attention`、`sku_unavailable` |
| UC-CART-02 | `POST /api/v1/cart/actions/merge` | Member | `CartMergeRequest` → `CartMergeResultDto`；需 Idempotency-Key | `cart_merge_conflict`（200，個別品項衝突）、`cart_item_limit_exceeded`（409，整次合併會超過購物車 100 品項上限而整批拒絕，Guest Cart 維持 Active）、`idempotency_payload_conflict` |
| UC-COUPON-01 | `POST /api/v1/cart/coupon`；`DELETE /api/v1/cart/coupon` | Public Cart／Member | `ApplyCouponRequest` → 更新後 `CartDto` | `coupon_not_applicable`、`coupon_usage_exhausted`、`coupon_not_active` |
| M 配送選項支撐 | `GET /api/v1/cart/shipping-options`；`GET /api/v1/convenience-stores` | Public Cart／Member | `ShippingOptionsDto`；門市使用 `ConvenienceStoreQuery` → `PageResult<ConvenienceStoreOptionDto>` | `shipping_method_not_allowed`、`shipping_constraint_exceeded` |
| M 訂單查詢支撐 | `GET /api/v1/orders`；`GET /api/v1/orders/{id}` | Member Owner；單筆亦允許有效 GuestOrderAccessToken | `OrderQuery` → `PageResult<OrderSummaryDto>`；`OrderDto` | `resource_not_found`、`guest_order_access_expired`、`guest_order_scope_mismatch` |
| M 模擬發票查詢支撐 | `GET /api/v1/orders/{orderId}/invoice` | Member Owner／有效 GuestOrderAccessToken | `SimulatedInvoiceDto`；只回遮蔽買受人資料及 DEMO 標記 | `resource_not_found`、`guest_order_access_expired`、`guest_order_scope_mismatch` |
| UC-CHECKOUT-01 | `POST /api/v1/orders` | Public／Member | `CreateOrderRequest` → `201 OrderDto`；需 Idempotency-Key | `inventory_insufficient`、`order_total_changed`、`order_total_below_minimum`、`cart_item_requires_attention` |
| UC-CHECKOUT-COD-01 | 同 `POST /api/v1/orders` | Public／Member | `paymentMethod = cashOnDelivery` | `payment_method_not_allowed`、`payment_cod_amount_exceeded`、`payment_cod_restricted_item`、`shipping_method_not_allowed` |
| M 訂單取消支撐 | `POST /api/v1/orders/{id}/actions/cancel` | Owner Member／有效 GuestOrderAccessToken | `CancelOrderRequest`＋RowVersion → `OrderDto` | `order_cancellation_not_allowed`、`order_state_conflict`、`concurrency_conflict` |
| UC-PAY-01 | `POST /api/v1/orders/{orderId}/payment-attempts`；`POST /api/v1/simulated-payments/{attemptId}/actions/complete` | 訂單擁有者／展示模擬權限 | `CreatePaymentAttemptRequest`、`CompleteSimulatedPaymentRequest` → `PaymentAttemptDto` | `payment_state_conflict`、`payment_attempt_expired`、`order_payment_deadline_expired` |
| UC-RETURN-01 | `POST /api/v1/orders/{orderId}/returns`；`GET /api/v1/returns/{id}`；`POST /api/v1/returns/{id}/attachments` | 訂單擁有者／GuestOrderAccessToken | `CreateReturnRequest` → `ReturnRequestDto`；附件遵守私有檔案契約 | `return_deadline_expired`、`return_quantity_exceeded`、`file_count_exceeded`、`file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable` |

## 管理後台型錄、圖片、匯入與相容性

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| M 商品管理支撐 | `GET /api/v1/admin/products`；`POST /api/v1/admin/products`；`GET /api/v1/admin/products/{id}`；`PUT /api/v1/admin/products/{id}` | CatalogManager／SuperAdmin；其他角色依矩陣只讀投影 | `AdminProductQuery`、`PageResult<AdminProductSummaryDto>`、`CreateProductRequest`（含第一個必填預設 SKU）、`UpdateProductRequest`、`AdminProductDetailDto`；Create 必須在同一 SQL 交易建立 Product、Tags 與預設 SKU | `product_code_duplicate`、`sku_code_duplicate`、`concurrency_conflict`、`specification_invalid` |
| M 商品批次操作 | `POST /api/v1/admin/products/actions/{action}`；`GET /api/v1/admin/products/export` | CatalogManager／SuperAdmin | Action 白名單：`publish`、`unpublish`、`adjust-price`；`BulkProductActionRequest`；匯出沿用目前 Filter | `validation_failed`、`product_unavailable`、`concurrency_conflict` |
| UC-ADM-PROD-01／02 | `POST /api/v1/admin/products/{productId}/skus`；`GET /api/v1/admin/skus/{id}`；`PUT /api/v1/admin/skus/{id}`；`DELETE /api/v1/admin/skus/{id}` | CatalogManager／SuperAdmin | `CreateSkuRequest`、`UpdateSkuRequest`、`SkuDto` | `sku_code_duplicate`、`sku_code_immutable`、`sku_delete_referenced`、`sku_default_required`、`sku_missing_required_specification`、`specification_invalid`、`sale_price_period_overlap`、`concurrency_conflict` |
| M 品牌／分類／標籤 | `GET/POST /api/v1/admin/brands`；`PUT /api/v1/admin/brands/{id}`；同型 Route 套用 `categories`、`tags` | CatalogManager／SuperAdmin | 各自 Query、Create／Update、`PageResult<CatalogLookupDto>` | `brand_code_duplicate`、`category_code_duplicate`、`tag_code_duplicate`、`concurrency_conflict` |
| M 規格範本 | `GET/POST /api/v1/admin/specification-definitions`；`PUT /api/v1/admin/specification-definitions/{id}`；`POST /api/v1/admin/specification-definitions/{id}/actions/disable` | CatalogManager／SuperAdmin | `SpecificationDefinitionDto`；Semantic Key／型別受保護 | `specification_semantic_key_duplicate`、`specification_definition_referenced`、`concurrency_conflict` |
| M 商品圖片 | `POST /api/v1/admin/products/{productId}/images`；`GET /api/v1/admin/product-images/{imageId}/preview/{variant}`；`PATCH /api/v1/admin/product-images/{imageId}`；`POST /api/v1/admin/product-images/{imageId}/actions/publish`；`DELETE /api/v1/admin/product-images/{imageId}` | CatalogImage 對應 Policy | DTO 與檔案限制依 [[03-架構/04-安全與檔案/檔案與圖片儲存設計]] | `file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable`、`image_processing_failed`、`image_metadata_incomplete`、`concurrency_conflict` |
| UC-IMPORT-01 | `GET /api/v1/admin/import-templates/products/current`；`POST /api/v1/admin/product-imports/preview`；`GET /api/v1/admin/product-imports/{id}`；`GET /api/v1/admin/product-imports/{id}/rows`；`GET /api/v1/admin/product-imports/{id}/errors`；`POST /api/v1/admin/product-imports/{id}/actions/confirm` | CatalogManager／SuperAdmin | Multipart、`ProductImportBatchDto`、`CursorPage<ProductImportRowDto>` | `import_format_unsupported`、`import_validation_failed`、`import_preview_expired`、`import_already_committed`、`import_batch_expired` |
| UC-COMPAT-01 後台 | `GET /api/v1/admin/compatibility-rules`；`PATCH /api/v1/admin/compatibility-rules/{ruleCode}/warning-settings`；`PATCH /api/v1/admin/compatibility-rules/{ruleCode}/activation`；`POST /api/v1/admin/compatibility-rules/test` | CatalogManager／SuperAdmin；啟停限 SuperAdmin | DTO 依 [[03-架構/07-領域設計/相容性規則後台設計]]。SKU 硬性相容性事實依 DEC-BATCH-027 改由既有 Catalog／SKU 規格管理端點寫入，不再有 Builds 模組自己的一組 SKU 屬性 API | `compatibility_threshold_out_of_range`、`concurrency_conflict`、`authorization_forbidden` |

## 管理後台庫存、訂單與物流

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| M 庫存查詢支撐 | `GET /api/v1/admin/inventory/balances`；`GET /api/v1/admin/inventory/movements` | InventoryManager／SuperAdmin；其他角色依矩陣使用遮蔽投影 | `InventoryBalanceQuery`、`InventoryMovementQuery` → `PageResult<T>` | `validation_failed`、`search_sort_unsupported` |
| UC-ADM-INV-01 保留 | `GET /api/v1/admin/inventory/reservations`；`POST /api/v1/admin/inventory/reservations/{id}/actions/release` | InventoryManager／SuperAdmin | `CursorPage<InventoryReservationDto>`；`ReleaseReservationRequest`＋RowVersion | `inventory_reservation_not_active`、`inventory_reservation_already_processed`、`concurrency_conflict` |
| UC-ADM-INV-01 匯入 | `POST /api/v1/admin/inventory-imports/preview`；`GET /api/v1/admin/inventory-imports/{id}`；`GET /api/v1/admin/inventory-imports/{id}/rows`；`GET /api/v1/admin/inventory-imports/{id}/errors`；`POST /api/v1/admin/inventory-imports/{id}/actions/confirm` | InventoryManager／SuperAdmin | `InventoryImportBatchDto`、預覽列、錯誤 CSV、原子提交 | `inventory_import_validation_failed`、`import_already_committed`、`import_batch_expired`、`concurrency_conflict` |
| UC-ADM-SHIP-01 | `GET /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST /api/v1/admin/shipping-providers/{id}/package-limit-versions/{versionId}/actions/publish` | OrderManager／SuperAdmin | Draft／Publish DTO＋RowVersion | `validation_failed`、`package_limit_period_overlap`、`concurrency_conflict` |
| UC-ADM-STORE-01 | `GET/POST /api/v1/admin/convenience-stores`；`PUT /api/v1/admin/convenience-stores/{id}` | OrderManager／SuperAdmin；CatalogManager 只讀 | `PageResult<ConvenienceStoreDto>`／Store DTO＋RowVersion | `store_code_duplicate`、`concurrency_conflict` |
| UC-ADM-ORDER-01 | `GET /api/v1/admin/orders`；`GET /api/v1/admin/orders/{id}`；`POST /api/v1/admin/orders/{id}/actions/{action}` | OrderManager／相關敏感 Policy | `CursorPage<AdminOrderSummaryDto>`、`AdminOrderDto`、合法命令 | `order_state_conflict`、`order_cancellation_not_allowed`、`concurrency_conflict` |
| UC-ADM-ORDER-02 | `GET /api/v1/admin/orders/{id}/recipient` | OrderManager／PrivacyAdmin／SuperAdmin，依用途 | `OrderRecipientDto` | `resource_not_found`、`authorization_forbidden` |
| UC-ADM-SHIP-02 | `POST /api/v1/admin/shipments/batches`；`GET /api/v1/admin/shipments/batches/{id}/result.csv` | OrderManager／SuperAdmin | 最多 100 筆 → `BatchShipmentResultDto` | `shipping_batch_limit_exceeded`；逐筆 `shipping_order_not_ready`、`shipping_tracking_duplicate`、`shipping_method_not_allowed` |

## 管理後台退貨、退款、優惠券與模擬發票

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-RETURN-01 後台查詢 | `GET /api/v1/admin/returns`；`GET /api/v1/admin/returns/{id}` | OrderManager／SuperAdmin；其他角色依矩陣只讀摘要 | `AdminReturnQuery` → `PageResult<AdminReturnSummaryDto>`；`AdminReturnDetailDto` | `resource_not_found`、`authorization_forbidden` |
| UC-RETURN-01 後台流程 | `POST /api/v1/admin/returns/{id}/actions/{action}` | OrderManager／SuperAdmin | Action 白名單：`receive`、`inspect`、`extend-shipment-deadline`；各命令含 RowVersion 與理由 | `return_state_conflict`、`return_shipment_extension_not_allowed`、`concurrency_conflict` |
| UC-RETURN-01 寄回物流 | `GET /api/v1/admin/returns/{id}/shipment`；`POST /api/v1/admin/returns/{id}/shipment`；`POST /api/v1/admin/returns/{id}/shipment/events` | OrderManager／SuperAdmin；事件端點限模擬 Provider／內部工作 | `CreateReturnShipmentRequest`、`ReturnShipmentDto`、`AppendReturnShipmentEventRequest`；每案最多一個有效寄回批次 | `return_state_conflict`、`concurrency_conflict`、`authorization_forbidden` |
| UC-REFUND-01 退貨審核 | `POST /api/v1/admin/returns/{id}/actions/review` | `Return.Approve`：OrderManager／SuperAdmin | `ApproveReturnRequest` → `ReturnRequestDto` | `return_state_conflict`、`concurrency_conflict` |
| UC-REFUND-01 退款 | `GET /api/v1/admin/refunds`；`GET /api/v1/admin/refunds/{id}`；`POST /api/v1/admin/refunds/{id}/actions/execute` | FinanceManager／SuperAdmin；查詢依角色矩陣 | `AdminRefundQuery`、`PageResult<RefundDto>`、`ExecuteRefundRequest` | `refund_amount_exceeded`、`refund_state_conflict`、`refund_snapshot_unavailable`、`concurrency_conflict` |
| M 優惠券管理支撐 | `GET/POST /api/v1/admin/coupons`；`GET/PUT /api/v1/admin/coupons/{id}`；`POST /api/v1/admin/coupons/{id}/actions/{action}` | `Coupon.Manage`：FinanceManager／MarketingAnalyst／SuperAdmin | Action 白名單：`activate`、`pause`、`disable`；`CouponDto` 與管理 Request | `coupon_code_duplicate`、`coupon_state_conflict`、`validation_failed`、`concurrency_conflict` |
| M 模擬發票查詢支撐 | `GET /api/v1/admin/invoices`；`GET /api/v1/admin/invoices/{id}` | `Invoice.Manage`：FinanceManager／SuperAdmin | `AdminInvoiceQuery` → `PageResult<AdminInvoiceSummaryDto>`；`AdminInvoiceDto` | `resource_not_found`、`authorization_forbidden` |
| M 模擬發票開立 | `POST /api/v1/admin/orders/{orderId}/invoices` | `Invoice.Manage`：FinanceManager／SuperAdmin | `IssueSimulatedInvoiceRequest` → `201 AdminInvoiceDto`；需 Idempotency-Key | `invoice_order_unpaid`、`invoice_order_cancelled`、`invoice_already_exists`、`idempotency_payload_conflict`、`concurrency_conflict` |
| M 模擬發票作廢 | `POST /api/v1/admin/invoices/{id}/actions/void` | `Invoice.Manage`：FinanceManager／SuperAdmin | `VoidSimulatedInvoiceRequest` → `AdminInvoiceDto` | `invoice_state_conflict`、`invoice_allowance_required`、`concurrency_conflict` |
| M 模擬折讓建立 | `POST /api/v1/admin/invoices/{id}/allowances` | `Invoice.Manage`：FinanceManager／SuperAdmin | `CreateSimulatedInvoiceAllowanceRequest` → `201 SimulatedInvoiceAllowanceDto`；需 Idempotency-Key；金額由成功 Refund 推導 | `invoice_state_conflict`、`refund_state_conflict`、`idempotency_payload_conflict`、`concurrency_conflict` |

`Invoice.Manage` 與 `Coupon.Manage` 均沿用管理員 Policy 基線，要求管理員身分與 TOTP／MFA。兩者只定義授權，不取代狀態機、冪等、RowVersion、金額／名額規則或 Audit；前台 `POST/DELETE /api/v1/cart/coupon` 不套用 `Coupon.Manage`。

## AI 客服、人工客服與案件工作台

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AI-SUPPORT-01 | `POST /api/v1/ai/consents`；`DELETE /api/v1/ai/consents/current` | Member | 同意版本／撤回 | `ai_consent_required` |
| UC-AI-SUPPORT-02／03 | `POST /api/v1/ai/support/messages` | Member＋有效同意 | `AiSupportMessageRequest` → `AiSupportAnswerDto`；只讀工具且不執行商業寫入；功能旗標關閉時 Fail Closed | `validation_failed`、`ai_consent_required`、`ai_usage_limit_exceeded`、`ai_order_access_denied`、`ai_output_invalid`、`ai_service_unavailable`、`ai_tool_not_allowed` |
| UC-AI-SUPPORT-04 | `GET /api/v1/ai/usage/me`；`GET /api/v1/admin/ai/usage` | Member；後台依角色矩陣 | `AiUsageDto`、`AdminAiUsageReportDto` | `ai_usage_limit_exceeded`、`ai_budget_protection_active`、`authorization_forbidden` |
| UC-SUPPORT-01 顧客端 | `GET /api/v1/support-tickets`；`POST /api/v1/support-tickets`；`GET /api/v1/support-tickets/{id}`；`POST /api/v1/support-tickets/{id}/messages`；`POST /api/v1/support-tickets/{id}/actions/cancel` | Member Owner | `SupportTicketQuery`、`PageResult<SupportTicketSummaryDto>`、Ticket／Message／Cancel DTO | `support_ticket_state_conflict`、`support_ticket_cancel_not_allowed`、`resource_not_found` |
| UC-SUPPORT-02 | `POST /api/v1/support-tickets/{id}/attachments`；`GET /api/v1/private-attachments/{id}/content` | 案件擁有者／授權客服 | Multipart／授權串流 | `file_count_exceeded`、`file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable` |
| UC-SUPPORT-01 後台明細 | `GET /api/v1/admin/support-tickets/{id}`；`POST /api/v1/admin/support-tickets/{id}/internal-notes`；`POST /api/v1/admin/support-tickets/{id}/actions/{action}` | 讀取依客服角色；internal-notes、`claim`、一般 `change-priority`、`change-status`、`cancel`、`reopen`：`SupportTicket.Handle`；`assign`、`transfer`、優先級覆核／覆寫：`SupportTicket.Supervise` | Action 白名單：`claim`、`assign`、`transfer`、`change-priority`、`change-status`、`cancel`、`reopen`；同一 `change-priority` 命令依是否為一般調整或主管覆核／覆寫選用 Policy，不新增未定義 Action | `support_ticket_assignment_conflict`、`support_ticket_state_conflict`、`support_ticket_cancel_not_allowed`、`concurrency_conflict` |
| UC-SLA-01 | `GET /api/v1/admin/support-tickets/sla` | CustomerService／Supervisor | `CursorPage<SupportSlaItemDto>` | `authorization_forbidden` |
| UC-WORKBENCH-01 | `GET /api/v1/admin/case-workbench` | 各角色只見可授權領域 | `CaseWorkbenchQuery` → `CursorPage<CaseWorkbenchItemDto>` | `search_sort_unsupported` |

客服指派競爭回 `409 support_ticket_assignment_conflict` 時只使用標準 Problem Details，不附最新承辦人 PublicId 或 DisplayName。前端收到後必須失效並重新查詢案件明細與所屬佇列，以最新 `SupportTicketDto` 的 Assignee、RowVersion 與 AvailableActions 重建畫面。

工作台固定使用 `LastActivityAtUtc／CasePublicId` Cursor，且只回傳正式 12 欄；RowVersion 與 AvailableActions 必須向來源領域詳情取得。UNION 分支授權與驗收見 [[03-架構/07-領域設計/統一案件工作台設計]]。

## 報表

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-REPORT-01 | `GET /api/v1/admin/reports/{reportKey}`；`GET /api/v1/admin/reports/{reportKey}/export` | 一般：MarketingAnalyst／FinanceManager／SuperAdmin；財務成本：FinanceManager／SuperAdmin；客服 SLA：CustomerServiceSupervisor／SuperAdmin | `ReportQuery` → 摘要＋`CursorPage<ReportRowDto>`／匯出檔 | `report_key_invalid`、`report_range_invalid`、`authorization_forbidden` |

## 分頁契約索引

- 一般列表固定使用 `pageNumber/pageSize` 與 `PageResult<T>`；商品、會員訂單、組裝清單、通知、門市、分類、品牌、標籤、SKU、規格、庫存餘額／異動、退貨、退款、優惠券及模擬發票不得自行改用 Cursor。
- 只有下列快速變動或大筆逐列資料使用 `cursor/pageSize` 與 `CursorPage<T>`：庫存保留、後台訂單、客服 SLA 佇列、統一案件工作台、匯入預覽列、報表明細列。
- 商品與庫存匯入預覽列分別使用 `GET /api/v1/admin/product-imports/{id}/rows` 與 `GET /api/v1/admin/inventory-imports/{id}/rows`；新增 Cursor 例外必須同時更新 [[03-架構/02-API與前端契約/API共通規範]] 與本索引。

## 完成與實作邊界

- 本目錄已補齊 M 桌面頁面需要的讀取、查詢、修改及狀態命令，不代表 Endpoint 程式已建立。
- 實際 ASP.NET Core OpenAPI、TypeScript Client、Policy、Controller 與契約測試須等 Solution 建立後實作。
- 新增 Endpoint 不得只補 Route；必須同步建立具名 Schema、錯誤碼引用、Policy／資源所有權測試及前端頁面對照。
- S 的收藏、評價、檢舉、多語系、RWD、AI 摘要與 AI 報表分析，以及 O 的自然語言營運查詢不列入本版目錄。
