---
type: decision-interaction
batch_id: DEC-BATCH-002
title: 第二批核心決策
status: drafting
created_at: 2026-08-09
submitted_at:
decision_count: 30
decision_range: DEC-P11～DEC-P40
q01_choice: protected_semantic_keys
q01_custom: ""
q02_choice: validate_then_atomic
q02_custom: ""
q03_choice: immutable_sku_code
q03_custom: ""
q04_choice: ""
q04_custom: ""
q05_choice: ""
q05_custom: ""
q06_choice: ""
q06_custom: ""
q07_choice: ""
q07_custom: ""
q08_choice: ""
q08_custom: ""
q09_choice: ""
q09_custom: ""
q10_choice: ""
q10_custom: ""
q11_choice: ""
q11_custom: ""
q12_choice: ""
q12_custom: ""
q13_choice: ""
q13_custom: ""
q14_choice: ""
q14_custom: ""
q15_choice: ""
q15_custom: ""
q16_choice: ""
q16_custom: ""
q17_choice: ""
q17_custom: ""
q18_choice: ""
q18_custom: ""
q19_choice: ""
q19_custom: ""
q20_choice: ""
q20_custom: ""
q21_choice: ""
q21_custom: ""
q22_choice: ""
q22_custom: ""
q23_choice: ""
q23_custom: ""
q24_choice: ""
q24_custom: ""
q25_choice: ""
q25_custom: ""
q26_choice: ""
q26_custom: ""
q27_choice: ""
q27_custom: ""
q28_choice: ""
q28_custom: ""
q29_choice: ""
q29_custom: ""
q30_choice: ""
q30_custom: ""
---

# DEC-BATCH-002｜第二批核心決策

目前狀態：`VIEW[{status}]`

> [!important] 填寫規則
> 每題選擇一個方案，或選擇「完全採自主方案」並填寫自主輸入。自主輸入也可以補充既有選項。填完 30 題後按頁尾的「送出本批 30 項決策」。送出只會改變本文件狀態，不會修改正式需求文件。

## A. 商品、規格與相容性

### 1. DEC-P11｜動態規格與固定相容性程式的銜接方式

DEC-P02 允許管理員動態編輯所有規格欄位，但 DEC-P03 又要求相容性規則程式固定。需要決定固定程式如何穩定找到 Socket、TDP、GPU 長度等欄位。

> [!tip] 建議
> 採用「受保護語意鍵」：所有欄位仍可在後台維護顯示名稱、排序與啟用狀態，但被相容性程式引用的語意鍵與資料型別不可直接刪除或變更。實作與測試成本最低。

```meta-bind
INPUT[select(
  option(protected_semantic_keys, '受保護語意鍵（建議）'),
  option(admin_field_mapping, '管理員將動態欄位映射到相容性角色'),
  option(dynamic_rule_engine, '改為完整動態規則引擎'),
  option(custom_only, '完全採自主方案')
):q01_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充限制，或輸入完整自主方案')):q01_custom]
```

### 2. DEC-P12｜Excel 匯入的交易方式

批次匯入可能同時包含數百筆商品與 SKU；其中一筆失敗時，要決定其他資料是否寫入。

> [!tip] 建議
> 先完整驗證並顯示預覽，確認後採整批原子提交；任一筆失敗則全部不寫入。資料最容易復原，也適合五人同時開發時維持一致性。

```meta-bind
INPUT[select(
  option(validate_then_atomic, '完整驗證、預覽後整批提交（建議）'),
  option(partial_success, '合法資料寫入，錯誤資料輸出報告'),
  option(row_by_row, '逐列立即建立或更新'),
  option(custom_only, '完全採自主方案')
):q02_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充錯誤處理，或輸入完整自主方案')):q02_custom]
```

### 3. DEC-P13｜Excel 建立與更新的資料識別方式

必須避免同名商品、相似規格或人工改名造成錯誤覆蓋。

> [!tip] 建議
> 使用不可變的 SKU Code 精確更新；空白 SKU Code 視為新增；重複或不存在但標示更新的代碼整批拒絕。

```meta-bind
INPUT[select(
  option(immutable_sku_code, '不可變 SKU Code 精確比對（建議）'),
  option(name_and_spec_match, '以商品名稱與規格組合比對'),
  option(import_source_id, '另建外部匯入識別碼映射'),
  option(custom_only, '完全採自主方案')
):q03_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充新增與更新規則')):q03_custom]
```

### 4. DEC-P14｜已使用規格欄位的刪除與變更

規格欄位可能已被商品、Excel 模板、搜尋篩選或相容性規則使用。

> [!tip] 建議
> 已被使用的欄位只能停用；穩定 Key 與資料型別不可變，顯示名稱及排序可修改。未被使用的欄位才可實體刪除。

```meta-bind
INPUT[select(
  option(disable_used_fields, '已使用欄位只能停用（建議）'),
  option(hard_delete_with_migration, '允許刪除，但必須同步遷移資料'),
  option(versioned_templates, '每次變更建立新版規格範本'),
  option(custom_only, '完全採自主方案')
):q04_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充欄位生命週期')):q04_custom]
```

### 5. DEC-P15｜相容性後台可以調整哪些內容

已決定管理員不能編寫規則程式，但可以查看、啟停設定值及測試；仍需界定可調整範圍。

> [!tip] 建議
> 管理員可啟停整條規則、調整警告門檻與測試 SKU；不能改比較運算式、欄位語意鍵或規則程式。

```meta-bind
INPUT[select(
  option(enable_threshold_test, '啟停規則、調門檻、執行測試（建議）'),
  option(test_only, '只能查看與測試，不能改任何設定'),
  option(edit_rule_parameters, '可修改所有參數但不能修改程式'),
  option(custom_only, '完全採自主方案')
):q05_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出允許或禁止調整的項目')):q05_custom]
```

### 6. DEC-P16｜BIOS 相容性深度

CPU 與主機板可能 Socket 相同，但仍需要特定 BIOS 版本。

> [!tip] 建議
> 第一版以晶片組與 CPU 世代映射做阻擋；已知可能需要更新 BIOS 時顯示警告，不維護每一張主機板的最低 BIOS 版本。

```meta-bind
INPUT[select(
  option(generation_map_with_warning, '世代映射加 BIOS 警告（建議）'),
  option(minimum_bios_version, '管理每組主機板與 CPU 的最低 BIOS 版本'),
  option(no_bios_check, '第一版不檢查 BIOS 相容性'),
  option(custom_only, '完全採自主方案')
):q06_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 BIOS 資料來源或警告規則')):q06_custom]
```

### 7. DEC-P17｜整機功耗估算與安全餘裕

需要將 CPU、GPU 及其他零件功耗轉成最低建議電源瓦數。

> [!tip] 建議
> 使用結構化功耗估值加總後保留 30% 安全餘裕，再向上取常見 PSU 瓦數級距；同時不得低於 GPU 廠商建議瓦數。

```meta-bind
INPUT[select(
  option(sum_plus_30_percent, '功耗加總＋30% 並採較高 GPU 建議值（建議）'),
  option(gpu_vendor_only, '只使用 GPU 廠商建議電源瓦數'),
  option(detailed_rail_model, '建立電源各路輸出與峰值的詳細模型'),
  option(custom_only, '完全採自主方案')
):q07_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充安全餘裕或瓦數級距')):q07_custom]
```

### 8. DEC-P18｜同一組裝清單的購買數量

組裝費已確認每台 NT$300，但仍需決定一份清單能否一次購買多台。

> [!tip] 建議
> 允許數量大於一；每台收取 NT$300、各自建立 AssemblyJob，庫存按總數原子檢查與保留。

```meta-bind
INPUT[select(
  option(multiple_with_jobs, '可多台且每台獨立 AssemblyJob（建議）'),
  option(quantity_one_only, '每份組裝清單一次只能買一台'),
  option(multiple_shared_job, '可多台但共用一筆組裝進度'),
  option(custom_only, '完全採自主方案')
):q08_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充數量上限或庫存規則')):q08_custom]
```

### 9. DEC-P19｜組裝清單分享連結期限

分享頁不含個資，每次開啟都會重新檢查價格、庫存與相容性。

> [!tip] 建議
> 預設不自動到期，由建立者撤銷；清單刪除或會員停權時失效。最容易展示，也不需要處理使用者收到過期連結的額外流程。

```meta-bind
INPUT[select(
  option(no_expiry_revocable, '不自動到期，可由建立者撤銷（建議）'),
  option(expire_30_days, '建立後 30 天到期'),
  option(expire_7_days, '建立後 7 天到期'),
  option(custom_only, '完全採自主方案')
):q09_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充期限、撤銷或失效條件')):q09_custom]
```

## B. 物流與付款限制

### 10. DEC-P20｜超商門市選擇的模擬深度

物流不串接真實服務，但結帳需要呈現可理解的門市選擇流程。

> [!tip] 建議
> 建立固定門市種子資料，支援縣市、區域、店名及店號搜尋，不做地圖。可展示真實流程感，又不增加地圖 API 成本。

```meta-bind
INPUT[select(
  option(searchable_store_dataset, '固定資料＋縣市區域與門市搜尋（建議）'),
  option(store_map, '加入模擬地圖與門市標記'),
  option(simple_dropdown, '只提供少量門市下拉選單'),
  option(custom_only, '完全採自主方案')
):q10_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充資料量、搜尋欄位或地圖需求')):q10_custom]
```

### 11. DEC-P21｜超商包裹尺寸與重量限制

電腦零件有大型及高價商品，需要在結帳前阻止不適合超取的訂單。

> [!tip] 建議
> 模擬限制採單邊不超過 45 cm、長寬高合計不超過 105 cm、重量不超過 5 kg；組裝電腦、螢幕及超限商品只能宅配。

```meta-bind
INPUT[select(
  option(limit_45_105_5, '45 cm／105 cm／5 kg，整機與螢幕排除（建議）'),
  option(category_blacklist_only, '只用商品分類限制，不計算尺寸重量'),
  option(no_store_limits, '模擬流程不限制尺寸與重量'),
  option(custom_only, '完全採自主方案')
):q11_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充數值或排除商品')):q11_custom]
```

### 12. DEC-P22｜貨到付款適用範圍

既有決策要求宅配必須先付款，因此貨到付款不能用於宅配。

> [!tip] 建議
> 只允許超商取貨付款，訂單總額上限 NT$10,000，且組裝電腦與高價限制品不適用。

```meta-bind
INPUT[select(
  option(store_cod_10000, '超商取貨付款、上限 NT$10,000 且排除組裝電腦（建議）'),
  option(store_cod_no_limit, '所有超商取貨訂單皆可使用'),
  option(remove_cod, '第一版取消貨到付款'),
  option(custom_only, '完全採自主方案')
):q12_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充金額上限或排除條件')):q12_custom]
```

## C. 客服工作台與 SLA

### 13. DEC-P23｜統一工作台與共用佇列的範圍

客服、檢舉、退貨資料已決定分開，但管理後台會統一顯示待辦。需要決定哪些案件能由一般客服自領。

> [!tip] 建議
> 統一工作台顯示三領域摘要；只有一般客服案件進自領佇列，檢舉與退貨由具權限角色在各自佇列承接。

```meta-bind
INPUT[select(
  option(unified_view_separate_queues, '統一畫面、各領域分流佇列（建議）'),
  option(self_claim_all_domains, '一般客服可自領三種領域案件'),
  option(separate_workbenches, '三個領域各自使用獨立工作台'),
  option(custom_only, '完全採自主方案')
):q13_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充跨領域權限或佇列範圍')):q13_custom]
```

### 14. DEC-P24｜客服主管的角色設計

DEC-P07 需要主管指派及轉派，但目前沒有對應後台角色。

> [!tip] 建議
> 新增 `CustomerServiceSupervisor`，擁有一般客服權限，再增加指派、轉派、SLA 升級與客服報表權限。

```meta-bind
INPUT[select(
  option(new_supervisor_role, '新增 CustomerServiceSupervisor（建議）'),
  option(super_admin_only, '只有 SuperAdmin 可以指派與轉派'),
  option(customer_service_permission, '所有 CustomerService 都可互相指派'),
  option(custom_only, '完全採自主方案')
):q14_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充主管角色或權限')):q14_custom]
```

### 15. DEC-P25｜客服優先級的判定方式

SLA 期限會依優先級計算，因此需要避免會員自行把一般問題標成緊急。

> [!tip] 建議
> 系統依案件類別與關鍵條件給預設優先級，客服可調低或調高，主管可覆核；會員不能直接選擇優先級。

```meta-bind
INPUT[select(
  option(rule_default_staff_adjust, '規則預設、客服調整、主管覆核（建議）'),
  option(member_selects, '會員建立案件時自行選擇'),
  option(staff_manual, '建立後完全由客服人工判定'),
  option(custom_only, '完全採自主方案')
):q15_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充優先級判定或升級條件')):q15_custom]
```

### 16. DEC-P26｜客服 SLA 的計時日曆

若採營業時間制，需要額外維護工作日、假日與暫停計算；若採日曆時間則較容易實作及展示。

> [!tip] 建議
> 第一版使用 24×7 日曆時間，期限直接以 UTC 時間計算；畫面再轉成台灣時間顯示。規則最清楚，也較容易測試。

```meta-bind
INPUT[select(
  option(calendar_24_7, '24×7 日曆時間（建議）'),
  option(business_hours, '週一至週五營業時間制'),
  option(hybrid_by_priority, '高優先級用 24×7，其餘用營業時間'),
  option(custom_only, '完全採自主方案')
):q16_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充時區、營業時間或假日規則')):q16_custom]
```

### 17. DEC-P27｜各優先級 SLA 期限

需要同時定義首次人工回覆期限與目標結案期限。AI 自動回答不算人工首次回覆。

> [!tip] 建議
> 採 Low 24 小時／5 天、Normal 8 小時／3 天、High 4 小時／24 小時、Urgent 1 小時／8 小時。區分明顯，Demo 也容易製造逾時案例。

```meta-bind
INPUT[select(
  option(tiered_standard, 'Low 24h/5d、Normal 8h/3d、High 4h/24h、Urgent 1h/8h（建議）'),
  option(tiered_lenient, 'Low 48h/7d、Normal 24h/5d、High 8h/2d、Urgent 2h/24h'),
  option(single_target, '所有優先級統一 24h 首回／5d 結案'),
  option(custom_only, '完全採自主方案')
):q17_choice]
```

```meta-bind
INPUT[textArea(placeholder('輸入自訂優先級期限或例外')):q17_custom]
```

### 18. DEC-P28｜SLA 暫停與重開規則

等待顧客補充期間是否繼續計時，會直接影響逾時率與客服績效。

> [!tip] 建議
> `WaitingForCustomer` 暫停結案 SLA，最多暫停 3 天；`WaitingForInternal` 不暫停。Resolved 後 3 天內重開時保留首次回覆結果並重新計算結案期限。

```meta-bind
INPUT[select(
  option(pause_customer_three_days, '等待顧客最多暫停 3 天，重開重算結案期限（建議）'),
  option(no_pause_reset_all, '所有等待都不停表，重開重算全部期限'),
  option(pause_all_waiting, '等待顧客與等待內部都暫停，重開沿用剩餘時間'),
  option(custom_only, '完全採自主方案')
):q18_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充暫停上限、重開期限或計時副作用')):q18_custom]
```

### 19. DEC-P29｜附件限制是否套用三個案件領域

目前 3 個 × 10 MB 的決策明確寫在客服附件，但檢舉證據與退貨照片也需要附件。

> [!tip] 建議
> 客服、檢舉、退貨共用相同的安全儲存服務與 3 個 × 10 MB 限制，但資料關聯仍各自獨立。

```meta-bind
INPUT[select(
  option(same_limit_all_domains, '三領域統一 3 個 × 10 MB（建議）'),
  option(more_for_report_return, '客服 3 個，檢舉與退貨各 5 個'),
  option(separate_limits, '三個領域分別制定格式與限制'),
  option(custom_only, '完全採自主方案')
):q19_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各領域格式、數量或大小')):q19_custom]
```

### 20. DEC-P30｜附件惡意檔案掃描策略

只檢查副檔名不足以阻止偽裝檔案；但引入額外防毒服務也會增加本機展示依賴。

> [!tip] 建議
> 建立 `IFileScanner`，第一版使用展示電腦的 Microsoft Defender 掃描暫存檔；同時驗證副檔名、MIME、檔案簽章並重新命名。掃描不可用或結果不明時拒絕保存。

```meta-bind
INPUT[select(
  option(defender_and_validation, 'Microsoft Defender＋格式驗證（建議）'),
  option(validation_only, '只驗證副檔名、MIME 與檔案簽章'),
  option(clamav_service, '安裝並使用本機 ClamAV 服務'),
  option(custom_only, '完全採自主方案')
):q20_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充掃描器、失敗行為或隔離流程')):q20_custom]
```

## D. 報表與資料口徑

### 21. DEC-P31｜商品成本與成交成本快照

毛利分析需要歷史成本；若使用目前 SKU 成本，修改成本後舊訂單毛利也會改變。

> [!tip] 建議
> SKU 保存目前單位成本；建立訂單時將單位成本快照到 OrderItem。報表只用完成訂單的成交成本快照，退貨與退款再依退款明細調整。

```meta-bind
INPUT[select(
  option(order_item_cost_snapshot, 'SKU 現行成本＋OrderItem 成本快照（建議）'),
  option(current_sku_cost, '報表直接使用查詢當下的 SKU 成本'),
  option(monthly_average_cost, '建立每月移動平均成本表'),
  option(custom_only, '完全採自主方案')
):q21_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充成本來源、快照時間或退款歸屬')):q21_custom]
```

### 22. DEC-P32｜七個報表的計算與更新方式

展示資料約一萬筆，在單機 SQL Server 上可以直接彙總，但仍需決定是否建立快取或預先計算表。

> [!tip] 建議
> 第一版依查詢條件即時計算並建立必要索引，不做報表快取；若實測超過效能門檻再加入短期快取。

```meta-bind
INPUT[select(
  option(realtime_sql_first, '即時 SQL 彙總，測量後再決定快取（建議）'),
  option(five_minute_cache, '所有報表使用 5 分鐘應用程式快取'),
  option(precomputed_daily, '建立每日報表彙總表與背景更新'),
  option(custom_only, '完全採自主方案')
):q22_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充更新頻率、效能門檻或快取規則')):q22_custom]
```

### 23. DEC-P33｜商品 ABC 分級口徑

附件提案以累計營收占比 80%／95% 分級，但需確認訂單、退款、日期與同值排序。

> [!tip] 建議
> 依所選期間「完成訂單扣除成功退款後的 SKU 淨營收」由高到低排列；累計占比 ≤80% 為 A、≤95% 為 B，其餘 C；同營收以 SKU Code 排序。

```meta-bind
INPUT[select(
  option(net_revenue_80_95, 'SKU 淨營收累計 80%／95%（建議）'),
  option(quantity_80_95, '依銷售數量累計 80%／95%'),
  option(gross_profit_80_95, '依毛利貢獻累計 80%／95%'),
  option(custom_only, '完全採自主方案')
):q23_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充分級門檻、退款或排序規則')):q23_custom]
```

### 24. DEC-P34｜關聯組合分析的層級與門檻

若顯示樣本太少的商品組合，Lift 可能很高但沒有商業意義。

> [!tip] 建議
> 以完成訂單的 SKU 配對分析，至少共同出現 5 筆訂單、Support ≥1%、Confidence ≥20%、Lift >1 才顯示。

```meta-bind
INPUT[select(
  option(sku_pair_thresholds, 'SKU 配對＋5 筆／1%／20%／Lift>1（建議）'),
  option(category_pairs, '只分析商品分類配對'),
  option(show_all_pairs, '顯示所有出現過的 SKU 配對'),
  option(custom_only, '完全採自主方案')
):q24_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充分析層級、最低樣本或門檻')):q24_custom]
```

### 25. DEC-P35｜銷售預測與異常偵測方法

原附件的異常公式使用變異數作除數，單位不一致，需要重新確認可實作公式。

> [!tip] 建議
> 最近 30 天使用簡單線性迴歸預測未來 7 天，負數歸零；異常使用 Z-score 並以標準差計算，絕對值大於 2 標記異常。資料不足 14 天時不產生預測。

```meta-bind
INPUT[select(
  option(linear_and_zscore, '30 天線性迴歸＋Z-score |z|>2（建議）'),
  option(moving_average_and_iqr, '7 日移動平均預測＋IQR 異常偵測'),
  option(linear_and_fixed_percent, '線性預測＋偏離平均固定百分比判定'),
  option(custom_only, '完全採自主方案')
):q25_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充期間、公式、門檻或資料不足行為')):q25_custom]
```

## E. 前後端與共用技術

### 26. DEC-P36｜Vue UI 元件庫

前台需要 RWD，後台需要大量表格、篩選、表單、對話框與圖表整合。

> [!tip] 建議
> 採用 PrimeVue。資料表、表單、日期、選單與後台元件完整，並能透過主題系統讓前後台使用不同視覺風格。

```meta-bind
INPUT[select(
  option(primevue, 'PrimeVue（建議）'),
  option(vuetify, 'Vuetify'),
  option(element_plus, 'Element Plus'),
  option(custom_only, '完全採自主方案')
):q26_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充元件庫、版本或選用理由')):q26_custom]
```

### 27. DEC-P37｜前端 Server State 管理

購物車登入合併、商品查詢、後台表格與報表都需要處理載入、快取、重抓及錯誤狀態。

> [!tip] 建議
> TanStack Query 管理 API Server State；Pinia 只保存登入狀態、UI 偏好及跨頁客戶端流程，避免把所有 API 資料手工塞進 Store。

```meta-bind
INPUT[select(
  option(tanstack_query_plus_pinia, 'TanStack Query＋Pinia 分工（建議）'),
  option(pinia_only, '所有狀態只使用 Pinia'),
  option(custom_composables, '自行以 composable 與 fetch 建立快取'),
  option(custom_only, '完全採自主方案')
):q27_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充狀態責任邊界或套件')):q27_custom]
```

### 28. DEC-P38｜HTTP Client 與 TypeScript 型別產生

兩個 Vue 應用都要呼叫同一套 API，手寫重複 DTO 容易和 ASP.NET Core Contract 不一致。

> [!tip] 建議
> 使用 OpenAPI 產生 TypeScript 型別與 typed fetch client；產生碼不可手改，客製錯誤處理、認證與 Correlation ID 放在共用 wrapper。

```meta-bind
INPUT[select(
  option(openapi_typescript_fetch, 'OpenAPI TypeScript＋typed fetch wrapper（建議）'),
  option(axios_orval, 'Axios＋Orval 產生 Client'),
  option(nswag_client, 'NSwag 產生 TypeScript Client'),
  option(custom_only, '完全採自主方案')
):q28_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充產生工具、命令或客製碼邊界')):q28_custom]
```

### 29. DEC-P39｜真實 Email 寄送服務

Email 驗證、忘記密碼及訂單通知必須真的寄送，但專案只在本機展示。

> [!tip] 建議
> Application 定義 `IEmailSender`，第一版 Infrastructure 使用 Brevo SMTP；開發環境另提供不寄送的本機實作，秘密放 User Secrets。

```meta-bind
INPUT[select(
  option(brevo_smtp, 'Brevo SMTP＋IEmailSender（建議）'),
  option(resend_api, 'Resend API＋IEmailSender'),
  option(gmail_smtp, 'Gmail SMTP＋App Password'),
  option(custom_only, '完全採自主方案')
):q29_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充服務商、測試帳號或失敗處理')):q29_custom]
```

### 30. DEC-P40｜背景工作與重試方案

庫存逾時釋放、Email、通知、AI 清除與報表工作需要持久化、重試及人工查看失敗。

> [!tip] 建議
> 使用 Hangfire 搭配 SQL Server。可持久化工作、設定重試、排程逾時取消，並提供 Dashboard 方便 Demo 與除錯。

```meta-bind
INPUT[select(
  option(hangfire_sqlserver, 'Hangfire＋SQL Server（建議）'),
  option(quartz_net, 'Quartz.NET＋自建工作紀錄'),
  option(background_service, 'ASP.NET Core BackgroundService＋自建資料表'),
  option(custom_only, '完全採自主方案')
):q30_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充背景工作套件、持久化或重試策略')):q30_custom]
```

## 批次操作

> [!warning]
> 送出前請確認 30 題均已選擇方案，或已在自主輸入提供完整方案。Meta Bind 按鈕只更新本檔 `status`，不會驗證答案，也不會修改正式文件；完整性由 Codex 收束時檢查。

`BUTTON[submit-decision-batch,restore-draft]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: drafting
```
