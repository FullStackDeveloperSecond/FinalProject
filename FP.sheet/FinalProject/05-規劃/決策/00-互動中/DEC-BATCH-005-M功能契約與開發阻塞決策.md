---
type: decision-interaction
batch_id: DEC-BATCH-005
title: M 功能契約與開發阻塞決策
status: applied
submission_feedback: ✅ 本批 30 項決策已寫回正式文件並封存。
created_at: 2026-08-11
submitted_at: 2026-08-11
applied_at: 2026-08-12
decision_snapshot: "[[05-規劃/決策/02-已寫回/DEC-BATCH-005-M功能契約與開發阻塞決策]]"
decision_count: 30
decision_range: DEC-P85～DEC-P114
q01_choice: accept_glossary_canonical
q01_custom: ""
q02_choice: query_relevance_browse_popularity
q02_custom: ""
q03_choice: and_groups_or_same_field
q03_custom: ""
q04_choice: sum_sku_keep_build_groups_conflict
q04_custom: ""
q05_choice: retain_flag_require_action
q05_custom: ""
q06_choice: unverified_seven_days_cleanup
q06_custom: ""
q07_choice: soft_delete_anonymize_keep_transactions
q07_custom: ""
q08_choice: revoke_block_login_public_only
q08_custom: ""
q09_choice: role_union_explicit_sensitive_policies
q09_custom: ""
q10_choice: customer_pre_reply_staff_inprogress_reason
q10_custom: ""
q11_choice: normalized_attention_due_created
q11_custom: ""
q12_choice: primary_summary_plus_badges
q12_custom: ""
q13_choice: provider_profile_guardrails
q13_custom: ""
q14_choice: draft_schedule_immutable_published
q14_custom: ""
q15_choice: max100_provider_tracking
q15_custom: ""
q16_choice: ui_and_utf8_csv_result
q16_custom: ""
q17_choice: xlsx_and_csv_import
q17_custom: ""
q18_choice: three_sheets_reference_existing_lookups
q18_custom: ""
q19_choice: full_preview_downloadable_errors_atomic
q19_custom: ""
q20_choice: two_brands_100_fictional_stores
q20_custom: ""
q21_choice: resellable_only_restock_others_quarantine
q21_custom: ""
q22_choice: reject_overlap_coupon_after_sale
q22_custom: ""
q23_choice: reject400_repeatable_sort_whitelist
q23_custom: ""
q24_choice: rest_baseline_400_409_202_async
q24_custom: ""
q25_choice: domain_reason_snake_case
q25_custom: ""
q26_choice: compact_business_schema_no_db_fields
q26_custom: ""
q27_choice: allowlisted_readonly_application_tools
q27_custom: ""
q28_choice: source_control_immutable_versions_record_usage
q28_custom: ""
q29_choice: search8_support12_one_retry_one_repair
q29_custom: ""
q30_choice: five_business_risk_flows
q30_custom: ""
---

# DEC-BATCH-005｜M 功能契約與開發阻塞決策

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題選擇一個方案，或選擇「完全採自主方案」並填寫自主輸入。自主輸入可以補充選項，也可以完整取代選項。送出只保存答案，不會直接修改正式文件。

本批優先處理目前 P0／P1 中會阻擋 M 功能使用案例、API、資料模型、AI 安全與 E2E 測試的決策。OpenAI 模型、套件版本、成員姓名與實際排程另批處理，避免未經查證或缺少人員資料便自行定案。

## A. 共用需求、搜尋與會員生命週期

### 1. DEC-P85｜專案英文名詞是否正式定版

現有名詞表已列出 Product、SKU、Order、Shipment、ReturnRequest、SupportTicket 等建議英文名；未定版會讓 Entity、API 與資料表出現同義名稱。

> [!tip] 建議
> 接受現有名詞表的建議英文名稱作為第一版唯一命名；之後若需改名，必須同時更新名詞表與受影響契約，不能由各模組自行使用別名。

```meta-bind
INPUT[select(
  option(accept_glossary_canonical, '接受現有建議英文名稱並作為唯一標準（建議）'),
  option(review_all_before_code, '先逐詞審核，全部確認後才定版'),
  option(core_only_local_others, '只固定核心 Entity，其餘由模組自行命名'),
  option(custom_only, '完全採自主方案')
):q01_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出要改名的詞、替代英文名稱或額外命名規則')):q01_custom]
```

### 2. DEC-P86｜商品搜尋的預設排序

一般瀏覽與輸入關鍵字時的意圖不同，需要固定預設值與同值排序，否則分頁結果可能跳動。

> [!tip] 建議
> 有關鍵字時以相關度優先，無關鍵字時以近期銷售熱度優先；同值再依上架時間、不可變 SKU Code 排序。缺貨與下架不參與可購買結果。

```meta-bind
INPUT[select(
  option(query_relevance_browse_popularity, '關鍵字相關度／瀏覽熱度，最後以 SKU Code 穩定排序（建議）'),
  option(always_newest, '一律以上架時間最新優先'),
  option(always_price_low, '一律以價格由低至高'),
  option(custom_only, '完全採自主方案')
):q02_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充相關度、熱度期間、缺貨位置或同值排序')):q02_custom]
```

### 3. DEC-P87｜搜尋篩選條件的組合方式

分類、品牌、價格、庫存與規格可能同時套用，需要先定義 AND／OR 邏輯與允許欄位。

> [!tip] 建議
> 不同欄位群組使用 AND；同一多選欄位內使用 OR，例如品牌為 ASUS 或 MSI，同時價格需在區間內。規格欄位只接受分類白名單，不接受任意查詢運算式。

```meta-bind
INPUT[select(
  option(and_groups_or_same_field, '跨群組 AND、同欄位多選 OR、規格白名單（建議）'),
  option(all_conditions_and, '所有條件一律 AND'),
  option(advanced_boolean_expression, '支援任意 AND／OR 群組運算式'),
  option(custom_only, '完全採自主方案')
):q03_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充篩選欄位、空值、價格區間或未支援規格處理')):q03_custom]
```

### 4. DEC-P88｜訪客購物車登入後的重複 SKU 合併

訪客購物車與會員既有購物車可能包含相同 SKU 或相同組裝清單。

> [!tip] 建議
> 一般 SKU 數量相加；超過購買上限或可售數量時不自動截斷，而是標記衝突請使用者選擇。組裝清單按獨立組裝群組保留，不因 SKU 相同便拆散合併。

```meta-bind
INPUT[select(
  option(sum_sku_keep_build_groups_conflict, '一般 SKU 相加、組裝群組分開、超限要求確認（建議）'),
  option(member_cart_wins, '會員購物車完全覆蓋訪客購物車'),
  option(guest_cart_wins, '訪客購物車完全覆蓋會員購物車'),
  option(custom_only, '完全採自主方案')
):q04_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充數量上限、組裝群組或使用者確認方式')):q04_custom]
```

### 5. DEC-P89｜合併時價格、庫存或上下架已改變

登入合併不能默默接受新價格，也不能讓無效項目直接消失。

> [!tip] 建議
> 保留項目但標示「需處理」，顯示價格差異、庫存不足或已下架原因；使用者確認新價格、調整數量或移除後才能結帳。

```meta-bind
INPUT[select(
  option(retain_flag_require_action, '保留並標記衝突，處理完成後才能結帳（建議）'),
  option(auto_update_remove_invalid, '自動接受新價格並移除無效項目'),
  option(abort_entire_merge, '任一衝突就取消整次合併'),
  option(custom_only, '完全採自主方案')
):q05_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充價格差異提示、缺貨、下架或合併失敗規則')):q05_custom]
```

### 6. DEC-P90｜未完成 Email 驗證帳號的保存期限

未驗證帳號會占用 Email，永久保存也會累積無效資料。

> [!tip] 建議
> 未驗證帳號保留 7 天，期間可重寄驗證信；到期且沒有訂單或其他必須保存的資料時清除。再次註冊可重新建立驗證流程。

```meta-bind
INPUT[select(
  option(unverified_seven_days_cleanup, '保留 7 天後清除，可重寄驗證信（建議）'),
  option(unverified_thirty_days, '保留 30 天後清除'),
  option(unverified_forever, '永久保留，由管理員人工清理'),
  option(custom_only, '完全採自主方案')
):q06_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充保存天數、重寄次數或重新註冊規則')):q06_custom]
```

### 7. DEC-P91｜會員刪除與交易資料匿名化

訂單、退款與稽核不能因刪除帳號而消失，但會員個資也不應無限留在會員檔案。

> [!tip] 建議
> 採軟刪除帳號並匿名化可移除的會員個資；既有訂單的必要交易與收件快照依保存規則保留，但與已刪除會員的日常登入關聯失效。操作需要稽核。

```meta-bind
INPUT[select(
  option(soft_delete_anonymize_keep_transactions, '軟刪除＋匿名化會員資料，保留必要交易快照（建議）'),
  option(admin_request_only, '不提供刪除，只能由管理員受理個資請求'),
  option(hard_delete_if_no_orders, '無訂單可硬刪，有訂單則拒絕刪除'),
  option(custom_only, '完全採自主方案')
):q07_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充可匿名化欄位、保留資料、恢復或核准流程')):q07_custom]
```

### 8. DEC-P92｜會員停權後可用範圍

停權可能是風險處置，也可能只是禁止交易；需要明確決定既有 Session 與訂單查詢行為。

> [!tip] 建議
> 停權立即撤銷所有 Session 並禁止登入、購買、評價、檢舉與客服；公開瀏覽仍可使用。既有訂單由 Email 驗證或管理員協助處理，解除停權後才能恢復會員登入。

```meta-bind
INPUT[select(
  option(revoke_block_login_public_only, '撤銷 Session 並禁止登入，只保留公開瀏覽（建議）'),
  option(read_only_member_login, '仍可登入查看訂單，但禁止所有寫入'),
  option(block_purchase_only, '只禁止購買，其他會員功能照常'),
  option(custom_only, '完全採自主方案')
):q08_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充停權原因、可用功能、通知、解除或訂單處理')):q08_custom]
```

## B. 權限、案件與訂單後台

### 9. DEC-P93｜多角色權限的合併方式

一位管理員可同時擁有多個角色，需要決定權限衝突及敏感操作如何判定。

> [!tip] 建議
> 第一版採允許權限聯集，不設顯式 Deny；敏感操作以明確 Policy 指定可執行角色與必要條件。`SuperAdmin` 全域允許，但仍需通過 2FA 與稽核。

```meta-bind
INPUT[select(
  option(role_union_explicit_sensitive_policies, '角色允許聯集＋敏感操作明確 Policy（建議）'),
  option(deny_overrides_allow, '加入顯式 Deny，Deny 優先於 Allow'),
  option(primary_role_only, '只使用一個主要角色決定權限'),
  option(custom_only, '完全採自主方案')
):q09_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充權限衝突、SuperAdmin 邊界或敏感 Policy')):q09_custom]
```

### 10. DEC-P94｜客服案件允許取消的角色與狀態

`Cancelled` 已存在，但尚未定義顧客與客服可在哪些狀態使用。

> [!tip] 建議
> 顧客只可在 `Open` 或 `Assigned` 且尚無人工公開回覆時取消；承辦客服或主管可在 `Open`、`Assigned`、`InProgress` 取消，但必填原因。已有實質處理結果時改用 `Resolved`，不使用取消隱藏歷程。

```meta-bind
INPUT[select(
  option(customer_pre_reply_staff_inprogress_reason, '顧客限人工回覆前；客服可至 InProgress 且必填原因（建議）'),
  option(supervisor_only_cancel, '只有客服主管可以取消任何未結案件'),
  option(customer_any_open_status, '顧客可取消所有未結狀態'),
  option(custom_only, '完全採自主方案')
):q10_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充角色、狀態、原因、SLA 或通知規則')):q10_custom]
```

### 11. DEC-P95｜統一案件工作台跨領域排序

客服、檢舉與退貨的狀態和期限不同，不能直接用單一原始狀態字串排序。

> [!tip] 建議
> 各領域先投影共同 `attentionLevel`、`dueAt`、`createdAt`；預設依逾時、注意等級、到期時間、建立時間與穩定 ID 排序。沒有期限的項目排在有期限項目之後。

```meta-bind
INPUT[select(
  option(normalized_attention_due_created, '正規化注意等級與期限後跨領域排序（建議）'),
  option(newest_first_all, '所有領域一律最新建立優先'),
  option(separate_tabs_no_cross_sort, '只保留三個分頁，不做跨領域排序'),
  option(custom_only, '完全採自主方案')
):q11_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充共同欄位、排序優先、無期限項目或分頁方式')):q11_custom]
```

### 12. DEC-P96｜後台訂單摘要狀態的呈現方式

訂單、付款、物流、退貨與退款狀態仍要分開保存，摘要只能用於列表顯示與篩選。

> [!tip] 建議
> 使用一個由訂單／履約推導的主要摘要，例如待付款、處理中、待出貨、配送中、已完成、已取消；付款失敗、部分退款、退貨中等另外顯示徽章，不強塞進單一優先順序。

```meta-bind
INPUT[select(
  option(primary_summary_plus_badges, '主要摘要狀態＋付款／退貨／退款徽章（建議）'),
  option(single_precedence_summary, '所有狀態依固定優先順序合成一個摘要'),
  option(raw_states_only, '列表直接顯示所有原始狀態，不提供摘要'),
  option(custom_only, '完全採自主方案')
):q12_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充摘要名稱、映射、徽章或篩選參數')):q12_custom]
```

### 13. DEC-P97｜包裹限制可編輯值的安全邊界

包裹限制可由後台版本化調整，但需要防止負數、互相矛盾或明顯誤輸入。

> [!tip] 建議
> 每種模擬物流服務建立不可由一般管理員修改的安全範圍；管理員只可在該範圍內設定單邊、三邊和與重量，且必須通過單邊不大於三邊和等跨欄位驗證。

```meta-bind
INPUT[select(
  option(provider_profile_guardrails, '依物流服務設定安全範圍＋跨欄位驗證（建議）'),
  option(universal_broad_limits, '所有物流服務共用一組寬鬆上下限'),
  option(positive_values_only, '只驗證大於零，不設上限'),
  option(custom_only, '完全採自主方案')
):q13_choice]
```

```meta-bind
INPUT[textArea(placeholder('填入各欄位上下限、物流服務差異或跨欄位規則')):q13_custom]
```

### 14. DEC-P98｜包裹設定版本的生效與併發修改

同時修改或預約未來生效時，必須避免兩個版本重疊及覆蓋。

> [!tip] 建議
> 支援草稿與排定生效時間；同一物流服務任一時間只有一個有效版本。發布使用 `rowversion`，衝突回傳 409；生效後不可修改，只能建立新版本取代。

```meta-bind
INPUT[select(
  option(draft_schedule_immutable_published, '草稿＋排定生效＋已發布不可變＋RowVersion（建議）'),
  option(immediate_only_new_version, '只能立即生效，每次修改建立新版本'),
  option(edit_active_in_place, '允許直接修改目前有效版本'),
  option(custom_only, '完全採自主方案')
):q14_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充生效時間、時區、重疊、取消排程或併發規則')):q14_custom]
```

### 15. DEC-P99｜批次出貨上限與物流單號

批次操作已確認逐筆驗證及部分成功，仍需限制單次負載並決定模擬物流單號來源。

> [!tip] 建議
> 單批最多 100 筆；若未提供物流單號，由模擬物流 Provider 產生唯一單號。重複單號或同訂單已有主要物流單時該筆失敗，不影響其他合法項目。

```meta-bind
INPUT[select(
  option(max100_provider_tracking, '最多 100 筆，模擬 Provider 可產生物流單號（建議）'),
  option(max50_manual_tracking, '最多 50 筆，每筆必須人工提供物流單號'),
  option(no_batch_limit, '不限制批次量，物流單號可空白'),
  option(custom_only, '完全採自主方案')
):q15_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充批次上限、物流單號格式、重複或既有物流單處理')):q15_custom]
```

### 16. DEC-P100｜批次出貨結果匯出格式

部分成功時需要讓管理員帶走逐筆成功與失敗原因，方便修正後重試。

> [!tip] 建議
> 畫面立即顯示摘要與逐筆結果，並可下載 UTF-8 CSV；至少包含輸入列號、訂單編號、結果、錯誤碼、訊息及物流單號。

```meta-bind
INPUT[select(
  option(ui_and_utf8_csv_result, '畫面結果＋UTF-8 CSV 下載（建議）'),
  option(ui_and_xlsx_result, '畫面結果＋XLSX 下載'),
  option(ui_only, '只在畫面顯示，不提供下載'),
  option(custom_only, '完全採自主方案')
):q16_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充匯出格式、欄位、保存時間或重新下載方式')):q16_custom]
```

## C. 商品資料、門市、退貨與促銷

### 17. DEC-P101｜商品 Excel 檔案與格式版本

Excel 是 M 功能，必須避免欄位變更後舊檔案被錯誤解讀。

> [!tip] 建議
> 第一版只接受 `.xlsx` 官方模板；在固定位置保存 `templateVersion`，不支援的版本整檔拒絕並提供正確模板下載。CSV 只作匯出，不作完整商品匯入。

```meta-bind
INPUT[select(
  option(xlsx_versioned_template_only, '只接受版本化 XLSX 官方模板（建議）'),
  option(xlsx_and_csv_import, '同時支援 XLSX 與 CSV 匯入'),
  option(flexible_header_detection, '不設版本，依欄名自動猜測格式'),
  option(custom_only, '完全採自主方案')
):q17_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充檔案格式、版本位置、舊版相容或模板下載')):q17_custom]
```

### 18. DEC-P102｜商品 Excel 工作表與欄位分法

商品、SKU、規格、分類與圖片是一對多關係，單一平面列可能大量重複。

> [!tip] 建議
> 使用 Product、SKU、Specification 三張主要工作表，以穩定匯入鍵與 SKU Code 關聯；分類、品牌及規格語意鍵只能引用系統已有值，不由匯入檔隱式建立。

```meta-bind
INPUT[select(
  option(three_sheets_reference_existing_lookups, 'Product／SKU／Specification 分表，Lookup 只引用（建議）'),
  option(single_flat_sheet, '所有資料放在單一平面工作表'),
  option(sheet_per_category, '每個商品分類使用不同工作表'),
  option(custom_only, '完全採自主方案')
):q18_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充工作表、關聯鍵、必填欄位或 Lookup 建立規則')):q18_custom]
```

### 19. DEC-P103｜Excel 驗證預覽與錯誤回報

已確認匯入要完整驗證預覽及整批原子提交，仍需固定錯誤呈現。

> [!tip] 建議
> 預覽列出新增、更新、無變更與錯誤數；每個錯誤包含工作表、列、欄位、穩定錯誤碼與安全訊息，並可下載錯誤 CSV。只要有錯誤就不能提交整批。

```meta-bind
INPUT[select(
  option(full_preview_downloadable_errors_atomic, '完整預覽＋逐欄錯誤下載＋有錯即禁止提交（建議）'),
  option(partial_success_valid_rows, '顯示錯誤，但允許匯入合法列'),
  option(first_error_only, '只回第一個錯誤，修正後重新上傳'),
  option(custom_only, '完全採自主方案')
):q19_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充預覽統計、錯誤欄位、下載格式或提交條件')):q19_custom]
```

### 20. DEC-P104｜示範超商品牌與門市資料量

門市資料只為展示選擇與後台維護，不串接真實門市 API，需要控制資料量與商標表達。

> [!tip] 建議
> 第一版使用 7-ELEVEN 與全家兩個品牌，各 50 間虛構門市，共 100 間；清楚標示為專題展示資料，不暗示與品牌合作。

```meta-bind
INPUT[select(
  option(two_brands_100_fictional_stores, '兩品牌共 100 間虛構門市並加展示標示（建議）'),
  option(four_brands_200_stores, '四品牌共 200 間虛構門市'),
  option(fictional_brand_only, '只使用完全虛構的超商品牌與門市'),
  option(custom_only, '完全採自主方案')
):q20_choice]
```

```meta-bind
INPUT[textArea(placeholder('填入品牌、各品牌筆數、縣市分布或展示標示')):q20_custom]
```

### 21. DEC-P105｜退貨檢查後的庫存處置

第一版只有一般可售庫存，若直接把拆封或瑕疵品回補同一 SKU，可能把不可售商品再次賣出。

> [!tip] 建議
> 只有檢查結果為 `Resellable` 才回補實體在庫；已拆封、缺件、瑕疵、報廢或待判定一律進隔離處置，不增加可售庫存，並留下退貨入庫或隔離的 InventoryMovement。

```meta-bind
INPUT[select(
  option(resellable_only_restock_others_quarantine, '只有可重新販售才回補，其餘隔離（建議）'),
  option(open_box_same_sku_restock, '拆封但可用商品也直接回補同一 SKU'),
  option(admin_quantity_manual, '不設固定規則，由管理員手動輸入回補數量'),
  option(custom_only, '完全採自主方案')
):q21_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充檢查結果、隔離資料、折價品或庫存異動類型')):q21_custom]
```

### 22. DEC-P106｜商品特價期間重疊與優惠券疊加

核心計價已確認先套商品特價再套優惠券，但同一 SKU 的兩個特價期間重疊仍需處理。

> [!tip] 建議
> 同一 SKU 的有效特價期間不得重疊，建立或修改時直接拒絕；特價後仍可套用符合條件的單張優惠券，訂單保存兩階段折扣快照。

```meta-bind
INPUT[select(
  option(reject_overlap_coupon_after_sale, '拒絕特價重疊；特價後仍可用優惠券（建議）'),
  option(lowest_sale_wins, '允許重疊並自動取最低特價'),
  option(latest_sale_wins_no_coupon, '允許重疊取最新設定，特價品禁用優惠券'),
  option(custom_only, '完全採自主方案')
):q22_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充期間邊界、重疊優先、優惠券排除或快照欄位')):q22_custom]
```

## D. API、AI 與測試閘門

### 23. DEC-P107｜無效分頁值與排序參數格式

現有分頁規範尚未決定無效數值要拒絕或修正，也未固定排序語法。

> [!tip] 建議
> 無效 `pageNumber`／`pageSize` 回傳 400，不自動修正；排序使用可重複的 `sort=field:asc` 參數，各 Endpoint 只接受白名單欄位，至少加入不可變 ID 作同值排序。

```meta-bind
INPUT[select(
  option(reject400_repeatable_sort_whitelist, '無效值 400＋可重複 sort＋欄位白名單（建議）'),
  option(clamp_sortby_direction, '自動修正分頁＋sortBy／sortDirection'),
  option(ignore_invalid_use_default, '忽略無效參數並套用預設值'),
  option(custom_only, '完全採自主方案')
):q23_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充分頁錯誤、排序語法、空值位置或同值排序')):q23_custom]
```

### 24. DEC-P108｜API HTTP Method 與 Status 基線

Endpoint 目錄需要一致判斷建立、查詢、更新、刪除、命令與業務衝突。

> [!tip] 建議
> 查詢 GET 200、建立 POST 201、成功無內容更新／命令 204；格式與欄位驗證 400、未登入 401、無權限 403、不存在 404、狀態／併發／冪等衝突 409。背景受理才使用 202。

```meta-bind
INPUT[select(
  option(rest_baseline_400_409_202_async, 'REST 基線：201／204／400／401／403／404／409，僅非同步用 202（建議）'),
  option(always200_envelope, '所有商業結果一律 HTTP 200，由 Envelope 表示成功失敗'),
  option(use422_business_validation, '欄位格式 400、商業驗證統一 422'),
  option(custom_only, '完全採自主方案')
):q24_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 PATCH／PUT、刪除、批次、驗證或例外 Status')):q24_custom]
```

### 25. DEC-P109｜穩定業務錯誤碼命名

前端不能靠中文訊息判斷處理方式，需要固定且可跨語系的 code。

> [!tip] 建議
> 使用小寫 snake_case 的「領域＋原因」，例如 `inventory_insufficient`、`order_state_conflict`；已發布錯誤碼不得改意義或重複使用，顯示文字由前端語系資源決定。

```meta-bind
INPUT[select(
  option(domain_reason_snake_case, '領域＋原因的 snake_case 穩定碼（建議）'),
  option(uppercase_dot_codes, '大寫點分隔，例如 ORDER.STATE_CONFLICT'),
  option(numeric_codes, '只使用數字錯誤碼'),
  option(custom_only, '完全採自主方案')
):q25_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充命名格式、版本、淘汰或前端翻譯規則')):q25_custom]
```

### 26. DEC-P110｜AI 商品搜尋 Schema 的形狀

Structured Outputs 已確認，但完整欄位與缺漏表達仍未定義。

> [!tip] 建議
> 使用小型業務 Schema：用途、預算上下限、商品／整機意圖、品牌偏好與排除、必要規格、軟性偏好、既有零件及待澄清問題；欄位採明確型別與可空值，不允許 AI 產生資料庫欄名或任意運算式。

```meta-bind
INPUT[select(
  option(compact_business_schema_no_db_fields, '小型業務 Schema＋待澄清欄位，不暴露 DB 查詢（建議）'),
  option(direct_filter_schema, 'Schema 直接對應完整商品查詢與排序欄位'),
  option(freeform_key_value, '允許 AI 回傳任意 key/value 條件'),
  option(custom_only, '完全採自主方案')
):q26_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出必填／選填欄位、型別、Enum、預設值或澄清規則')):q26_custom]
```

### 27. DEC-P111｜AI 客服取得資料的工具方式

AI 只能查本人訂單與核准知識，不能直接讀資料庫或自行決定會員身分。

> [!tip] 建議
> 提供白名單只讀 Application 工具，例如取得本人訂單摘要、FAQ、退換貨政策；會員 ID 只取自後端登入內容，工具內再次授權並回傳去識別化 DTO。第一版不建立向量 RAG。

```meta-bind
INPUT[select(
  option(allowlisted_readonly_application_tools, '白名單只讀 Application 工具＋工具內再授權（建議）'),
  option(preload_all_context, '由後端一次把所有可能資料放入 Prompt'),
  option(ai_query_language, '允許 AI 產生查詢語言交由後端執行'),
  option(custom_only, '完全採自主方案')
):q27_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出工具名稱、輸入輸出、引用、授權或無資料處理')):q27_custom]
```

### 28. DEC-P112｜Prompt、Schema 與工具版本保存

AI 回答需要能追溯使用了哪個 Prompt、Schema 與工具契約，也需要安全回復舊版本。

> [!tip] 建議
> Prompt、Schema 與工具契約以版本化檔案進 Git；每次 AI Interaction 保存用途與版本 ID。已被使用的版本不可覆寫，部署設定可切回前一版。

```meta-bind
INPUT[select(
  option(source_control_immutable_versions_record_usage, 'Git 版本檔＋互動紀錄版本＋可回復（建議）'),
  option(database_editable_prompts, 'Prompt 全部存資料庫並由後台直接編輯'),
  option(code_constants_only, '只用程式碼常數，不另保存版本資訊'),
  option(custom_only, '完全採自主方案')
):q28_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充版本格式、發布、回復、Interaction 紀錄或後台編輯')):q28_custom]
```

### 29. DEC-P113｜AI 逾時、重試與格式修復

重試會增加延遲與成本，完全不重試則可能把短暫錯誤直接丟給使用者。

> [!tip] 建議
> 商品搜尋 8 秒、客服 12 秒；只對限流或暫時性服務錯誤重試一次並加入短暫退避。Schema 不合法最多做一次格式修復；仍失敗立即降級，不在同一請求無限重試。

```meta-bind
INPUT[select(
  option(search8_support12_one_retry_one_repair, '搜尋 8s／客服 12s；暫時錯誤重試一次、格式修復一次（建議）'),
  option(search6_support10_no_retry, '搜尋 6s／客服 10s，不做任何重試'),
  option(search15_support25_two_retries, '搜尋 15s／客服 25s，最多重試兩次'),
  option(custom_only, '完全採自主方案')
):q29_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各功能逾時、可重試錯誤、退避、修復或降級')):q29_custom]
```

### 30. DEC-P114｜合併至 dev 後固定執行的五條 E2E

五條流程必須同時代表商業價值與最危險整合點，不能只挑最容易的頁面操作。

> [!tip] 建議
> 固定測試：AI 需求搜尋與相容組裝、訪客結帳與庫存保留、最後庫存併發防超賣、會員 AI 客服個資隔離與降級、後台出貨→單項退貨→部分退款→報表更新。

```meta-bind
INPUT[select(
  option(five_business_risk_flows, 'AI 組裝／訪客結帳／併發庫存／AI 客服安全／售後報表（建議）'),
  option(five_customer_happy_paths, '五條都使用消費者前台成功流程'),
  option(one_flow_per_team_member, '由五位成員各自選一條模組流程'),
  option(custom_only, '完全採自主方案')
):q30_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出五條流程、必要前置資料、失敗條件或執行頻率')):q30_custom]
```

## 批次操作

> [!warning]
> 送出前請確認 30 題均已選擇方案，或已在自主輸入提供完整方案。按鈕只更新本檔 Metadata；完整性與衝突由 Codex 收束時檢查。

`BUTTON[submit-decision-batch-005,restore-draft-005]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-005
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
  - type: updateMetadata
    bindTarget: submitted_at
    evaluate: false
    value: "2026-08-11"
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "✅ 已送出本批 30 項決策；答案已保存，可交由 Codex 收束。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-005
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
