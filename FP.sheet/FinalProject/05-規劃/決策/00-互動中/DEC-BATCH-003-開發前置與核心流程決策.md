---
type: decision-interaction
batch_id: DEC-BATCH-003
title: 開發前置與核心流程決策
status: applied
submission_feedback: ✅ 本批 30 項決策已寫回正式文件並封存。
created_at: 2026-08-10
submitted_at: 2026-08-11
decision_snapshot: "[[05-規劃/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]"
decision_count: 30
decision_range: DEC-P41～DEC-P70
q01_choice: hybrid_feature_ownership
q01_custom: ""
q02_choice: five_phase_freeze_day35
q02_custom: ""
q03_choice: m_build_migration_api_demo_gate
q03_custom: ""
q04_choice: three_weekly_integration
q04_custom: ""
q05_choice: main_dev_short_branches
q05_custom: ""
q06_choice: lead_only_merge
q06_custom: ""
q07_choice: identity_http_only_cookie
q07_custom: ""
q08_choice: separate_member_admin_cookies
q08_custom: ""
q09_choice: member_8h_7d_admin_2h
q09_custom: ""
q10_choice: explicit_origins_antiforgery_header
q10_custom: ""
q11_choice: email24_reset1_code10_guest30
q11_custom: ""
q12_choice: member5_15_admin5_30
q12_custom: ""
q13_choice: page_number_20_max100_total
q13_custom: ""
q14_choice: problem_details_code_trace_errors
q14_custom: ""
q15_choice: critical_writes_24h_provider_event
q15_custom: ""
q16_choice: rowversion_409_inventory_transaction
q16_custom: ""
q17_choice: retry_until_original_deadline
q17_custom: ""
q18_choice: direct_cancel_admin_exception
q18_custom: ""
q19_choice: cod_payment_awaiting_to_paid
q19_custom: ""
q20_choice: order_summary_plus_transactions
q20_custom: ""
q21_choice: one_method_one_shipment
q21_custom: ""
q22_choice: seven_days_extend_once
q22_custom: ""
q23_choice: auto_close_3_closed_final
q23_custom: ""
q24_choice: remind80_overdue100_supervisor
q24_custom: ""
q25_choice: xunit_integration_vitest_playwright
q25_custom: ""
q26_choice: dedicated_sqlserver_test_db
q26_custom: ""
q27_choice: pr_unit_affected_integration_dev_e2e
q27_custom: ""
q28_choice: structured_outputs_versioned_schema
q28_custom: ""
q29_choice: sql_rules_first_no_vector
q29_custom: ""
q30_choice: tiered_10_30_20_budget70_90
q30_custom: ""
---

# DEC-BATCH-003｜開發前置與核心流程決策

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題選擇一個方案，或選擇「完全採自主方案」並填寫自主輸入。自主輸入可以補充既有選項，也可以完整取代選項。送出只會更新本表狀態，不會直接修改正式文件。

## 建議依據

本批技術建議已核對以下官方資料：

- [ASP.NET Core 10：使用 Identity 保護 SPA Web API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
- [ASP.NET Core 10：防範 CSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [ASP.NET Core 10：CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
- [ASP.NET Core：Problem Details](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api)
- [ASP.NET Core 10：整合測試](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [Vitest 官方指南](https://vitest.dev/guide/)
- [Playwright 官方指南](https://playwright.dev/docs/intro)
- [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [OpenAI Embeddings](https://developers.openai.com/api/docs/guides/embeddings)

## A. 團隊、時程與 GitHub

### 1. DEC-P41｜五人模組負責方式

需要讓五人都能開發，同時避免每個人只負責單一技術層而長時間互相等待。

> [!tip] 建議
> 採混合式功能負責：每個核心領域有一位主要負責人及一位備援；組長負責架構、共用能力、整合與 Code Review，仍承接小型模組。實際人名與模組對應請在自主輸入補上。

```meta-bind
INPUT[select(
  option(hybrid_feature_ownership, '功能主責＋備援，組長負責架構整合（建議）'),
  option(layer_ownership, '依前端、後端、資料庫、AI、測試分層負責'),
  option(pair_rotation, '兩人配對並每週輪換模組'),
  option(custom_only, '完全採自主方案')
):q01_choice]
```

```meta-bind
INPUT[textArea(placeholder('填入五位成員代稱、主責模組與備援模組')):q01_custom]
```

### 2. DEC-P42｜40 天階段與功能凍結日

40 天包含文件、實作、測試、簡報與彩排，需要先保留整合及失敗修復時間。

> [!tip] 建議
> Day 1–5 架構與契約；Day 6–18 核心 M；Day 19–27 AI、報表與補齊 M；Day 28–34 整合測試；Day 35 起功能凍結，只修 Bug、整理資料、簡報及彩排。

```meta-bind
INPUT[select(
  option(five_phase_freeze_day35, '五階段，Day 35 起功能凍結（建議）'),
  option(four_week_sprints_freeze_day33, '四個短 Sprint，Day 33 起凍結'),
  option(module_completion_freeze_day37, '依模組完成推進，Day 37 起凍結'),
  option(custom_only, '完全採自主方案')
):q02_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各階段日期、交付物或凍結規則')):q02_custom]
```

### 3. DEC-P43｜S 功能的啟動門檻

若太早做收藏、評價、多語系或 AI 摘要，可能使 M 功能無法整合完成。

> [!tip] 建議
> 只有所有 M 專案可建置、核心資料庫 Migration 可重建、主要 API 契約穩定，且核心 Demo 流程已有自動化整合測試後，才允許個別 S 功能進入開發。

```meta-bind
INPUT[select(
  option(m_build_migration_api_demo_gate, 'M 可建置＋Migration＋API＋Demo 測試均通過（建議）'),
  option(per_module_gate, '各模組自己的 M 完成後即可做該模組 S'),
  option(fixed_day_gate, '到固定日期後由組長決定啟動 S'),
  option(custom_only, '完全採自主方案')
):q03_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充啟動門檻、核准者或例外')):q03_custom]
```

### 4. DEC-P44｜同步、整合與彩排頻率

五人可以固定同時協作，應利用固定節奏及早發現 API、資料庫與畫面不一致。

> [!tip] 建議
> 每日 15 分鐘同步；每週二、五整合 dev 並啟動全系統；Day 21 後每週一次走 Demo 主流程；功能凍結後每天跑一次完整 Demo。

```meta-bind
INPUT[select(
  option(daily_sync_twice_weekly_integration, '每日同步＋每週兩次整合＋後期固定彩排（建議）'),
  option(three_weekly_integration, '每週三次整合，不做每日同步'),
  option(weekly_integration, '每週只整合一次'),
  option(custom_only, '完全採自主方案')
):q04_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充固定星期、時段、參與者或失敗處理')):q04_custom]
```

### 5. DEC-P45｜Git 分支模型

現有規範草案使用 main、dev 與短生命週期功能分支，需要正式確認。

> [!tip] 建議
> 保留 `main` 與 `dev`；所有工作從最新 `dev` 建立短分支並以 PR 合回 `dev`，展示穩定版才由 `dev` PR 至 `main`。

```meta-bind
INPUT[select(
  option(main_dev_short_branches, 'main＋dev＋短功能分支（建議）'),
  option(trunk_based, 'main＋短分支，不使用 dev'),
  option(full_gitflow, '完整 Git Flow，加入 release 與 hotfix'),
  option(custom_only, '完全採自主方案')
):q05_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充分支用途、命名或發布流程')):q05_custom]
```

### 6. DEC-P46｜PR 審核與合併規則

需要兼顧五人速度與最低品質，並避免開發者未經檢查自行合併。

> [!tip] 建議
> 每個 PR 至少一位非作者核准、必要 CI 通過後使用 Squash Merge；資料庫 Migration、權限、付款退款及共用契約需由組長或指定 Reviewer 審核。

```meta-bind
INPUT[select(
  option(one_review_ci_squash_sensitive_owner, '一人核准＋CI＋Squash，敏感變更指定審核（建議）'),
  option(two_reviews_all, '所有 PR 都需兩人核准＋CI'),
  option(lead_only_merge, '只需組長核准並由組長合併'),
  option(custom_only, '完全採自主方案')
):q06_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Reviewer、CI 條件或合併方式')):q06_custom]
```

## B. 身分驗證與 API 共通規範

### 7. DEC-P47｜Vue 瀏覽器登入驗證方式

系統只有瀏覽器前台與後台，不需要提供第三方行動 App 或外部 API Client。

> [!tip] 建議
> 使用 ASP.NET Core Identity 的安全 HttpOnly Cookie。官方文件建議瀏覽器應用優先使用 Cookie，避免 Token 暴露給 JavaScript；狀態變更 API 必須搭配 Antiforgery。

```meta-bind
INPUT[select(
  option(identity_http_only_cookie, 'ASP.NET Core Identity＋HttpOnly Cookie（建議）'),
  option(access_memory_refresh_cookie, 'Access Token 只存記憶體＋Refresh Token Cookie'),
  option(jwt_browser_storage, 'JWT 存瀏覽器儲存空間'),
  option(custom_only, '完全採自主方案')
):q07_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Cookie、Token、Identity 或登入需求')):q07_custom]
```

### 8. DEC-P48｜會員與管理員 Session 是否分離

管理員強制 TOTP，安全等級與一般會員不同。

> [!tip] 建議
> 使用兩個 Cookie Scheme 與不同 Cookie 名稱、路徑及期限；管理員登入完成密碼與 TOTP 後才建立管理 Session，會員 Cookie 不可存取管理 API。

```meta-bind
INPUT[select(
  option(separate_member_admin_cookies, '會員與管理員使用獨立 Cookie Scheme（建議）'),
  option(same_cookie_roles, '共用 Cookie，僅以角色區分'),
  option(separate_identity_stores, '會員與管理員使用完全不同帳號資料表'),
  option(custom_only, '完全採自主方案')
):q08_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充帳號表、Cookie Scheme 或登入入口')):q08_custom]
```

### 9. DEC-P49｜登入 Session 有效時間

期限過長會增加帳號被占用的風險，過短則不利現場展示。

> [!tip] 建議
> 會員閒置 8 小時滑動續期、最長 7 天；管理員 2 小時絕對到期，不滑動續期。登出、停權、密碼或 TOTP 異動時使既有 Session 失效。

```meta-bind
INPUT[select(
  option(member_8h_7d_admin_2h, '會員 8h／最長 7d；管理員絕對 2h（建議）'),
  option(member_24h_admin_4h, '會員 24h；管理員 4h'),
  option(all_8h_sliding, '會員與管理員皆為 8h 滑動續期'),
  option(custom_only, '完全採自主方案')
):q09_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充期限、續期或強制失效事件')):q09_custom]
```

### 10. DEC-P50｜Cookie 驗證下的 CSRF 與 CORS

Vue 與 API 開發時使用不同 Port，瀏覽器會視為不同 Origin；Cookie 又會由瀏覽器自動送出。

> [!tip] 建議
> CORS 只允許設定檔列出的前台與後台 Origin並允許 Credentials；所有 POST、PUT、PATCH、DELETE 使用 Antiforgery Header；禁止 `AllowAnyOrigin` 與 Credentials 同時使用。

```meta-bind
INPUT[select(
  option(explicit_origins_antiforgery_header, '明確 Origin 白名單＋Credentials＋Antiforgery（建議）'),
  option(same_origin_proxy_only, '只允許同 Origin，開發時由 Vite Proxy 轉送'),
  option(bearer_no_antiforgery, '改用 Bearer Token，不使用 Cookie Antiforgery'),
  option(custom_only, '完全採自主方案')
):q10_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Origin、HTTPS、Header 或開發環境策略')):q10_custom]
```

### 11. DEC-P51｜Email 與訪客訂單 Token 期限

Email 驗證、重設密碼與訪客限單授權具有不同風險。

> [!tip] 建議
> Email 驗證連結 24 小時、重設密碼連結 1 小時、訪客訂單驗證碼 10 分鐘、驗證成功後的限單 Token 30 分鐘；所有 Token 單次使用或可撤銷。

```meta-bind
INPUT[select(
  option(email24_reset1_code10_guest30, '驗證 24h／重設 1h／驗證碼 10m／限單 30m（建議）'),
  option(email48_reset2_code15_guest60, '驗證 48h／重設 2h／驗證碼 15m／限單 60m'),
  option(all_24h, '所有 Email 與訪客 Token 統一 24h'),
  option(custom_only, '完全採自主方案')
):q11_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各 Token 期限、單次使用或撤銷規則')):q11_custom]
```

### 12. DEC-P52｜登入失敗鎖定規則

會員與管理員登入需要防止密碼暴力嘗試，但現場展示也要容易復原。

> [!tip] 建議
> 會員連續 5 次失敗鎖定 15 分鐘；管理員 5 次失敗鎖定 30 分鐘並記錄稽核；成功登入後重設計數，SuperAdmin 可人工解鎖但必須稽核。

```meta-bind
INPUT[select(
  option(member5_15_admin5_30, '會員 5 次／15m；管理員 5 次／30m（建議）'),
  option(all5_15, '全部帳號 5 次／15m'),
  option(progressive_lockout, '鎖定時間依重複失敗逐次增加'),
  option(custom_only, '完全採自主方案')
):q12_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充次數、期限、解鎖者或稽核規則')):q12_custom]
```

### 13. DEC-P53｜列表 API 分頁格式

商品搜尋、後台表格、案件工作台與訂單列表需要一致的分頁契約。

> [!tip] 建議
> 使用 `pageNumber`、`pageSize`，預設 20、最大 100；回傳 `items`、`pageNumber`、`pageSize`、`totalCount`、`totalPages`。一萬筆展示資料不需要先導入 Cursor 分頁。

```meta-bind
INPUT[select(
  option(page_number_20_max100_total, 'Page Number，預設 20、最大 100、回傳總數（建議）'),
  option(offset_limit, 'Offset＋Limit＋Total Count'),
  option(cursor_pagination, 'Cursor 分頁，不固定回傳總數'),
  option(custom_only, '完全採自主方案')
):q13_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充分頁欄位、上限、排序或總數規則')):q13_custom]
```

### 14. DEC-P54｜API 錯誤回應格式

兩個 Vue 應用需要用同一方式處理驗證、商業錯誤、權限及未預期錯誤。

> [!tip] 建議
> 使用 RFC Problem Details；額外加入穩定 `code`、`traceId` 與欄位驗證 `errors`。正式環境不回傳 Stack Trace 或內部例外訊息。

```meta-bind
INPUT[select(
  option(problem_details_code_trace_errors, 'Problem Details＋code＋traceId＋errors（建議）'),
  option(custom_envelope, '自訂 success/data/error Envelope'),
  option(http_status_message_only, '只使用 HTTP Status 與文字訊息'),
  option(custom_only, '完全採自主方案')
):q14_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充錯誤欄位、錯誤碼命名或前端顯示規則')):q14_custom]
```

### 15. DEC-P55｜冪等鍵適用範圍與保存時間

建立訂單、付款、退款及模擬回呼重複送達時不得產生兩次副作用。

> [!tip] 建議
> 建立訂單及人工退款要求 `Idempotency-Key`；付款／物流回呼以 Provider Event Id 去重；成功或失敗結果保存 24 小時，同一 Key 搭配不同 Payload 時回傳衝突。

```meta-bind
INPUT[select(
  option(critical_writes_24h_provider_event, '關鍵寫入 Key 24h＋回呼 Event Id（建議）'),
  option(all_posts_72h, '所有 POST 都要求 Key 並保存 72h'),
  option(callbacks_only, '只有外部回呼做冪等'),
  option(custom_only, '完全採自主方案')
):q15_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充適用 API、保存時間或衝突回應')):q15_custom]
```

### 16. DEC-P56｜一般資料與庫存的併發控制

最後一件庫存需要資料庫交易；後台商品、訂單及退款編輯也需要避免覆蓋他人變更。

> [!tip] 建議
> 一般可編輯資料使用 SQL Server `rowversion` 並在衝突時回傳 409；庫存保留另使用交易與條件更新，不能只靠 RowVersion；前端提示重新載入後再操作。

```meta-bind
INPUT[select(
  option(rowversion_409_inventory_transaction, 'RowVersion＋409；庫存另用交易條件更新（建議）'),
  option(last_write_wins, '一般資料採最後寫入覆蓋，庫存才做併發'),
  option(serializable_all, '所有重要寫入都使用 Serializable 交易'),
  option(custom_only, '完全採自主方案')
):q16_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 RowVersion 範圍、隔離層級或前端處理')):q16_custom]
```

## C. 訂單、付款、物流與客服狀態

### 17. DEC-P57｜付款失敗或取消後能否重試

一次付款嘗試失敗不一定代表顧客要取消整張訂單。

> [!tip] 建議
> 訂單維持 `PendingPayment` 到原付款期限；每次重試建立新的 Payment Attempt，失敗紀錄不可倒退。期限到期才取消訂單並釋放庫存。

```meta-bind
INPUT[select(
  option(retry_until_original_deadline, '保留訂單至原期限並建立新付款嘗試（建議）'),
  option(cancel_on_first_failure, '第一次失敗或取消即取消訂單'),
  option(extend_deadline_on_retry, '每次重試都延長付款與庫存期限'),
  option(custom_only, '完全採自主方案')
):q17_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充重試次數、期限或付款方式切換規則')):q17_custom]
```

### 18. DEC-P58｜需要人工審核的訂單取消資料模型

第一版只有整筆取消；符合條件的取消可以立即執行，例外取消可能需管理員判斷。

> [!tip] 建議
> 第一版不建立獨立 CancellationRequest。符合規則時直接取消；不符合時禁止顧客取消，由管理員以有權限、必填原因及稽核的例外操作處理。

```meta-bind
INPUT[select(
  option(direct_cancel_admin_exception, '直接取消＋管理員例外操作，不建申請表（建議）'),
  option(cancellation_request_entity, '所有取消先建立 CancellationRequest'),
  option(request_only_paid, '未付款直接取消，已付款建立取消申請'),
  option(custom_only, '完全採自主方案')
):q18_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充取消審核、原因或退款關聯')):q18_custom]
```

### 19. DEC-P59｜貨到付款的 Payment 紀錄

COD 訂單建立時尚未收款，但仍需要在取貨時完成付款歷程。

> [!tip] 建議
> 訂單建立時建立 `AwaitingPayment` 的 COD Payment；超商 `PickedUp` 時以同一交易轉為 `Paid`。未取退回時轉為 `Cancelled`，不可標記付款成功。

```meta-bind
INPUT[select(
  option(cod_payment_awaiting_to_paid, '建立 AwaitingPayment，取貨時轉 Paid（建議）'),
  option(create_payment_on_pickup, '取貨完成時才建立 Paid Payment'),
  option(no_payment_record_for_cod, 'COD 不建立 Payment 紀錄'),
  option(custom_only, '完全採自主方案')
):q19_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 COD 付款、未取、退回或冪等規則')):q19_custom]
```

### 20. DEC-P60｜退款狀態是否採兩層模型

一張訂單可能有多次部分退款，單一狀態無法同時表達訂單累計與每次退款結果。

> [!tip] 建議
> 採 `OrderRefundStatus` 表達訂單累計，`RefundTransactionStatus` 表達每次人工審核及執行結果；成功後重新計算訂單退款彙總。

```meta-bind
INPUT[select(
  option(order_summary_plus_transactions, '訂單退款彙總＋單次退款交易（建議）'),
  option(transactions_only, '只保存退款交易，查詢時計算彙總'),
  option(single_refund_status, '訂單只保存單一退款狀態'),
  option(custom_only, '完全採自主方案')
):q20_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充狀態名稱、彙總規則或多次退款限制')):q20_custom]
```

### 21. DEC-P61｜第一版物流單數量

若同一訂單拆多種配送或多張物流單，會大幅增加出貨、退貨、運費及前台顯示複雜度。

> [!tip] 建議
> 第一版限制一張訂單只能選一種配送方式並建立一張主要物流單；所有商品需同批出貨。無法符合者在結帳前拆成不同訂單。

```meta-bind
INPUT[select(
  option(one_method_one_shipment, '一張訂單一種配送、一張主要物流單（建議）'),
  option(multiple_shipments_same_method, '同配送方式可拆多張物流單'),
  option(multiple_methods_shipments, '同訂單可使用多種配送與物流單'),
  option(custom_only, '完全採自主方案')
):q21_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充拆單、運費、部分出貨或物流單限制')):q21_custom]
```

### 22. DEC-P62｜退貨核准後的寄回期限

退貨核准後若長期未寄回，案件與預期退款不能無限保持等待。

> [!tip] 建議
> 核准後 7 個日曆天內需完成交寄；未交寄自動取消退貨申請。管理員可在到期前延長一次 7 天，必須記錄理由及通知顧客。

```meta-bind
INPUT[select(
  option(seven_days_extend_once, '7 天交寄，可人工延長一次 7 天（建議）'),
  option(ten_days_no_extension, '10 天交寄，不提供延長'),
  option(fourteen_days, '14 天交寄'),
  option(custom_only, '完全採自主方案')
):q22_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充起算點、期限、延長或逾時狀態')):q22_custom]
```

### 23. DEC-P63｜客服 Resolved 自動結案與 Closed 重開

已決定 Resolved 後 3 天內可以重開，但尚未決定三天後的處理方式。

> [!tip] 建議
> `Resolved` 滿 3 天自動轉 `Closed`；`Closed` 不重開，若仍需協助則建立新案件並關聯舊案。規則最容易說明、測試及計算 SLA。

```meta-bind
INPUT[select(
  option(auto_close_3_closed_final, 'Resolved 3 天自動結案，Closed 不重開（建議）'),
  option(auto_close_3_supervisor_reopen14, '3 天結案，主管可在 14 天內重開'),
  option(manual_close_closed_reopen, '只人工結案，主管可重開 Closed'),
  option(custom_only, '完全採自主方案')
):q23_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充自動結案、重開角色或新舊案件關聯')):q23_custom]
```

### 24. DEC-P64｜SLA 即將逾時與逾時升級

只有期限沒有通知及處置，後台仍難以實際管理客服風險。

> [!tip] 建議
> 用掉 80% 時間時通知承辦客服；100% 逾時時通知承辦與 CustomerServiceSupervisor、標記 Overdue 並置頂。第一版不自動轉派或提高優先級，避免隱藏副作用。

```meta-bind
INPUT[select(
  option(remind80_overdue100_supervisor, '80% 提醒；100% 通知主管並置頂（建議）'),
  option(overdue_only, '只在 100% 逾時時通知主管'),
  option(auto_escalate_reassign, '80% 提升優先級，100% 自動轉派主管'),
  option(custom_only, '完全採自主方案')
):q24_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充通知對象、站內／Email、升級或報表規則')):q24_custom]
```

## D. 測試與 AI

### 25. DEC-P65｜前後端測試工具組合

需要能覆蓋 .NET 領域規則、API 與 Vue 元件，也要有少量完整 Demo 流程。

> [!tip] 建議
> 後端使用 xUnit 與 `Microsoft.AspNetCore.Mvc.Testing`；Vue 使用 Vitest 與 Vue Test Utils；跨前後端主流程使用 Playwright。

```meta-bind
INPUT[select(
  option(xunit_integration_vitest_playwright, 'xUnit＋ASP.NET Integration＋Vitest＋Playwright（建議）'),
  option(mstest_vitest_playwright, 'MSTest＋Vitest＋Playwright'),
  option(xunit_playwright_only, '後端 xUnit，前端只做 Playwright E2E'),
  option(custom_only, '完全採自主方案')
):q25_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充測試框架、Mock 套件或責任邊界')):q25_custom]
```

### 26. DEC-P66｜後端整合測試資料庫

SQLite 與 SQL Server 在交易、RowVersion、索引及查詢行為上不同，庫存併發又是核心風險。

> [!tip] 建議
> 使用獨立 SQL Server 測試資料庫，測試集合開始前套用 Migration，案例間以可重複方式清理；Domain/Application 單元測試不連資料庫。

```meta-bind
INPUT[select(
  option(dedicated_sqlserver_test_db, '獨立 SQL Server 測試資料庫（建議）'),
  option(sqlite_in_memory, 'SQLite In-Memory 作為整合測試資料庫'),
  option(mock_dbcontext, '整合測試也 Mock DbContext'),
  option(custom_only, '完全採自主方案')
):q26_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充資料庫名稱、清理策略或測試隔離')):q26_custom]
```

### 27. DEC-P67｜PR 最低自動檢查

測試若全部留到最後，五人整合時才會同時爆出錯誤。

> [!tip] 建議
> 每個 PR 必跑 .NET build/unit、受影響 integration、Vue lint/type-check/unit；合回 dev 後跑五條核心 Playwright 流程。Migration、權限、金額與庫存變更必須有對應測試。

```meta-bind
INPUT[select(
  option(pr_unit_affected_integration_dev_e2e, 'PR 單元與受影響整合；dev 跑核心 E2E（建議）'),
  option(pr_all_tests, '每個 PR 都跑全部測試與 E2E'),
  option(manual_until_freeze, '功能凍結前只做人工測試'),
  option(custom_only, '完全採自主方案')
):q27_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充必跑命令、核心流程數量或失敗阻擋規則')):q27_custom]
```

### 28. DEC-P68｜AI 自然語言需求的結構化方式

AI 搜尋需要把用途、預算、品牌與規格轉成後端可驗證的固定契約，不能直接接受自由文字欄位作查詢。

> [!tip] 建議
> 使用 OpenAI Structured Outputs 與版本化 JSON Schema；後端再做商業驗證。必要欄位缺漏時回傳澄清問題，不猜測；拒絕、截斷或商業驗證失敗時不執行查詢。

```meta-bind
INPUT[select(
  option(structured_outputs_versioned_schema, 'Structured Outputs＋版本化 Schema＋後端驗證（建議）'),
  option(function_calling_schema, '使用 Function Calling 產生查詢參數'),
  option(plain_json_retry, '要求一般 JSON，解析失敗後重試'),
  option(custom_only, '完全採自主方案')
):q28_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Schema 欄位、追問策略、拒絕或修復流程')):q28_custom]
```

### 29. DEC-P69｜AI 商品檢索是否使用向量

第一版約 750 個 SKU，且價格、庫存與相容性都必須由後端確定性驗證。

> [!tip] 建議
> 第一版不使用向量資料庫：AI 只結構化需求，SQL 依預算、分類、品牌與規格篩選，再由相容性規則及排序產生候選。先用評估資料集量測，只有關鍵字與結構化篩選不足時才加入 Embedding 混合檢索。

```meta-bind
INPUT[select(
  option(sql_rules_first_no_vector, 'SQL＋規則優先，第一版不使用向量（建議）'),
  option(hybrid_sql_embeddings, 'SQL 結構化篩選＋Embedding 混合排序'),
  option(vector_first_then_rules, '向量先找候選，再做 SQL 與相容性驗證'),
  option(custom_only, '完全採自主方案')
):q29_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充檢索欄位、Embedding、評估門檻或 fallback')):q29_custom]
```

### 30. DEC-P70｜AI 每日使用與總預算保護

AI 預算以 100 美元為基準，訪客搜尋又對外開放，必須同時限制濫用與 Demo 期間停用風險。

> [!tip] 建議
> 訪客 AI 搜尋每 IP＋瀏覽器每日 10 次；會員搜尋 30 次、AI 客服 20 則訊息。估算累計達 70 美元警告，90 美元時停用非 Demo 帳號的 AI，保留組長可控制的 Demo Allowlist。

```meta-bind
INPUT[select(
  option(tiered_10_30_20_budget70_90, '訪客 10／會員搜尋 30／客服 20；$70 警告、$90 保護（建議）'),
  option(lenient_20_60_40_budget80_95, '訪客 20／會員搜尋 60／客服 40；$80／$95'),
  option(global_budget_only, '不限制個人次數，只設全域預算'),
  option(custom_only, '完全採自主方案')
):q30_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充每日次數、Token、美元警戒、Demo 帳號或重設時間')):q30_custom]
```

## 批次操作

> [!warning]
> 送出前請確認 30 題均已選擇方案，或已在自主輸入提供完整方案。按鈕只更新本檔 Metadata；完整性與衝突由 Codex 收束時檢查。

`BUTTON[submit-decision-batch-003,restore-draft-003]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-003
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "✅ 已送出本批 30 項決策；答案已保存，可交由 Codex 收束。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-003
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
