---
文件狀態: 已確認
最後更新: 2026-08-13
追蹤項目:
  - DES-10
  - REQ-02
  - REQ-03
---

# API Endpoint 目錄

本目錄把 37 個 M 使用案例回連至第一版 API 路由。Method、HTTP Status、分頁、Problem Details、冪等與 RowVersion 遵守 [[03-架構/API共通規範]]；錯誤碼遵守 [[03-架構/API錯誤碼目錄]]；DTO 欄位與上限見 [[03-架構/API DTO與Schema契約]]。

所有 Route 參數 `{id}`、`{orderId}`、`{productId}`、`{attemptId}` 等皆代表資源 `PublicId`，不得接受資料庫 `bigint Id`。產生、索引、授權與錯誤隱匿規則見 [[03-架構/PublicId與資料完整性設計]]。

## 公開商品、搜尋與組裝

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-SEARCH-01 | `GET /api/v1/products` | Public | `ProductSearchQuery` → `PageResult<ProductCardDto>` | `validation_failed`、`search_sort_unsupported`、`search_filter_unsupported` |
| UC-AI-SEARCH-01 | `POST /api/v1/ai/product-search/recommendations` | Public＋額度 | `AiProductSearchRequest` → `AiProductSearchResultDto`；內含 Intent 或補問，不公開 Parse Endpoint | `ai_usage_limit_exceeded`、`ai_output_invalid` |
| UC-AI-SEARCH-02 | 同上 | Public＋額度 | 後端完成意圖驗證、SQL 候選與相容性後回傳推薦；無候選是 200 的 Result Type | `build_incompatible` |
| UC-AI-SEARCH-03 | 同上 | Public＋額度 | 回應包含 `resultType` 與 `degradationMode` | `ai_service_unavailable` |
| UC-BUILD-01 | `POST /api/v1/build-lists`；`GET /api/v1/build-lists/{id}`；`POST /api/v1/build-lists/{id}/share`；`POST /api/v1/build-lists/{id}/cart` | 建立／保存為 Member；公開分享 Token 可讀 | `CreateBuildListRequest`、`BuildListDto`、`BuildShareDto` | `validation_failed`、`build_incomplete`、`build_unavailable_item`、`inventory_insufficient` |
| UC-COMPAT-01 | `POST /api/v1/compatibility-checks` | Public | `CompatibilityCheckRequest` → `CompatibilityCheckDto` | `validation_failed`、`build_incompatible` |

> [!note]
> 對前端只公開單一 Recommendations Endpoint；Application 內仍分成解析、驗證、補問、候選查詢、相容性與理由產生階段。一次 HTTP Request 不表示固定只呼叫一次 OpenAI。

## 會員與驗證

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AUTH-01 | `POST /api/v1/auth/register`；`POST /api/v1/auth/email-verifications`；`POST /api/v1/auth/email-verifications/confirm` | Public | 註冊／驗證 Request → `202` 或會員摘要 | `account_email_in_use`、`email_token_invalid`、`email_token_expired` |
| UC-AUTH-02 | `POST /api/v1/auth/login`；`POST /api/v1/auth/logout` | Public／Member | Cookie Session | `invalid_credentials`、`account_locked`、`account_suspended` |
| UC-AUTH-03 | `POST /api/v1/auth/password-resets`；`POST /api/v1/auth/password-resets/confirm` | Public | Request 不洩漏帳號是否存在 | `password_reset_token_invalid`、`password_reset_token_expired` |
| UC-ADMIN-AUTH-01 | `POST /api/v1/admin/auth/login`；`POST /api/v1/admin/auth/totp/verify`；`POST /api/v1/admin/auth/recovery-codes/use` | Admin | 兩階段管理員 Cookie | `admin_two_factor_required`、`admin_two_factor_invalid`、`admin_recovery_code_invalid` |
| UC-GUEST-ORDER-01 | `POST /api/v1/guest-orders/access-requests`；`POST /api/v1/guest-orders/access-verifications` | Public | Email＋訂單編號／Token → 短效存取 | `guest_order_verification_invalid`、`guest_order_access_expired`、`guest_order_scope_mismatch` |

## 購物車、訂單、付款、退貨與退款

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-CART-01 | `GET /api/v1/cart`；`POST /api/v1/cart/revalidate` | Public Cart／Member | `CartDto`、`CartValidationDto` | `cart_item_requires_attention`、`sku_unavailable` |
| UC-CART-02 | `POST /api/v1/cart/merge` | Member | 冪等命令 → `CartMergeResultDto` | `cart_merge_conflict` |
| UC-CHECKOUT-01 | `POST /api/v1/orders` | Public／Member | `CreateOrderRequest` → `201 OrderDto`；需 Idempotency-Key | `inventory_insufficient`、`order_total_changed` |
| UC-CHECKOUT-COD-01 | 同 `POST /api/v1/orders` | Public／Member | `paymentMethod = cashOnDelivery` | `payment_method_not_allowed`、`payment_cod_amount_exceeded`、`payment_cod_restricted_item`、`shipping_method_not_allowed` |
| UC-PAY-01 | `POST /api/v1/orders/{orderId}/payment-attempts`；`POST /api/v1/simulated-payments/{attemptId}/complete` | 訂單擁有者／展示模擬權限 | `CreatePaymentAttemptRequest` → `PaymentAttemptDto` | `payment_state_conflict`、`payment_attempt_expired`、`order_payment_deadline_expired` |
| UC-COUPON-01 | `POST /api/v1/cart/coupon`；`DELETE /api/v1/cart/coupon` | Public Cart／Member | `ApplyCouponRequest` → 更新後 `CartDto` | `coupon_not_applicable`、`coupon_usage_exhausted`、`coupon_not_active` |
| UC-RETURN-01 | `POST /api/v1/orders/{orderId}/returns`；`GET /api/v1/returns/{id}` | 訂單擁有者／GuestOrderAccessToken | `CreateReturnRequest` → `ReturnRequestDto` | `return_deadline_expired`、`return_quantity_exceeded` |
| UC-REFUND-01 | `POST /api/v1/admin/returns/{id}/approvals`；`POST /api/v1/admin/refunds/{id}/execute` | `Return.Approve`：OrderManager／SuperAdmin；`Refund.Execute`：FinanceManager／SuperAdmin | `ApproveReturnRequest`、`ExecuteRefundRequest` → `ReturnRequestDto`／`RefundDto` | `return_state_conflict`、`refund_amount_exceeded`、`refund_state_conflict` |

## 管理後台商品、庫存、訂單與物流

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-ADM-PROD-01 | `POST /api/v1/admin/products/{productId}/skus` | CatalogManager／SuperAdmin | `CreateSkuRequest` → `201 SkuDto` | `sku_code_duplicate`、`specification_invalid` |
| UC-ADM-PROD-02 | `PUT /api/v1/admin/skus/{id}` | CatalogManager／SuperAdmin | `UpdateSkuRequest`＋RowVersion → `SkuDto` | `concurrency_conflict`、`sale_price_period_overlap` |
| UC-IMPORT-01 | `POST /api/v1/admin/product-imports/preview`；`POST /api/v1/admin/product-imports/{id}/confirm` | CatalogManager／SuperAdmin | Multipart → `ProductImportPreviewDto` | `import_format_unsupported`、`import_validation_failed`、`import_preview_expired` |
| UC-ADM-INV-01 | `GET /api/v1/admin/inventory/reservations`；`POST /api/v1/admin/inventory/reservations/{id}/release`；`POST /api/v1/admin/inventory-imports/preview`；`POST /api/v1/admin/inventory-imports/{id}/confirm` | InventoryManager／SuperAdmin | `CursorPage<InventoryReservationDto>`；釋放理由＋RowVersion；庫存匯入全批原子提交 | `inventory_reservation_not_active`、`inventory_import_validation_failed`、`concurrency_conflict` |
| UC-ADM-SHIP-01 | `GET /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST ...`；`POST .../{versionId}/publish` | OrderManager／SuperAdmin | Draft／Publish DTO＋RowVersion | `validation_failed`、`package_limit_period_overlap`、`concurrency_conflict` |
| UC-ADM-STORE-01 | `GET/POST /api/v1/admin/convenience-stores`；`PUT /api/v1/admin/convenience-stores/{id}` | OrderManager／SuperAdmin；CatalogManager 只讀 | `PageResult<ConvenienceStoreDto>`／Store DTO＋RowVersion | `store_code_duplicate`、`concurrency_conflict` |
| UC-ADM-ORDER-01 | `GET /api/v1/admin/orders`；`GET /api/v1/admin/orders/{id}`；`POST /api/v1/admin/orders/{id}/actions/{action}` | OrderManager／相關敏感 Policy | `CursorPage<AdminOrderSummaryDto>`、合法動作命令 | `order_state_conflict`、`concurrency_conflict` |
| UC-ADM-ORDER-02 | `GET /api/v1/admin/orders/{id}/recipient` | OrderManager／PrivacyAdmin／SuperAdmin，依用途 | `OrderRecipientDto` | `resource_not_found`、`authorization_forbidden` |
| UC-ADM-SHIP-02 | `POST /api/v1/admin/shipments/batches`；`GET /api/v1/admin/shipments/batches/{id}/result.csv` | OrderManager／SuperAdmin | 最多 100 筆 → `BatchShipmentResultDto` | `shipping_batch_limit_exceeded`；逐筆 `shipping_order_not_ready`、`shipping_tracking_duplicate` |

## AI 客服、一般客服與工作台

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AI-SUPPORT-01 | `POST /api/v1/ai/consents`；`DELETE /api/v1/ai/consents/current` | Member | 同意版本／撤回 | `ai_consent_required` |
| UC-AI-SUPPORT-02 | `POST /api/v1/ai/support/messages` | Member＋有效同意 | `AiSupportMessageRequest` → `AiSupportAnswerDto` | `ai_order_access_denied`、`ai_tool_not_allowed` |
| UC-AI-SUPPORT-03 | 同上 | Member＋有效同意 | 回應可導向正式頁面但不執行寫入 | `ai_tool_not_allowed` |
| UC-AI-SUPPORT-04 | `GET /api/v1/ai/usage/me`；`GET /api/v1/admin/ai/usage` | Member；彙總為 MarketingAnalyst／CustomerServiceSupervisor／SuperAdmin，成本明細為 FinanceManager／SuperAdmin | `AiUsageDto`、`AdminAiUsageReportDto` | `ai_usage_limit_exceeded`、`ai_budget_protection_active`、`authorization_forbidden` |
| UC-SUPPORT-01 | `POST /api/v1/support-tickets`；`GET /api/v1/support-tickets/{id}`；`POST .../messages`；`POST .../cancel` | Member／指派客服 | Ticket／Message／Command DTO | `support_ticket_state_conflict`、`support_ticket_cancel_not_allowed` |
| UC-SUPPORT-02 | `POST /api/v1/support-tickets/{id}/attachments`；`GET /api/v1/private-attachments/{id}/content` | 案件擁有者／授權客服 | Multipart／授權串流 | `file_count_exceeded`、`file_size_exceeded`、`file_format_invalid`、`file_malware_detected`、`file_scan_unavailable` |
| UC-SLA-01 | `GET /api/v1/admin/support-tickets/sla` | CustomerService／Supervisor | `CursorPage<SupportSlaItemDto>` | `authorization_forbidden` |
| UC-WORKBENCH-01 | `GET /api/v1/admin/case-workbench` | 各角色只見可授權領域 | `CaseWorkbenchQuery` → `CursorPage<CaseWorkbenchItemDto>` | `search_sort_unsupported` |

工作台固定使用 `LastActivityAtUtc／CasePublicId` Cursor，12 個共通欄位、UNION 分支授權與驗收見 [[03-架構/統一案件工作台設計]]。

## 報表

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-REPORT-01 | `GET /api/v1/admin/reports/{reportKey}`；`GET /api/v1/admin/reports/{reportKey}/export` | 一般：MarketingAnalyst／FinanceManager／SuperAdmin；財務成本：FinanceManager／SuperAdmin；客服 SLA：CustomerServiceSupervisor／SuperAdmin | `ReportQuery` → 摘要＋`CursorPage<ReportRowDto>`／匯出檔 | `report_key_invalid`、`report_range_invalid`、`authorization_forbidden` |

## 分頁契約索引

- 一般列表固定使用 `pageNumber/pageSize` 與 `PageResult<T>`；商品列表、門市、分類、品牌、SKU 及版本清單不得自行改用 Cursor。
- 只有下列快速變動或大筆逐列資料使用 `cursor/pageSize` 與 `CursorPage<T>`：庫存保留、後台訂單、客服 SLA 佇列、統一案件工作台、匯入預覽列、報表明細列。
- 匯入預覽列的 Route 是 `GET /api/v1/admin/product-imports/{id}/rows`；其餘 Cursor Route 已在上表標明。新增 Cursor 例外必須同時更新 [[03-架構/API共通規範]] 與本索引。

## 待實作

- DTO、Enum、長度、範例及連結 Schema 已收斂至 [[03-架構/API DTO與Schema契約]]。
- 示範門市、報表、AI 用量、退貨、退款、Audit 與個資的精確 Policy 已定於 [[01-需求/角色與權限]]。
- 報表營收口徑及 AI 既有零件 Schema 已定於正式領域／AI 文件。
- Rate Limit Header、Problem Details 與錯誤碼行為已在共通規範定義；仍需在實際 Endpoint 套用契約測試。
- OpenAPI TypeScript Client 流程已定；實際產生檔與 CI 契約差異檢查須等 Solution 建立。
