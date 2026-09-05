---
文件狀態: 已確認
最後更新: 2026-08-31
追蹤項目:
  - DES-10
  - DES-16
  - DES-20
  - DES-22
  - DES-23
  - DES-25
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
| M Checkout 政策版本支撐 | `GET /api/v1/checkout/policy-versions` | Public | 無 Request → `200 AcceptedPolicyVersions`；只回目前 Terms／Return／Privacy 三個版本，不回伺服器 ShippingConstraint | — |
| UC-SEARCH-01 | `GET /api/v1/products` | Public | `ProductSearchQuery` → `PageResult<ProductCardDto>` | `validation_failed`、`search_sort_unsupported`、`search_filter_unsupported` |
| M 商品明細支撐 | `GET /api/v1/products/{id}` | Public | `ProductDetailDto`；只回已發布且可公開內容 | `resource_not_found` |
| SH-06 公開商品圖片 | `GET /media/products/{publicId}/{variant}/{contentHash}.webp` | Public | `variant` 只接受 `320`、`800`、`1600`；`contentHash` 為該 WebP 衍生檔 SHA-256 小寫 Hex；成功回 `image/webp` 與一年 immutable Cache | Hash／狀態／檔案不符均回無內容 `404` |
| M 搜尋篩選支撐 | `GET /api/v1/catalog/filter-options` | Public | `CatalogFilterOptionsQuery` → `CatalogFilterOptionsDto` | `validation_failed`、`search_filter_unsupported` |
| UC-AI-SEARCH-01～03 | `POST /api/v1/ai/product-search/recommendations` | Public＋額度 | `AiProductSearchRequest` → `AiProductSearchResultDto`；內含 `searchPublicId`、Intent、未確認 `proposedExistingParts`、補問、後端核准推薦、可選 `customBuild`、一般搜尋降級與用量。CustomBuild 成功時回八類完整清單；最高預算計新購小計＋NT$300 組裝費，既有零件不計價但參與相容性。存在自然語言既有零件候選時先回 `clarification`，確認前不查商品或計算相容性；AI／Schema／理由驗證失敗以 `200 degraded + keywordSearch` 明確降級；額度或預算保護才回 Problem Details | `ai_usage_limit_exceeded`、`ai_budget_protection_active`、`ai_service_unavailable` |
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
| UC-ADMIN-AUTH-01 登入／首次綁定 | `POST /api/v1/admin/auth/login`；`POST /api/v1/admin/auth/totp/verify`；`POST /api/v1/admin/auth/recovery-codes/use`；`POST /api/v1/admin/auth/totp/enroll/begin`；`POST /api/v1/admin/auth/totp/enroll/confirm` | 登入、驗證及首次綁定為短效 Admin Challenge；成功後才建立 Admin Session | 兩階段管理員 Cookie；首次綁定確認後 Recovery Codes 只顯示一次 | `invalid_credentials`、`account_suspended`、`admin_two_factor_invalid`、`admin_recovery_code_invalid`、`admin_challenge_invalid`、`admin_challenge_rate_limited` |
| UC-ADMIN-AUTH-01 登出／TOTP 重綁 | `POST /api/v1/admin/auth/logout`；`POST /api/v1/admin/auth/totp/rebind/begin`；`POST /api/v1/admin/auth/totp/rebind/confirm` | Admin；重綁 Begin 另需現有 TOTP 或單組 Recovery Code Step-up，Confirm 另需短效 Rebind Challenge | 登出回 204；重綁成功產生新 Recovery Codes、撤銷舊 Session 並重簽目前 Session | `admin_rebind_step_up_required`、`admin_two_factor_invalid`、`admin_recovery_code_invalid`、`admin_challenge_invalid`、`admin_challenge_rate_limited`、`authorization_forbidden` |
| UC-GUEST-ORDER-01 | `POST /api/v1/guest-orders/access-requests`；`POST /api/v1/guest-orders/access-requests/{requestPublicId}/actions/resend`；`POST /api/v1/guest-orders/access-verifications` | Public | `GuestOrderAccessRequest` → 永遠 202 `GuestOrderAccessRequestAcceptedDto`；Resend 維持安全回應；Verification → 30 分鐘限單 HttpOnly Cookie | `guest_order_verification_invalid`、`guest_order_access_expired`、`guest_order_scope_mismatch`、`rate_limit_exceeded` |

## 購物車、配送、訂單、付款與售後

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-CART-01 購物車明細 | `GET /api/v1/cart`；`POST /api/v1/cart/items`；`PATCH /api/v1/cart/items/{id}`；`DELETE /api/v1/cart/items/{id}` | Public Cart／Member | `CartDto`；Add／Update 帶 SKU、數量及必要 RowVersion | `sku_unavailable`、`cart_quantity_exceeded`、`cart_item_limit_exceeded`、`resource_not_found`、`concurrency_conflict`、`cart_assembly_item_immutable`（PATCH／DELETE 對屬於 AssemblyGroupKey 的品項一律拒絕，只能整組處理） |
| UC-CART-01 組裝群組整組移除 | `DELETE /api/v1/cart/assembly-groups/{assemblyGroupKey}` | Public Cart／Member | `RemoveAssemblyGroupRequest`（Cart 層級 RowVersion，置於 DELETE Body）→ 更新後 `CartDto`；同一交易內移除該 AssemblyGroupKey 的全部品項，不會部分成功 | `resource_not_found`、`concurrency_conflict`、`validation_failed` |
| UC-CART-01 重驗 | `POST /api/v1/cart/actions/revalidate` | Public Cart／Member | `CartValidationDto`；屬於 AssemblyGroupKey 的品項其 `availableActions` 一律為 `["remove-group"]`（`reduce-quantity`／`remove` 會被 `cart_assembly_item_immutable` 拒絕，不得回報） | `cart_item_requires_attention`、`sku_unavailable` |
| UC-CART-02 | `POST /api/v1/cart/actions/merge` | Member | `CartMergeRequest` → `CartMergeResultDto`；需 Idempotency-Key | `cart_merge_conflict`（200，個別品項衝突）、`cart_item_limit_exceeded`（409，整次合併會超過購物車 100 品項上限而整批拒絕，Guest Cart 維持 Active）、`idempotency_payload_conflict` |
| UC-COUPON-01 | `POST /api/v1/cart/coupon`；`DELETE /api/v1/cart/coupon` | Public Cart／Member | `ApplyCouponRequest` → 更新後 `CartDto` | `coupon_not_applicable`、`coupon_usage_exhausted`、`coupon_not_active` |
| M 配送選項支撐 | `GET /api/v1/cart/shipping-options?couponCode={code?}`；`GET /api/v1/convenience-stores` | Public Cart／Member | `ShippingOptionsDto`；Coupon Code 可選、最長 64 字元，提供時以後端 Coupon Quote 重算免運、運費與 COD；門市使用 `ConvenienceStoreQuery` → `PageResult<ConvenienceStoreOptionDto>` | `coupon_not_applicable`、`coupon_usage_exhausted`、`coupon_not_active`、`shipping_method_not_allowed`、`shipping_constraint_exceeded` |
| M 訂單查詢支撐 | `GET /api/v1/orders`；`GET /api/v1/orders/{id}` | Member Owner；單筆亦允許有效 GuestOrderAccessToken | `OrderQuery` → `PageResult<OrderSummaryDto>`；`OrderDto` | `resource_not_found`、`guest_order_access_expired`、`guest_order_scope_mismatch` |
| M 模擬發票查詢支撐 | `GET /api/v1/orders/{orderId}/invoice` | Member Owner／有效 GuestOrderAccessToken | `SimulatedInvoiceDto`；只回遮蔽買受人資料及 DEMO 標記 | `resource_not_found`、`guest_order_access_expired`、`guest_order_scope_mismatch` |
| UC-CHECKOUT-01 | `POST /api/v1/orders` | Public／Member | `CreateOrderRequest` → `201 OrderDto`；需 Idempotency-Key | `inventory_insufficient`、`order_total_changed`、`order_total_below_minimum`、`cart_item_requires_attention` |
| UC-CHECKOUT-COD-01 | 同 `POST /api/v1/orders` | Public／Member | `paymentMethod = cashOnDelivery` | `payment_method_not_allowed`、`payment_cod_amount_exceeded`、`payment_cod_restricted_item`、`shipping_method_not_allowed` |
| M 訂單取消支撐 | `POST /api/v1/orders/{id}/actions/cancel` | Owner Member／有效 GuestOrderAccessToken | `CancelOrderRequest`＋RowVersion → `OrderDto` | `order_cancellation_not_allowed`、`order_state_conflict`、`concurrency_conflict` |
| UC-PAY-01 | `GET /api/v1/orders/{orderId}/payment-attempts/latest`；`POST /api/v1/orders/{orderId}/payment-attempts`；`POST /api/v1/simulated-payments/{attemptId}/actions/complete` | Owner Member／有效 GuestOrderAccessToken；產品完成端點另限 Demo Profile＋`Demo:SimulationEndpointsEnabled=true`，Cookie 寫入由全域 Antiforgery 保護；COD 不得使用此完成端點。依 DEC-P356，隔離 E2E Environment 可用相同顯式開關驗證此契約，但不是公開產品 Profile | Latest GET 回包含終態的最新 `PaymentAttemptDto`；訂單不存在或尚無 Attempt 為 `404`，匿名為 `401`。建立／完成使用 `CreatePaymentAttemptRequest`、`CompleteSimulatedPaymentRequest` → `PaymentAttemptDto`；`simulationKey` 同時作為唯一重播鍵與模擬 Provider Event 鍵；即時付款支援 `succeeded`／`failed`／`cancelled`，遞延付款另支援 `expired` | `authentication_required`、`guest_order_access_expired`、`guest_order_scope_mismatch`、`resource_not_found`、`payment_state_conflict`、`payment_attempt_expired`、`order_payment_deadline_expired`、`payment_event_duplicate`、`idempotency_payload_conflict` |
| UC-RETURN-01 | `POST /api/v1/orders/{orderId}/returns`；`GET /api/v1/returns/{id}`；`POST /api/v1/returns/{id}/attachments` | 訂單擁有者／GuestOrderAccessToken | `CreateReturnRequest` → `ReturnRequestDto`；附件遵守私有檔案契約 | `return_deadline_expired`、`return_quantity_exceeded`、`file_count_exceeded`、`file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable` |

## 管理後台型錄、圖片、匯入與相容性

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| M 商品管理支撐 | `GET /api/v1/admin/products`；`POST /api/v1/admin/products`；`GET /api/v1/admin/products/{id}`；`PUT /api/v1/admin/products/{id}` | CatalogManager／SuperAdmin；其他角色依矩陣只讀投影 | `AdminProductQuery`、`PageResult<AdminProductSummaryDto>`、`CreateProductRequest`（含第一個必填預設 SKU）、`UpdateProductRequest`、`AdminProductDetailDto`；Create 必須在同一 SQL 交易建立 Product、Tags 與預設 SKU | `product_code_duplicate`、`sku_code_duplicate`、`concurrency_conflict`、`specification_invalid` |
| M 商品批次操作 | `POST /api/v1/admin/products/actions/{action}`；`GET /api/v1/admin/products/export` | CatalogManager／SuperAdmin | Action 白名單：`publish`、`unpublish`、`adjust-price`；`BulkProductActionRequest`；匯出沿用目前 Filter | `validation_failed`、`product_unavailable`、`concurrency_conflict` |
| UC-ADM-PROD-01／02 | `POST /api/v1/admin/products/{productId}/skus`；`GET /api/v1/admin/skus/{id}`；`PUT /api/v1/admin/skus/{id}`；`DELETE /api/v1/admin/skus/{id}` | CatalogManager／SuperAdmin | `CreateSkuRequest`、`UpdateSkuRequest`、`SkuDto` | `sku_code_duplicate`、`sku_code_immutable`、`sku_delete_referenced`、`sku_default_required`、`sku_missing_required_specification`、`specification_invalid`、`sale_price_period_overlap`、`concurrency_conflict` |
| M 品牌／分類／標籤 | `GET/POST /api/v1/admin/brands`；`PUT /api/v1/admin/brands/{id}`；同型 Route 套用 `categories`、`tags` | CatalogManager／SuperAdmin | 各自 Query、Create／Update、`PageResult<CatalogLookupDto>` | `brand_code_duplicate`、`category_code_duplicate`、`tag_code_duplicate`、`category_parent_invalid`、`reference_not_found`、`concurrency_conflict` |
| M 規格範本 | `GET/POST /api/v1/admin/specification-definitions`；`PUT /api/v1/admin/specification-definitions/{id}`；`POST /api/v1/admin/specification-definitions/{id}/actions/disable` | CatalogManager／SuperAdmin | `SpecificationDefinitionDto`；Semantic Key／型別受保護 | `specification_semantic_key_duplicate`、`specification_definition_referenced`、`concurrency_conflict` |
| M 商品圖片 | `POST /api/v1/admin/products/{productId}/images`；`GET /api/v1/admin/product-images/{imageId}/preview/{variant}`；`PATCH /api/v1/admin/product-images/{imageId}`；`POST /api/v1/admin/product-images/{imageId}/actions/publish`；`DELETE /api/v1/admin/product-images/{imageId}` | `CatalogImage.Manage`（上傳／PATCH／DELETE）、`CatalogImage.ViewDraft`（預覽；未登入、無權限、不存在一律 404）、`CatalogImage.Publish`；三者皆 CatalogManager／SuperAdmin | DTO 與檔案限制依 [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]；`AdminProductImageDto`、`UpdateProductImageRequest`、`ProductImageActionRequest`＋RowVersion；上傳後 Ready，發布後 `AdminProductDetailDto.images[].variants[].publicUrl` 與前台 `ProductDetailDto.images`／商品卡 `primaryImage` 才有值 | `file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable`、`image_processing_failed`、`image_metadata_incomplete`、`concurrency_conflict` |
| UC-IMPORT-01 | `GET /api/v1/admin/import-templates/products/current`；`POST /api/v1/admin/product-imports/preview`；`GET /api/v1/admin/product-imports/{id}`；`GET /api/v1/admin/product-imports/{id}/rows`；`GET /api/v1/admin/product-imports/{id}/errors`；`POST /api/v1/admin/product-imports/{id}/actions/confirm` | CatalogManager／SuperAdmin | Multipart、`ProductImportBatchDto`、`CursorPage<ProductImportRowDto>` | `import_format_unsupported`、`import_validation_failed`、`import_preview_expired`、`import_already_committed`、`import_batch_expired` |
| UC-COMPAT-01 後台 | `GET /api/v1/admin/compatibility-rules`；`PATCH /api/v1/admin/compatibility-rules/{ruleCode}/warning-settings`；`PATCH /api/v1/admin/compatibility-rules/{ruleCode}/activation`；`POST /api/v1/admin/compatibility-rules/test` | CatalogManager／SuperAdmin；啟停限 SuperAdmin | DTO 依 [[03-架構/07-領域設計/相容性規則後台設計]]。SKU 硬性相容性事實依 DEC-BATCH-027 改由既有 Catalog／SKU 規格管理端點寫入，不再有 Builds 模組自己的一組 SKU 屬性 API | `compatibility_threshold_out_of_range`、`concurrency_conflict`、`authorization_forbidden` |

## 管理後台庫存、訂單與物流

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| M 庫存查詢支撐 | `GET /api/v1/admin/inventory/balances`；`GET /api/v1/admin/inventory/movements` | InventoryManager／SuperAdmin；其他角色依矩陣使用遮蔽投影 | `InventoryBalanceQuery`、`InventoryMovementQuery` → `PageResult<T>` | `validation_failed`、`search_sort_unsupported` |
| UC-ADM-INV-01 保留 | `GET /api/v1/admin/inventory/reservations`；`POST /api/v1/admin/inventory/reservations/{id}/actions/release` | InventoryManager／SuperAdmin | `CursorPage<InventoryReservationDto>`（只有 Active 列出 `availableActions:["release"]`）；`ReleaseReservationRequest`＋RowVersion → 204。釋放與 `inventory_reservation.release` 中央 Audit（含 note）、InventoryMovement 同一次提交；重送靠 RowVersion 擋 | `validation_failed`（未填原因／備註或 reasonCode 不在白名單）、`inventory_reservation_not_active`、`inventory_reservation_already_processed`、`concurrency_conflict` |
| UC-ADM-INV-01 匯入 | `POST /api/v1/admin/inventory-imports/preview`；`GET /api/v1/admin/inventory-imports/{id}`；`GET /api/v1/admin/inventory-imports/{id}/rows`；`GET /api/v1/admin/inventory-imports/{id}/errors`；`POST /api/v1/admin/inventory-imports/{id}/actions/confirm` | InventoryManager／SuperAdmin | `InventoryImportBatchDto`、預覽列、錯誤 CSV、原子提交 | `inventory_import_validation_failed`、`import_already_committed`、`import_batch_expired`、`concurrency_conflict` |
| UC-ADM-SHIP-01 | `GET /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST /api/v1/admin/shipping-providers/{id}/package-limit-versions/{versionId}/actions/publish` | OrderManager／SuperAdmin | Draft／Publish DTO＋RowVersion | `validation_failed`、`package_limit_period_overlap`、`concurrency_conflict` |
| UC-ADM-STORE-01 | `GET/POST /api/v1/admin/convenience-stores`；`PUT /api/v1/admin/convenience-stores/{id}` | OrderManager／SuperAdmin；CatalogManager 只讀 | `PageResult<ConvenienceStoreDto>`／Store DTO＋RowVersion | `store_code_duplicate`、`concurrency_conflict` |
| UC-ADM-ORDER-01 | `GET /api/v1/admin/orders`；`GET /api/v1/admin/orders/{id}`；`POST /api/v1/admin/orders/{id}/actions/{action}` | OrderManager／相關敏感 Policy | `CursorPage<AdminOrderSummaryDto>`、`AdminOrderDto`（含 `shipment` 摘要、歷程與後端計算的 `availableActions`）、合法命令 | `order_state_conflict`、`order_cancellation_not_allowed`、`concurrency_conflict` |
| UC-ADM-ORDER-02 | `GET /api/v1/admin/orders/{id}/recipient` | OrderManager／PrivacyAdmin／SuperAdmin，依用途 | `OrderRecipientDto` | `resource_not_found`、`authorization_forbidden` |
| SH-08 Outbox 人工重送 | `POST /api/v1/admin/outbox-messages/{publicId}/actions/retry` | `Outbox.Retry`（完成 MFA 的 SuperAdmin） | `RetryOutboxMessageRequest{reasonCode}` → `202 RetryOutboxMessageResponse`；只將 Failed 改回 Pending，不重設 AttemptCount、不改 Payload；狀態與中央 Audit 同次提交 | `validation_failed`、`outbox_message_not_found`、`outbox_message_not_retryable`、`concurrency_conflict` |
| UC-ADM-SHIP-02 | `POST /api/v1/admin/shipments/batches` | OrderManager／SuperAdmin | 最多 100 筆 → `BatchShipmentResultDto`；逐筆結果與 CSV 由這份同步回應在前端就地產生（PR #93 裁定 A1：不新增 ShipmentBatch 表，故無結果重新下載端點）。`idempotencyKey` 沿用 `IdempotencyRecords`，同鍵同 payload 重播原結果 | `shipping_batch_limit_exceeded`；逐筆 `shipping_order_not_ready`、`shipping_tracking_duplicate`、`shipping_method_not_allowed`、`concurrency_conflict`；整批 `idempotency_payload_conflict`、`idempotency_request_in_progress` |

## 管理後台退貨、退款、優惠券與模擬發票

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-RETURN-01 後台查詢 | `GET /api/v1/admin/returns`；`GET /api/v1/admin/returns/{id}` | OrderManager／SuperAdmin；其他角色依矩陣只讀摘要 | `AdminReturnQuery` → `PageResult<AdminReturnSummaryDto>`；`AdminReturnDetailDto` | `resource_not_found`、`authorization_forbidden` |
| UC-RETURN-01 後台流程 | `POST /api/v1/admin/returns/{id}/actions/{action}` | OrderManager／SuperAdmin | Action 白名單：`receive`、`inspect`、`extend-shipment-deadline`；各命令含 RowVersion 與理由 | `return_state_conflict`、`return_shipment_extension_not_allowed`、`concurrency_conflict` |
| UC-ADM-SHIP-03 物流狀態命令 | `POST /api/v1/admin/shipments/{shipmentPublicId}/actions/{action}`；action＝`in-transit`、`delivered`、`pickup-ready`、`picked-up`、`delivery-failed`、`returned` | ShippingManage（OrderManager／SuperAdmin） | 必帶 `Idempotency-Key`；Body `ShipmentStatusActionRequest { shipmentRowVersion, reasonCode?, note? }`（`delivery-failed`／`returned` 必填 reasonCode，note ≤500 且須通過中央 Audit note 規則；不接受 occurredAtUtc，一律伺服器 UTC）→ 更新後的 `AdminOrderDto`。狀態轉移、ShipmentStatusHistory、Order Fulfillment 投影與 OrderStatusHistory、中央 Audit（`shipment.mark_*`）、通知 Outbox 同一交易；宅配才允許 Delivered、超取才允許 PickupReady／PickedUp；進入 Delivered／PickedUp 時 COD 同交易收款（付款事件、通知、模擬發票 Outbox）並把訂單推進 Completed；同鍵同 payload 不重複副作用，重播回傳目前最新的 `AdminOrderDto`（不保存第一次的快照） | `validation_failed`、`resource_not_found`、`shipping_status_transition_invalid`、`payment_state_conflict`、`concurrency_conflict`、`idempotency_payload_conflict`、`idempotency_request_in_progress` |
| UC-RETURN-01 寄回物流 | `GET /api/v1/admin/returns/{id}/shipment`；`POST /api/v1/admin/returns/{id}/shipment`；`POST /api/v1/admin/returns/{id}/shipment/events` | OrderManager／SuperAdmin；事件端點限模擬 Provider／內部工作 | `CreateReturnShipmentRequest`、`ReturnShipmentDto`、`AppendReturnShipmentEventRequest`；每案最多一個有效寄回批次 | `return_state_conflict`、`concurrency_conflict`、`authorization_forbidden` |
| UC-REFUND-01 退貨審核 | `POST /api/v1/admin/returns/{id}/actions/review` | `Return.Approve`：OrderManager／SuperAdmin | `ApproveReturnRequest` → `ReturnRequestDto` | `return_state_conflict`、`concurrency_conflict` |
| UC-REFUND-01 退款 | `GET /api/v1/admin/refunds`；`GET /api/v1/admin/refunds/{id}`；`POST /api/v1/admin/refunds/{id}/actions/execute` | FinanceManager／SuperAdmin；查詢依角色矩陣 | `AdminRefundQuery`、`PageResult<RefundDto>`、`ExecuteRefundRequest` | `refund_amount_exceeded`、`refund_state_conflict`、`refund_snapshot_unavailable`、`concurrency_conflict` |
| M 優惠券管理支撐 | `GET/POST /api/v1/admin/coupons`；`GET/PUT /api/v1/admin/coupons/{id}`；`POST /api/v1/admin/coupons/{id}/actions/{action}` | `Coupon.Manage`：FinanceManager／MarketingAnalyst／SuperAdmin | Action 白名單：`activate`、`pause`、`disable`；`CouponDto` 與管理 Request | `coupon_code_duplicate`、`coupon_state_conflict`、`validation_failed`、`concurrency_conflict` |
| M 模擬發票查詢支撐 | `GET /api/v1/admin/invoices`；`GET /api/v1/admin/invoices/{id}` | `Invoice.Manage`：FinanceManager／SuperAdmin | `AdminInvoiceQuery` → `PageResult<AdminInvoiceSummaryDto>`；`AdminInvoiceDto` | `resource_not_found`、`authorization_forbidden` |
| M 手動開立訂單快照 | `GET /api/v1/admin/orders/{orderId}/invoice-issuance` | `Invoice.Manage`：FinanceManager／SuperAdmin；需 MFA | `InvoiceIssuanceOrderDto`；只回開票必要事實，不回收件人、品項、內部 ID | `resource_not_found`、`authorization_forbidden` |
| M 模擬發票開立 | `POST /api/v1/admin/orders/{orderId}/invoices` | `Invoice.Manage`：FinanceManager／SuperAdmin | `IssueSimulatedInvoiceRequest` → `201 AdminInvoiceDto`；需 Idempotency-Key | `invoice_order_unpaid`、`invoice_order_cancelled`、`invoice_already_exists`、`idempotency_payload_conflict`、`concurrency_conflict` |
| M 模擬發票作廢 | `POST /api/v1/admin/invoices/{id}/actions/void` | `Invoice.Manage`：FinanceManager／SuperAdmin | `VoidSimulatedInvoiceRequest` → `AdminInvoiceDto` | `invoice_state_conflict`、`invoice_allowance_required`、`concurrency_conflict` |
| M 模擬折讓建立 | `POST /api/v1/admin/invoices/{id}/allowances` | `Invoice.Manage`：FinanceManager／SuperAdmin | `CreateSimulatedInvoiceAllowanceRequest` → `201 SimulatedInvoiceAllowanceDto`；需 Idempotency-Key；金額由成功 Refund 推導 | `invoice_state_conflict`、`refund_state_conflict`、`idempotency_payload_conflict`、`concurrency_conflict` |

`Invoice.Manage` 與 `Coupon.Manage` 均沿用管理員 Policy 基線，要求管理員身分與 TOTP／MFA。兩者只定義授權，不取代狀態機、冪等、RowVersion、金額／名額規則或 Audit；前台 `POST/DELETE /api/v1/cart/coupon` 不套用 `Coupon.Manage`。

## AI 客服、人工客服與案件工作台

| 範圍／使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AI-SUPPORT-01 | `GET /api/v1/ai/consents/current`；`POST /api/v1/ai/consents`；`DELETE /api/v1/ai/consents/current` | Member | `AiConsentStatusDto`；同意目前版本／撤回 | `validation_failed`、`ai_service_unavailable` |
| UC-AI-SUPPORT-02／03 | `POST /api/v1/ai/support/messages` | Member＋有效同意 | `AiSupportMessageRequest` → `AiSupportAnswerDto`；本人 Order／SupportTicket 公開訊息只讀投影，不執行商業寫入；功能旗標關閉時 Fail Closed | `validation_failed`、`ai_consent_required`、`ai_usage_limit_exceeded`、`ai_budget_protection_active`、`ai_order_access_denied`、`ai_output_invalid`、`ai_service_unavailable` |
| UC-AI-SUPPORT-04 | `GET /api/v1/ai/usage/me`；`GET /api/v1/admin/ai/usage` | Member；A-28：FinanceManager／CustomerServiceSupervisor／MarketingAnalyst／SuperAdmin；成本金額只限 FinanceManager／SuperAdmin | `AiUsageDto`、`AdminAiUsageReportDto`；管理端日期區間預設 30 天、上限 90 天 | `validation_failed`、`ai_budget_protection_active`、`authorization_forbidden`、`ai_service_unavailable` |
| UC-SUPPORT-01 顧客端 | `GET /api/v1/support-tickets`；`POST /api/v1/support-tickets`；`GET /api/v1/support-tickets/{id}`；`POST /api/v1/support-tickets/{id}/messages`；`POST /api/v1/support-tickets/{id}/actions/cancel` | Member Owner | `SupportTicketQuery`、`PageResult<SupportTicketSummaryDto>`、Ticket／Message／Cancel DTO | `support_ticket_state_conflict`、`support_ticket_cancel_not_allowed`、`support_ticket_number_generation_failed`、`resource_not_found` |
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
