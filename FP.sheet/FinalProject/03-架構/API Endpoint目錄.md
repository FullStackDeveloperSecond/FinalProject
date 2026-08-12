---
文件狀態: 草案
最後更新: 2026-08-12
追蹤項目:
  - DES-10
  - REQ-02
  - REQ-03
---

# API Endpoint 目錄

本目錄把 37 個 M 使用案例回連至第一版 API 路由。Method、HTTP Status、分頁、Problem Details、冪等與 RowVersion 遵守 [[03-架構/API共通規範]]；錯誤碼遵守 [[03-架構/API錯誤碼目錄]]。DTO 名稱是契約占位，完整欄位尚未定版。

## 公開商品、搜尋與組裝

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-SEARCH-01 | `GET /api/v1/products` | Public | `ProductSearchQuery` → `PagedResult<ProductCardDto>` | `request_validation_failed`、`sort_field_invalid` |
| UC-AI-SEARCH-01 | `POST /api/v1/ai/product-search/parse` | Public＋額度 | `AiProductSearchRequest` → `SearchIntentResultDto` | `ai_quota_exceeded`、`ai_schema_invalid` |
| UC-AI-SEARCH-02 | `POST /api/v1/ai/product-search/recommendations` | Public＋額度 | 已驗證意圖 → `AiRecommendationDto` | `ai_no_valid_candidate`、`compatibility_failed` |
| UC-AI-SEARCH-03 | 同上 | Public＋額度 | 回應包含 `degradationMode` | `ai_provider_unavailable`、`ai_timeout` |
| UC-BUILD-01 | `POST /api/v1/build-lists`；`GET /api/v1/build-lists/{id}`；`POST /api/v1/build-lists/{id}/share`；`POST /api/v1/build-lists/{id}/cart` | 建立／保存為 Member；公開分享 Token 可讀 | `CreateBuildListRequest`、`BuildListDto`、`BuildShareDto` | `build_invalid`、`inventory_insufficient` |
| UC-COMPAT-01 | `POST /api/v1/compatibility-checks` | Public | `CompatibilityCheckRequest` → `CompatibilityCheckDto` | `compatibility_input_invalid` |

> [!note]
> AI 搜尋解析與推薦是否合併為單一公開 Endpoint 尚待完整 SearchIntent 契約與前端互動流程確認；上表先分列責任，不表示必須產生兩次 OpenAI 呼叫。

## 會員與驗證

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AUTH-01 | `POST /api/v1/auth/register`；`POST /api/v1/auth/email-verifications`；`POST /api/v1/auth/email-verifications/confirm` | Public | 註冊／驗證 Request → `202` 或會員摘要 | `auth_email_in_use`、`auth_token_invalid` |
| UC-AUTH-02 | `POST /api/v1/auth/login`；`POST /api/v1/auth/logout` | Public／Member | Cookie Session | `auth_invalid_credentials`、`auth_account_locked` |
| UC-AUTH-03 | `POST /api/v1/auth/password-resets`；`POST /api/v1/auth/password-resets/confirm` | Public | Request 不洩漏帳號是否存在 | `auth_token_invalid`、`auth_token_expired` |
| UC-ADMIN-AUTH-01 | `POST /api/v1/admin/auth/login`；`POST /api/v1/admin/auth/totp/verify`；`POST /api/v1/admin/auth/recovery-codes/use` | Admin | 兩階段管理員 Cookie | `admin_2fa_required`、`admin_totp_invalid` |
| UC-GUEST-ORDER-01 | `POST /api/v1/guest-orders/access-requests`；`POST /api/v1/guest-orders/access-verifications` | Public | Email＋訂單編號／Token → 短效存取 | `guest_order_verification_failed`、`guest_order_access_expired` |

## 購物車、訂單、付款、退貨與退款

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-CART-01 | `GET /api/v1/cart`；`POST /api/v1/cart/revalidate` | Public Cart／Member | `CartDto`、`CartValidationDto` | `cart_item_unavailable`、`cart_price_changed` |
| UC-CART-02 | `POST /api/v1/cart/merge` | Member | 冪等命令 → `CartMergeResultDto` | `cart_merge_conflict` |
| UC-CHECKOUT-01 | `POST /api/v1/orders` | Public／Member | `CreateOrderRequest` → `201 OrderDto`；需 Idempotency-Key | `inventory_insufficient`、`order_total_changed` |
| UC-CHECKOUT-COD-01 | 同 `POST /api/v1/orders` | Public／Member | `paymentMethod = cashOnDelivery` | `cod_not_allowed`、`shipping_method_not_allowed` |
| UC-PAY-01 | `POST /api/v1/orders/{orderId}/payment-attempts`；`POST /api/v1/simulated-payments/{attemptId}/complete` | 訂單擁有者／展示模擬權限 | `PaymentAttemptDto` | `payment_state_conflict`、`payment_attempt_expired` |
| UC-COUPON-01 | `POST /api/v1/cart/coupon`；`DELETE /api/v1/cart/coupon` | Public Cart／Member | `ApplyCouponRequest` → 更新後 `CartDto` | `coupon_not_eligible`、`coupon_usage_exceeded` |
| UC-RETURN-01 | `POST /api/v1/orders/{orderId}/returns`；`GET /api/v1/returns/{id}` | 訂單擁有者／GuestOrderAccessToken | `CreateReturnRequest` → `ReturnRequestDto` | `return_window_expired`、`return_quantity_invalid` |
| UC-REFUND-01 | `POST /api/v1/admin/returns/{id}/refund-approvals`；`POST /api/v1/admin/refunds/{id}/execute` | FinanceManager／SuperAdmin；退貨流程角色依矩陣 | `ApproveRefundRequest`、`RefundDto` | `refund_amount_invalid`、`refund_state_conflict` |

## 管理後台商品、庫存、訂單與物流

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-ADM-PROD-01 | `POST /api/v1/admin/products/{productId}/skus` | CatalogManager／SuperAdmin | `CreateSkuRequest` → `201 SkuDto` | `sku_code_duplicate`、`specification_invalid` |
| UC-ADM-PROD-02 | `PUT /api/v1/admin/skus/{id}` | CatalogManager／SuperAdmin | `UpdateSkuRequest`＋RowVersion → `SkuDto` | `concurrency_conflict`、`sale_price_overlap` |
| UC-IMPORT-01 | `POST /api/v1/admin/product-imports/previews`；`POST /api/v1/admin/product-imports/{id}/commit` | CatalogManager／SuperAdmin | Multipart → `ProductImportPreviewDto` | `import_template_unsupported`、`import_has_errors` |
| UC-ADM-INV-01 | `GET /api/v1/admin/inventory/reservations`；`POST /api/v1/admin/inventory/reservations/{id}/release` | InventoryManager／SuperAdmin | 分頁查詢；釋放理由＋RowVersion | `inventory_reservation_not_active`、`concurrency_conflict` |
| UC-ADM-SHIP-01 | `GET /api/v1/admin/shipping-providers/{id}/package-limit-versions`；`POST ...`；`POST .../{versionId}/publish` | OrderManager／SuperAdmin | Draft／Publish DTO＋RowVersion | `package_limit_invalid`、`effective_period_overlap` |
| UC-ADM-STORE-01 | `GET/POST /api/v1/admin/convenience-stores`；`PUT /api/v1/admin/convenience-stores/{id}` | 精確角色待權限矩陣；SuperAdmin | Store DTO＋RowVersion | `store_code_duplicate`、`concurrency_conflict` |
| UC-ADM-ORDER-01 | `GET /api/v1/admin/orders`；`GET /api/v1/admin/orders/{id}`；`POST /api/v1/admin/orders/{id}/actions/{action}` | OrderManager／相關敏感 Policy | `AdminOrderSummaryDto`、合法動作命令 | `order_action_not_allowed`、`order_state_conflict` |
| UC-ADM-ORDER-02 | `GET /api/v1/admin/orders/{id}/recipient` | OrderManager／PrivacyAdmin／SuperAdmin，依用途 | `OrderRecipientDto` | `personal_data_access_denied` |
| UC-ADM-SHIP-02 | `POST /api/v1/admin/shipments/batches`；`GET /api/v1/admin/shipments/batches/{id}/result.csv` | OrderManager／SuperAdmin | 最多 100 筆 → `BatchShipmentResultDto` | 逐筆 `shipment_*` 錯誤 |

## AI 客服、一般客服與工作台

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-AI-SUPPORT-01 | `POST /api/v1/ai/consents`；`DELETE /api/v1/ai/consents/current` | Member | 同意版本／撤回 | `ai_consent_required` |
| UC-AI-SUPPORT-02 | `POST /api/v1/ai/support/messages` | Member＋有效同意 | `AiSupportMessageRequest` → `AiSupportAnswerDto` | `order_access_denied`、`ai_tool_forbidden` |
| UC-AI-SUPPORT-03 | 同上 | Member＋有效同意 | 回應可導向正式頁面但不執行寫入 | `ai_write_action_forbidden` |
| UC-AI-SUPPORT-04 | `GET /api/v1/ai/usage/me`；後台用量查詢路由待角色矩陣 | Member／授權管理員 | `AiUsageDto` | `ai_quota_exceeded`、`ai_budget_protection_active` |
| UC-SUPPORT-01 | `POST /api/v1/support-tickets`；`GET /api/v1/support-tickets/{id}`；`POST .../messages`；`POST .../cancel` | Member／指派客服 | Ticket／Message／Command DTO | `support_transition_invalid`、`support_cancel_not_allowed` |
| UC-SUPPORT-02 | `POST /api/v1/support-tickets/{id}/attachments`；`GET /api/v1/support-attachments/{id}/content` | 案件擁有者／授權客服 | Multipart／授權串流 | `attachment_type_invalid`、`attachment_scan_failed` |
| UC-SLA-01 | `GET /api/v1/admin/support-tickets/sla` | CustomerService／Supervisor | SLA 分頁投影 | `support_access_denied` |
| UC-WORKBENCH-01 | `GET /api/v1/admin/case-workbench` | 各角色只見可授權領域 | `CaseWorkbenchQuery` → `PagedResult<CaseWorkbenchItemDto>` | `sort_field_invalid` |

## 報表

| 使用案例 | Method／Route | 權限 | Request／Response 契約 | 主要錯誤 |
|---|---|---|---|---|
| UC-REPORT-01 | `GET /api/v1/admin/reports/{reportKey}`；`GET /api/v1/admin/reports/{reportKey}/export` | MarketingAnalyst、FinanceManager 或其他精確 Policy 待權限矩陣 | `ReportQuery` → 報表專用 DTO／匯出檔 | `report_key_invalid`、`report_range_invalid` |

## 仍未完成

- 所有 DTO 的完整欄位、Enum、長度、範例與 OpenAPI Schema。
- AI 搜尋單一 Endpoint 或解析／推薦分離方式。
- 示範門市維護、報表及 AI 用量後台的精確角色 Policy。
- 退貨審核角色與 FinanceManager 執行退款之間的命令邊界。
- 每個 Endpoint 的完整錯誤碼集合與 Rate Limit Header。
- OpenAPI TypeScript Client 工具、產生命令與 CI 契約差異檢查。
