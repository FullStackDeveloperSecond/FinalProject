---
文件狀態: 已確認
最後更新: 2026-08-20
追蹤項目:
  - DES-11
  - DES-22
  - DES-23
---

# API 錯誤碼目錄

本目錄定義第一版 M 功能可由前端處理的穩定業務錯誤碼。錯誤回應格式、HTTP Status 與安全訊息遵守 [[03-架構/02-API與前端契約/API共通規範]]。

## 使用規則

- `code` 使用小寫 snake_case 的「領域＋原因」。
- 同一錯誤在不同 Endpoint 使用相同 code 與 HTTP Status。
- 已發布 code 不得改變意義或移作他用；需要更精確語意時新增 code。
- 前端以 code 選擇操作及語系文字，不解析中文 `detail`。
- `401`、`403`、`404` 必須依安全邊界避免洩漏帳號、訂單、附件或其他會員資源是否存在。
- 欄位格式與一般輸入錯誤使用 `validation_failed` 並在 `errors` 提供欄位；明確業務原因使用下列 code。
- Endpoint 目錄不得使用 `shipment_*` 等萬用碼；每個可能回傳的錯誤都必須以完整 code 登錄。
- 檔案上傳只使用本目錄的 `file_*` 系列；舊草稿中的 `attachment_*`、`file_too_large`、`file_type_not_allowed`、`file_signature_mismatch`、`file_scan_rejected` 不得實作。

## 共通與驗證

| Code | HTTP | 使用時機 | 前端／安全行為 |
|---|---:|---|---|
| `validation_failed` | 400 | 欄位、格式、範圍、分頁或必填驗證失敗 | 顯示欄位 errors |
| `antiforgery_validation_failed` | 400 | Cookie 認證的非安全方法缺少、失效或身分不相符的 Anti-forgery Token | 清除記憶體 Token、重新取得一次；不得保存、顯示或記錄 Token |
| `authentication_required` | 401 | 尚未登入或 Session 已失效 | 導向正確登入入口 |
| `authorization_forbidden` | 403 | 已登入但角色、Policy 或資源範圍不足 | 不顯示內部權限細節 |
| `resource_not_found` | 404 | 資源不存在或依安全策略不可揭露 | 不區分不存在與無權限 |
| `concurrency_conflict` | 409 | `rowversion` 或版本已被其他人更新 | 重新載入後再操作 |
| `idempotency_payload_conflict` | 409 | 同一 Idempotency-Key 搭配不同 Payload | 不自動重試或換 Key 重送同操作 |
| `idempotency_request_in_progress` | 409 | 同 Scope／Operation／Key 的相同請求仍在處理 | 依 `Retry-After: 3` 等待後，以相同 Key 與 Payload 重試 |
| `rate_limit_exceeded` | 429 | 登入、驗證、AI 或其他用途超過限制 | 顯示可安全揭露的重試時間 |
| `request_method_not_allowed` | 405 | Route 存在但 HTTP Method 不支援 | 修正 Method，不自動重送寫入操作 |
| `request_content_type_unsupported` | 415 | Request Content-Type 不受端點支援 | 使用 OpenAPI 宣告的媒體類型重送 |
| `request_conflict` | 409 | 回應沒有更精確領域 code 的一般衝突保底；商業端點不得用它取代具名衝突 | 重新取得最新狀態；開發端應補上精確領域 code |
| `service_unavailable` | 503 | API 必要依賴或基礎能力暫時不可用，且沒有更精確領域 code | 顯示稍後重試；不得洩漏依賴名稱或拓樸 |
| `unexpected_error` | 500 | 未處理例外或無法安全分類的伺服器錯誤 | 顯示通用錯誤並以 traceId／correlationId 協助查詢 |

## 會員與管理員驗證

| Code | HTTP | 使用時機 |
|---|---:|---|
| `account_email_unverified` | 403 | 會員尚未完成 Email 驗證 |
| `account_email_in_use` | 409 | 註冊 Email 已存在；只用於註冊提交，不用於忘記密碼或驗證碼申請 |
| `account_suspended` | 403 | 帳號已停權且 Session／登入被拒絕 |
| `account_locked` | 423 | 登入失敗達鎖定門檻 |
| `invalid_credentials` | 401 | Email 或密碼錯誤；不得指出是哪一欄錯誤 |
| `email_token_invalid` | 400 | Email 驗證 Token 無效、已使用或撤銷 |
| `email_token_expired` | 400 | Email 驗證 Token 已過期 |
| `password_reset_token_invalid` | 400 | 重設 Token 無效、已使用或撤銷 |
| `password_reset_token_expired` | 400 | 重設 Token 已過期 |
| `admin_two_factor_required` | 403 | 管理員密碼正確但尚未完成 TOTP |
| `admin_two_factor_invalid` | 400 | TOTP 不正確或不在允許時間窗 |
| `admin_recovery_code_invalid` | 400 | Recovery Code 無效或已使用 |
| `guest_order_verification_invalid` | 400 | 訪客訂單驗證碼無效；訊息不得揭露訂單存在性 |
| `guest_order_access_expired` | 401 | 限單存取權杖已到期 |
| `guest_order_scope_mismatch` | 404 | 權杖嘗試存取另一張訂單 |

## 商品、SKU、搜尋與匯入

| Code | HTTP | 使用時機 |
|---|---:|---|
| `product_unavailable` | 409 | 商品下架、停用或不接受新交易 |
| `product_code_duplicate` | 409 | 新增商品的 Product Code 已存在 |
| `brand_code_duplicate` | 409 | 品牌 Code 已存在 |
| `category_code_duplicate` | 409 | 分類 Code 已存在 |
| `tag_code_duplicate` | 409 | 標籤 Code 已存在 |
| `sku_unavailable` | 409 | SKU 停用、下架或不能加入購物車 |
| `sku_code_duplicate` | 409 | 新增 SKU 的 Code 已存在 |
| `sku_code_immutable` | 409 | 嘗試修改已建立的 SKU Code |
| `sku_delete_referenced` | 409 | SKU 已被訂單、庫存或其他資料引用，不能實體刪除 |
| `search_sort_unsupported` | 400 | 排序欄位或方向不在 Endpoint 白名單 |
| `search_filter_unsupported` | 400 | 篩選欄位、規格或運算不在白名單 |
| `sale_price_period_overlap` | 409 | 同一 SKU 的有效特價期間重疊 |
| `specification_invalid` | 400 | 規格語意鍵、型別、單位或值不符合分類規格定義 |
| `specification_semantic_key_duplicate` | 409 | 同一範圍的規格語意鍵已存在 |
| `specification_definition_referenced` | 409 | 規格定義已被商品、搜尋、匯入或相容性規則引用，只能停用 |
| `compatibility_threshold_out_of_range` | 400 | 相容性警告門檻不在程式允許的安全範圍 |
| `import_format_unsupported` | 400 | 不是支援的 XLSX／CSV 格式或版本 |
| `import_dataset_missing` | 400 | Product、SKU 或 Specification 必要資料集缺少 |
| `import_lookup_not_found` | 400 | 分類、品牌或規格語意鍵不存在 |
| `import_sku_code_duplicate` | 400 | 同一批次重複 SKU Code |
| `import_sku_update_not_found` | 400 | 指定更新的 SKU Code 不存在 |
| `import_validation_failed` | 400 | 匯入預覽含一個以上逐欄錯誤，禁止提交 |
| `import_preview_expired` | 409 | 提交所引用的預覽已失效或來源檔改變 |
| `import_already_committed` | 409 | 匯入批次已成功提交，不得再次提交 |
| `import_batch_expired` | 410 | 匯入批次已超過 24 小時保存期限 |

## 購物車、組裝與相容性

| Code | HTTP | 使用時機 |
|---|---:|---|
| `cart_item_requires_attention` | 409 | 價格、庫存、上下架或相容性衝突尚未處理 |
| `cart_quantity_exceeded` | 409 | 合併或修改後超過購買上限 |
| `cart_item_limit_exceeded` | 409 | 購物車已達 100 品項上限，無法再新增；合併訪客購物車時若會超過上限則整次合併被拒絕，訪客購物車維持 Active |
| `cart_merge_conflict` | 409 | 購物車合併存在需使用者決定的項目 |
| `build_incomplete` | 400 | 組裝清單缺少必要零件 |
| `build_incompatible` | 409 | 確定性規則判定零件不相容 |
| `build_unavailable_item` | 409 | 組裝群組內必要 SKU 缺貨、下架或停用 |
| `assembly_cancellation_restricted` | 409 | 組裝已開始，不允許顧客無理由取消 |

## 訂單、優惠券與庫存

| Code | HTTP | 使用時機 |
|---|---:|---|
| `inventory_insufficient` | 409 | 任一 SKU 可售庫存不足，訂單整筆回滾 |
| `inventory_reservation_not_active` | 409 | 嘗試消耗或釋放非 Active 保留 |
| `inventory_reservation_already_processed` | 409 | 保留已消耗、釋放或逾時 |
| `inventory_import_validation_failed` | 400 | 庫存調整預覽含錯誤，整批不得提交 |
| `order_state_conflict` | 409 | 目前訂單狀態不允許所要求操作 |
| `order_total_changed` | 409 | 結帳重算後總額與使用者確認快照不同，需重新確認 |
| `order_cancellation_not_allowed` | 409 | 訂單已出貨、組裝已開始或不符合取消條件 |
| `order_payment_deadline_expired` | 409 | 訂單付款期限已到，不能建立新付款嘗試 |
| `coupon_invalid` | 400 | 優惠碼格式或基本資料無效 |
| `coupon_not_active` | 409 | 優惠券未開始、暫停、停用或已到期 |
| `coupon_not_applicable` | 409 | 商品、分類、會員或最低消費不符合 |
| `coupon_usage_exhausted` | 409 | 總量或每人次數已用完 |
| `coupon_multiple_not_allowed` | 409 | 同一訂單嘗試使用多張優惠券 |
| `coupon_code_duplicate` | 409 | 建立或修改的優惠碼與既有優惠券重複 |
| `coupon_state_conflict` | 409 | 優惠券目前狀態不允許啟用、暫停、停用或修改 |

## 付款、物流、退貨與退款

| Code | HTTP | 使用時機 |
|---|---:|---|
| `payment_method_not_allowed` | 409 | 訂單、商品或配送方式不允許該付款方式 |
| `payment_state_conflict` | 409 | 付款嘗試目前狀態不允許要求的完成、失敗或逾時操作 |
| `payment_attempt_expired` | 409 | 指定付款嘗試已超過自身有效期限 |
| `payment_cod_amount_exceeded` | 409 | COD 最終應付金額超過 NT$20,000 |
| `payment_cod_restricted_item` | 409 | COD 訂單含組裝電腦或任一 `RequiresPrepayment` SKU |
| `payment_event_duplicate` | 409 | Provider Event 已處理；API 可依 Provider 契約回安全冪等結果 |
| `shipping_method_not_allowed` | 409 | 商品尺寸、重量、分類或組裝限制不允許配送方式 |
| `shipping_store_inactive` | 409 | 選取門市已停用 |
| `shipping_constraint_exceeded` | 409 | 超過有效包裹尺寸或重量限制 |
| `shipping_tracking_duplicate` | 409 | 物流單號已被使用 |
| `shipping_batch_limit_exceeded` | 400 | 批次出貨超過 100 筆 |
| `shipping_order_not_ready` | 409 | 付款、組裝、保留或狀態尚未符合出貨條件 |
| `package_limit_period_overlap` | 409 | 同一物流 Provider 的包裹限制版本生效期間重疊 |
| `store_code_duplicate` | 409 | 同一 Provider 下的示範門市代碼已存在 |
| `return_deadline_expired` | 409 | 不符合一般退貨期限且不屬例外原因 |
| `return_quantity_exceeded` | 409 | 申請數量超過可退數量 |
| `return_shipment_deadline_expired` | 409 | 核准後未在期限內交寄，申請已取消 |
| `return_shipment_extension_not_allowed` | 409 | 退貨交寄期限已到、已延長一次或目前狀態不允許延長 |
| `return_state_conflict` | 409 | 目前退貨狀態不允許操作 |
| `refund_amount_exceeded` | 409 | 金額超過可退款餘額 |
| `refund_state_conflict` | 409 | 退款交易目前狀態不允許操作 |

## 模擬發票與折讓

| Code | HTTP | 使用時機 |
|---|---:|---|
| `invoice_order_unpaid` | 409 | 訂單尚未完成收款，不允許開立模擬發票 |
| `invoice_order_cancelled` | 409 | 訂單已取消，不允許開立模擬發票 |
| `invoice_already_exists` | 409 | 訂單已有模擬發票，不得重複開立 |
| `invoice_state_conflict` | 409 | 模擬發票目前狀態不允許所要求的開立、作廢或折讓操作 |
| `invoice_allowance_required` | 409 | 訂單已發生退款，不能作廢原發票，必須依成功退款建立折讓 |

## 客服、檔案上傳與 AI

| Code | HTTP | 使用時機 |
|---|---:|---|
| `support_ticket_state_conflict` | 409 | 目前客服狀態不允許操作 |
| `support_ticket_cancel_not_allowed` | 409 | 顧客已超過取消邊界或操作者無取消條件 |
| `support_ticket_assignment_conflict` | 409 | 案件已由其他客服領取或轉派；只回標準 Problem Details，不附最新承辦人 PublicId／DisplayName；前端須失效並重新查詢案件明細與所屬佇列 |
| `file_count_exceeded` | 400 | 同一資源的檔案數超過該 Endpoint 上限；尚未讀取檔案內容 |
| `file_size_exceeded` | 413 | 單檔或整體 Multipart 超過 Endpoint 上限 |
| `file_format_invalid` | 415 | 副檔名、MIME 或檔案簽章不在白名單或彼此不一致 |
| `file_malware_detected` | 422 | 格式可解析，但 Defender 確認內容有惡意程式 |
| `file_scan_unavailable` | 503 | Defender 不可用、逾時或結果不明，採 Fail Closed |
| `image_processing_failed` | 422 | 圖片格式已接受，但無法解碼、轉向或產生安全衍生圖 |
| `image_metadata_incomplete` | 422 | 商品圖片缺少第一版要求的 Alt、來源或授權欄位 |
| `ai_consent_required` | 409 | 尚未同意目前 AI 處理版本 |
| `ai_usage_limit_exceeded` | 429 | 使用者當日 AI 額度已用完 |
| `ai_budget_protection_active` | 503 | 非 Demo 流量因成本門檻停用 |
| `ai_output_invalid` | 502 | Structured Output 修復後仍無效 |
| `ai_service_unavailable` | 503 | OpenAI 逾時、限流或暫時性錯誤重試後仍失敗 |
| `ai_order_access_denied` | 404 | AI 工具嘗試取得非當前會員訂單 |
| `ai_tool_not_allowed` | 403 | 模型要求未列入白名單或寫入型工具 |

## 報表

| Code | HTTP | 使用時機 |
|---|---:|---|
| `report_key_invalid` | 400 | Report Key 不存在、不在第一版白名單或不支援該輸出形式 |
| `report_range_invalid` | 400 | 日期區間、粒度或比較期間不符合該報表限制 |

## Endpoint 覆蓋稽核

- 2026-08-13 稽核發現 53 筆未正式登錄引用：52 個明確別名與 1 個 `shipment_*` 萬用碼。
- 已將同義別名改為本目錄既有 code，新增確實具有不同前端處理方式的 code，並把 `shipment_*` 拆成三個正式物流錯誤。
- 2026-08-14 因 M 桌面 UI 補齊商品、會員、購物車、訂單、型錄、庫存、售後、優惠券及客服支撐 Endpoint；同步新增 12 個具有獨立前端處理語意的正式 code，再次自動稽核結果為 `Missing = 0`。
- 2026-08-19 補齊模擬發票開立、作廢與退款折讓的五個穩定業務錯誤碼；重新自動稽核結果為 `Missing = 0`，Endpoint 不得再以型別化文字原因代替正式 Problem Details code。
- 驗收條件：[[03-架構/02-API與前端契約/API Endpoint目錄]] 中所有反引號包覆的 snake_case 錯誤碼都必須存在於本目錄；自動檢查結果必須為 `Missing = 0`。

## 維護與驗收

- Endpoint 目錄建立時只能引用本表 code；缺少的業務錯誤需先補本表再發布契約。
- 每個 code 至少有一個 API 整合測試；授權、個資、庫存、金額及 AI 越權錯誤需有負面測試。
- OpenAPI 範例需使用安全訊息，不放 Stack Trace、SQL、檔案實體路徑、Token 或完整個資。
- 批次操作的逐筆錯誤仍使用本表 code，不另創只能由單一頁面理解的文字錯誤。
