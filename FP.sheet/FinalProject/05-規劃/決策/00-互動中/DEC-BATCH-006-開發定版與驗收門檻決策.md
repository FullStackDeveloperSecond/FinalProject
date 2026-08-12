---
type: decision-interaction
batch_id: DEC-BATCH-006
title: 開發定版與驗收門檻決策
status: applied
submission_feedback: ✅ 本批 30 項決策已寫回正式文件；DEC-P120 精確數值保留待確認。
created_at: 2026-08-12
submitted_at: 2026-08-12
applied_at: 2026-08-12
decision_snapshot: "[[05-規劃/決策/02-已寫回/DEC-BATCH-006-開發定版與驗收門檻決策]]"
decision_count: 30
decision_range: DEC-P115～DEC-P144
q01_choice: core_owner_plus_backup
q01_custom: |-
  alex : 我，組長，共用相關
  haru : 會員相關
  kafen : 客服檢舉相關
  yinyin : 優惠金流相關
  terry : 商品購物相關
q02_choice: custom_only
q02_custom: |-
  改成平日一次假日一次
  平日 星期三 早上10.
  假日 星期日 早上11.
q03_choice: progressive_rehearsal_schedule
q03_custom: ""
q04_choice: three_upload_slots
q04_custom: ""
q05_choice: current_and_previous_template
q05_custom: ""
q06_choice: separate_profile_bounds
q06_custom: 你有什麼建議
q07_choice: hard_block_adjustable_warning_margin
q07_custom: ""
q08_choice: psu_ladder_450_to_1500
q08_custom: ""
q09_choice: luna_search_terra_support_luna_summary
q09_custom: ""
q10_choice: responses_api
q10_custom: ""
q11_choice: alias_dev_snapshot_freeze
q11_custom: ""
q12_choice: eight_purpose_enum
q12_custom: ""
q13_choice: required_by_intent_max_two_questions
q13_custom: ""
q14_choice: four_tools_structured_citations
q14_custom: ""
q15_choice: eval_120_balanced
q15_custom: ""
q16_choice: ai_threshold_98_90_95_safety100
q16_custom: ""
q17_choice: openapi_typescript_fetch
q17_custom: ""
q18_choice: generate_diff_typecheck_gate
q18_custom: ""
q19_choice: four_domain_queues
q19_custom: ""
q20_choice: retry_by_job_class_3_2_0
q20_custom: ""
q21_choice: superadmin_readonly_dashboard
q21_custom: ""
q22_choice: customer_four_admin_two_latest2
q22_custom: ""
q23_choice: p95_read1_write2_report3
q23_custom: ""
q24_choice: concurrency_20_checkout_50_read_10_admin
q24_custom: ""
q25_choice: daily_backup_rpo24_rto2
q25_custom: ""
q26_choice: backend70_frontend60_risk_gate
q26_custom: ""
q27_choice: order_manager_store_write
q27_custom: ""
q28_choice: closed180_orphan24h_legal_hold
q28_custom: ""
q29_choice: local_storage_webp_service_abstraction
q29_custom: ""
q30_choice: serilog_json_live_ready
q30_custom: ""
---

# DEC-BATCH-006｜開發定版與驗收門檻決策

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題選一個方案；若選「完全採自主方案」，請在自主輸入填入完整規則。即使選了建議，也可以用自主輸入補充或覆蓋部分內容。送出只保存答案，不會直接修改正式文件。

本批處理成員分工、資料契約、OpenAI、OpenAPI、Hangfire、效能、備份、附件及監控等開發前置阻塞。涉及現行產品能力的建議已附官方來源；模型與工具仍以你的選擇為準。

## A. 團隊、排程與資料匯入

### 1. DEC-P115｜五位成員的主責與備援

> [!tip] 建議
> 採「每人一個核心主責＋一個跨模組備援」，組長主責架構、整合、共用與文件；請在自主輸入填入五位姓名或代號及分工，否則本題無法真正定案。

```meta-bind
INPUT[select(
  option(core_owner_plus_backup, '每人核心主責＋跨模組備援；組長負責整合（建議）'),
  option(pair_ownership, '兩人一組共同負責模組，剩餘一人統整'),
  option(task_pool, '不固定模組，從共用工作池領取'),
  option(custom_only, '完全採自主方案')
):q01_choice]
```

```meta-bind
INPUT[textArea(placeholder('必填：五位姓名／代號；各自主責、備援、測試與整合責任')):q01_custom]
```

### 2. DEC-P116｜每週三次固定整合時段

> [!tip] 建議
> 兩次平日短整合＋一次週末長整合，每次都執行 dev 建置、Migration、核心測試與衝突處理；請在自主輸入填入實際星期與時間。

```meta-bind
INPUT[select(
  option(two_weekdays_one_weekend, '兩次平日短整合＋一次週末長整合（建議）'),
  option(three_weekdays, '三次都安排在平日'),
  option(one_weekday_two_weekends, '一次平日＋兩次週末'),
  option(custom_only, '完全採自主方案')
):q02_choice]
```

```meta-bind
INPUT[textArea(placeholder('填入三個固定星期、開始時間、時長、必到人員與缺席處理')):q02_custom]
```

### 3. DEC-P117｜Demo 彩排頻率

> [!tip] 建議
> Day 20 起每週一次局部彩排，Day 30 起每兩天一次完整彩排，Day 36～39 每天一次；每次記錄時間、失敗點與備援。

```meta-bind
INPUT[select(
  option(progressive_rehearsal_schedule, 'Day20 每週、Day30 隔日、最後四天每日（建議）'),
  option(weekly_until_final_week, '前期每週一次，最後一週每日'),
  option(final_week_only, '只在最後一週集中彩排'),
  option(custom_only, '完全採自主方案')
):q03_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充開始日、頻率、每次時長、參與者與失敗紀錄方式')):q03_custom]
```

### 4. DEC-P118｜CSV 三邏輯資料集的封裝方式

> [!tip] 建議
> 單次上傳一個 ZIP，內含固定命名的 `products.csv`、`skus.csv`、`specifications.csv`；缺檔、重複檔或未知檔整批拒絕，三份一起預覽及原子提交。

```meta-bind
INPUT[select(
  option(zip_three_fixed_csv, '單一 ZIP＋三個固定命名 CSV（建議）'),
  option(three_upload_slots, '畫面提供三個獨立上傳欄位'),
  option(one_flat_csv, '合併成單一平面 CSV'),
  option(custom_only, '完全採自主方案')
):q04_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充檔名、編碼、分隔符、缺檔／多檔與原子提交規則')):q04_custom]
```

### 5. DEC-P119｜匯入模板版本相容策略

> [!tip] 建議
> XLSX 與 ZIP Manifest 都保存 `templateVersion`；只接受目前版本與前一版本，舊版先轉成目前內部格式再驗證，不支援版本整檔拒絕並提供新版模板。

```meta-bind
INPUT[select(
  option(current_and_previous_template, '接受目前版＋前一版，舊版先轉換（建議）'),
  option(current_only, '只接受目前版本'),
  option(all_versions_best_effort, '所有歷史版本盡量相容'),
  option(custom_only, '完全採自主方案')
):q05_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充版本位置、支援範圍、轉換、拒絕訊息與模板下載')):q05_custom]
```

### 6. DEC-P120｜物流包裹設定的可輸入安全範圍

> [!tip] 建議
> 將「超商」與「宅配」分成不同 Profile；超商預設仍為單邊 45 cm、三邊和 105 cm、5 kg，管理員只能在專題核准範圍內調整；宅配使用另一組較寬範圍。請在自主輸入填精確上下限，未填前不得定版。

```meta-bind
INPUT[select(
  option(separate_profile_bounds, '超商／宅配分開設定安全上下限（建議）'),
  option(one_universal_bound, '所有物流方式使用同一組上下限'),
  option(fixed_non_editable_limits, '第一版固定限制，不開放數值修改'),
  option(custom_only, '完全採自主方案')
):q06_choice]
```

```meta-bind
INPUT[textArea(placeholder('必填：各 Profile 的單邊、三邊和、重量最小／最大值及跨欄位規則')):q06_custom]
```

## B. 相容性與 AI 商品搜尋

### 7. DEC-P121｜相容性警告門檻

> [!tip] 建議
> 將硬性不相容固定為阻擋；只有可量化餘裕使用可調門檻，例如 PSU 建議負載餘裕低於 30% 或機殼尺寸接近上限時警告，管理員只可在核准區間調整。

```meta-bind
INPUT[select(
  option(hard_block_adjustable_warning_margin, '硬規則固定阻擋；只開放警告餘裕門檻（建議）'),
  option(all_thresholds_fixed_in_code, '阻擋與警告門檻全部固定在程式'),
  option(admin_can_change_blocking, '管理員也可修改阻擋門檻'),
  option(custom_only, '完全採自主方案')
):q07_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 PSU、尺寸、BIOS 等警告門檻、上下限與測試案例')):q07_custom]
```

### 8. DEC-P122｜PSU 常見瓦數級距

> [!tip] 建議
> 第一版級距採 450、550、650、750、850、1000、1200、1500 W；先取估算功耗＋30%，再向上取級距，且不得低於 GPU 建議瓦數。

```meta-bind
INPUT[select(
  option(psu_ladder_450_to_1500, '450／550／650／750／850／1000／1200／1500W（建議）'),
  option(psu_ladder_500_step100, '500W 起每 100W 一級'),
  option(data_driven_available_psus, '只從目前上架 PSU 的瓦數動態取下一級'),
  option(custom_only, '完全採自主方案')
):q08_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充級距、最大值、查無級距與 GPU 建議值資料來源')):q08_custom]
```

### 9. DEC-P123｜OpenAI 模型分工

OpenAI 官方目前把 GPT-5.6 Luna 定位為高量低成本、Terra 定位為品質與成本平衡、Sol 定位為複雜專業工作；三者均列出 Function Calling 與 Structured Outputs 支援。[官方模型目錄](https://developers.openai.com/api/docs/models)

> [!tip] 建議
> 搜尋解析與可選摘要用 `gpt-5.6-luna`，AI 客服用 `gpt-5.6-terra`；只有評估集證明品質不足才升級，不預設使用成本較高的 Sol。

```meta-bind
INPUT[select(
  option(luna_search_terra_support_luna_summary, 'Luna 搜尋／Terra 客服／Luna 摘要（建議）'),
  option(terra_all_ai, '所有 AI 功能統一使用 Terra'),
  option(luna_all_ai, '所有 AI 功能統一使用 Luna'),
  option(custom_only, '完全採自主方案')
):q09_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各功能模型、品質不足時升級順序、預算或禁止模型')):q09_custom]
```

### 10. DEC-P124｜OpenAI API 介面

> [!tip] 建議
> 新專案統一採 Responses API，並明確設定是否儲存；使用工具與 Structured Outputs 時只透過後端 Adapter。官方提供 [Responses API 遷移說明](https://developers.openai.com/api/docs/guides/migrate-to-responses)。

```meta-bind
INPUT[select(
  option(responses_api, '統一使用 Responses API（建議）'),
  option(chat_completions_api, '使用 Chat Completions API'),
  option(adapter_supports_both, 'Adapter 同時支援兩套 API'),
  option(custom_only, '完全採自主方案')
):q10_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 API、store 設定、資料保存、SDK 或 Adapter 邊界')):q10_custom]
```

### 11. DEC-P125｜模型版本鎖定

OpenAI 官方建議需要一致行為時使用固定模型版本並搭配 Evals。[官方相容性說明](https://platform.openai.com/docs/api-reference/backward-compatibility)

> [!tip] 建議
> 開發期可透過設定切換 Alias；進入功能凍結後，若該模型提供 Snapshot，鎖定通過評估的 Snapshot，Demo 後再另行升級。

```meta-bind
INPUT[select(
  option(alias_dev_snapshot_freeze, '開發用 Alias；凍結後鎖已評估 Snapshot（建議）'),
  option(alias_all_the_way, '開發與 Demo 都使用 Alias'),
  option(snapshot_from_day_one, '從第一天固定 Snapshot'),
  option(custom_only, '完全採自主方案')
):q11_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充鎖版日、設定名稱、Snapshot 不可用時策略與升級流程')):q11_custom]
```

### 12. DEC-P126｜SearchIntent 的用途 Enum

> [!tip] 建議
> 第一版用途固定為 Gaming、VideoEditing、ThreeDRendering、GraphicDesign、Office、Programming、Streaming、General；允許多選，未知內容放入待澄清，不讓模型新增 Enum。

```meta-bind
INPUT[select(
  option(eight_purpose_enum, '八項固定用途 Enum，可多選（建議）'),
  option(four_broad_purposes, '只保留遊戲／創作／辦公／一般四類'),
  option(free_text_purpose, '用途保留自由文字，不使用 Enum'),
  option(custom_only, '完全採自主方案')
):q12_choice]
```

```meta-bind
INPUT[textArea(placeholder('列出完整用途 Enum、中英文顯示、複合用途與未知值處理')):q12_custom]
```

### 13. DEC-P127｜AI 搜尋何時必須補問

> [!tip] 建議
> 完整組裝至少需要用途與最高預算；單品至少需要商品類別或可辨識關鍵字。缺少硬性必要資訊時最多一次提出 2 個問題；只有軟性偏好缺少時直接搜尋。

```meta-bind
INPUT[select(
  option(required_by_intent_max_two_questions, '依意圖設定必要欄位，每次最多補問 2 題（建議）'),
  option(always_ask_budget_and_purpose, '所有搜尋都先問用途與預算'),
  option(never_clarify, '不補問，永遠以現有資訊搜尋'),
  option(custom_only, '完全採自主方案')
):q13_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各意圖必要欄位、最多補問次數、拒答與直接搜尋條件')):q13_custom]
```

### 14. DEC-P128｜AI 客服工具與引用格式

> [!tip] 建議
> 接受四個只讀工具：本人訂單摘要、公開 FAQ、退貨政策、公開商品詳情；引用固定包含來源類型、來源 ID、標題、版本／更新時間，由前端建立站內連結。

```meta-bind
INPUT[select(
  option(four_tools_structured_citations, '四個只讀工具＋結構化站內引用（建議）'),
  option(three_tools_no_product, '不提供商品工具，其餘三個工具＋引用'),
  option(preloaded_context_no_tools, '不使用工具，後端預先載入所有內容'),
  option(custom_only, '完全採自主方案')
):q14_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充工具名稱、輸入輸出、來源 Enum、無資料與錯誤格式')):q14_custom]
```

### 15. DEC-P129｜第一版 AI 評估資料集規模

> [!tip] 建議
> 建立 120 筆可重跑案例：搜尋新手 30、創作者 20、相容性 20、無結果／降級 15、客服政策 15、本人訂單／越權／注入 20；功能凍結後不隨意改標準答案。

```meta-bind
INPUT[select(
  option(eval_120_balanced, '120 筆平衡資料集（建議）'),
  option(eval_60_minimum, '60 筆最小資料集'),
  option(eval_200_extended, '200 筆較完整資料集'),
  option(custom_only, '完全採自主方案')
):q15_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充總數、各類分布、語言分布、標註者與凍結日期')):q15_custom]
```

### 16. DEC-P130｜AI 品質發布門檻

> [!tip] 建議
> Schema Valid ≥98%、Intent 欄位正確率 ≥90%、引用可支持率 ≥95%；隱私／越權、合法推薦與安全降級必須 100%。低於門檻不得以人工挑選成功案例取代。

```meta-bind
INPUT[select(
  option(ai_threshold_98_90_95_safety100, 'Schema98／Intent90／引用95／安全100（建議）'),
  option(ai_threshold_95_85_90_safety100, 'Schema95／Intent85／引用90／安全100'),
  option(manual_review_no_numeric, '只做人工審核，不設數值門檻'),
  option(custom_only, '完全採自主方案')
):q16_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充每項門檻、P95 延遲、單次成本、失敗時發布規則')):q16_custom]
```

## C. OpenAPI、背景工作與監控

### 17. DEC-P131｜TypeScript API Client 產生工具

`openapi-typescript` 可從 OpenAPI 產生型別，`openapi-fetch` 提供 typed fetch client；官方文件建議在 CI 執行 TypeScript typecheck。[官方文件](https://openapi-ts.dev/openapi-fetch/)

> [!tip] 建議
> 前後台統一使用 `openapi-typescript`＋`openapi-fetch`，產生型別但保留既有共用 fetch wrapper 處理 Credentials、CSRF、Correlation ID 與錯誤。

```meta-bind
INPUT[select(
  option(openapi_typescript_fetch, 'openapi-typescript＋openapi-fetch（建議）'),
  option(nswag_typescript_client, 'NSwag 產生完整 TypeScript Client'),
  option(kiota_client, 'Microsoft Kiota 產生 Client'),
  option(custom_only, '完全採自主方案')
):q17_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充工具版本、輸出路徑、前後台共用方式與 wrapper 邊界')):q17_custom]
```

### 18. DEC-P132｜OpenAPI 契約 CI 閘門

> [!tip] 建議
> CI 從 API 匯出固定 OpenAPI JSON、重新產生 TypeScript 型別，再檢查 Git Diff 必須為空並執行兩個 Vue 專案 typecheck；契約改動必須連同產生碼一起提交。

```meta-bind
INPUT[select(
  option(generate_diff_typecheck_gate, '重新產生＋Diff 為空＋雙前端 Typecheck（建議）'),
  option(typecheck_only, '只執行前端 Typecheck'),
  option(manual_generation, '只由開發者手動更新，不設 CI 檢查'),
  option(custom_only, '完全採自主方案')
):q18_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充產生命令、OpenAPI 檔位置、CI 觸發與失敗處理')):q18_custom]
```

### 19. DEC-P133｜Hangfire Queue 分組

Hangfire 支援多 Queue；使用 SQL Server Storage 時處理順序依名稱排序，不能只靠陣列順序假定優先級。[官方 Queue 文件](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html)

> [!tip] 建議
> 第一版使用 `critical`（庫存逾時、狀態推進）、`notifications`（Email／站內通知）、`maintenance`（清理與統計）、`ai`（S 摘要）四組；所有 Job 必須可冪等。

```meta-bind
INPUT[select(
  option(four_domain_queues, 'critical／notifications／maintenance／ai 四組（建議）'),
  option(two_queues_critical_default, 'critical＋default 兩組'),
  option(default_queue_only, '全部使用 default Queue'),
  option(custom_only, '完全採自主方案')
):q19_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Queue、各 Job 歸屬、Worker 數與是否允許並行')):q19_custom]
```

### 20. DEC-P134｜Hangfire 重試政策

Hangfire 預設自動重試 10 次且延遲增加；官方允許用 AutomaticRetry 覆寫。[官方例外處理文件](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)

> [!tip] 建議
> 不沿用全域 10 次：Email／暫時外部錯誤 3 次、清理工作 2 次、商業狀態衝突 0 次；失敗後保留 Failed 狀態、告警並允許授權管理員人工重試。

```meta-bind
INPUT[select(
  option(retry_by_job_class_3_2_0, '依 Job 類型採 3／2／0 次（建議）'),
  option(global_three_retries, '所有 Job 統一重試 3 次'),
  option(hangfire_default_ten, '維持 Hangfire 預設 10 次'),
  option(custom_only, '完全採自主方案')
):q20_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各 Job 次數、退避、不可重試錯誤、告警與人工重試')):q20_custom]
```

### 21. DEC-P135｜Hangfire Dashboard 權限

官方指出 Dashboard 會暴露方法與序列化參數，且可重試、刪除或觸發工作，必須限制；也可設唯讀模式。[官方 Dashboard 文件](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html)

> [!tip] 建議
> 只有完成管理員 TOTP 的 `SuperAdmin` 可進入；預設唯讀，人工重試另走具理由與稽核的系統管理 API，不直接開放 Dashboard 寫入。

```meta-bind
INPUT[select(
  option(superadmin_readonly_dashboard, '僅 SuperAdmin＋TOTP，Dashboard 唯讀（建議）'),
  option(superadmin_full_dashboard, '僅 SuperAdmin＋TOTP，可直接操作 Dashboard'),
  option(relevant_roles_readonly, '相關管理角色可唯讀，SuperAdmin 可操作'),
  option(custom_only, '完全採自主方案')
):q21_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充角色、唯讀、人工重試入口、參數遮蔽與稽核')):q21_custom]
```

### 22. DEC-P136｜瀏覽器支援範圍

> [!tip] 建議
> 消費者前台支援最新版與前一版 Chrome、Edge、Firefox、Safari；管理後台只支援最新版與前一版 Chrome、Edge。手機至少驗證 iOS Safari 與 Android Chrome 兩個常見寬度。

```meta-bind
INPUT[select(
  option(customer_four_admin_two_latest2, '前台四瀏覽器 latest-2；後台 Chrome／Edge latest-2（建議）'),
  option(chrome_edge_only, '前後台都只支援 Chrome／Edge'),
  option(evergreen_latest_only, '所有瀏覽器只驗證最新版'),
  option(custom_only, '完全採自主方案')
):q22_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充瀏覽器版本、iOS／Android、最低寬度與不支援提示')):q22_custom]
```

### 23. DEC-P137｜本機展示效能門檻

> [!tip] 建議
> 10,000 筆資料下：一般商品／訂單列表 P95 ≤1 秒、非 AI 寫入 P95 ≤2 秒、七個報表 P95 ≤3 秒；AI 延遲沿用搜尋 8 秒、客服 12 秒逾時。

```meta-bind
INPUT[select(
  option(p95_read1_write2_report3, 'P95：讀1s／寫2s／報表3s（建議）'),
  option(p95_read2_write3_report5, 'P95：讀2s／寫3s／報表5s'),
  option(no_numeric_demo_observation, '只以 Demo 人工觀察，不設數值'),
  option(custom_only, '完全採自主方案')
):q23_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充端點、P95／P99、資料量、冷啟動與量測工具')):q23_custom]
```

### 24. DEC-P138｜併發驗收負載

> [!tip] 建議
> 至少測 20 個同時結帳競爭同一 SKU、50 個同時讀取商品／訂單列表，以及 10 個管理員同時執行不同後台操作；資料必須不超賣、不重複及不遺失更新。

```meta-bind
INPUT[select(
  option(concurrency_20_checkout_50_read_10_admin, '20 結帳／50 讀取／10 後台（建議）'),
  option(concurrency_10_checkout_20_read, '10 結帳／20 讀取'),
  option(concurrency_50_checkout_100_read, '50 結帳／100 讀取'),
  option(custom_only, '完全採自主方案')
):q24_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充並行數、持續時間、成功率、資料不變量與量測環境')):q24_custom]
```

### 25. DEC-P139｜本機展示備份與復原目標

> [!tip] 建議
> 每日完整備份，重大 Migration／重設 Demo 資料前再備份；目標 RPO 24 小時、RTO 2 小時。種子資料仍以可重建為主，備份保護手工設定與展示前狀態。

```meta-bind
INPUT[select(
  option(daily_backup_rpo24_rto2, '每日＋重大操作前；RPO24h／RTO2h（建議）'),
  option(each_integration_rpo8_rto1, '每次整合備份；RPO8h／RTO1h'),
  option(seed_only_no_backup, '只靠 Migration＋Seed 重建，不做資料庫備份'),
  option(custom_only, '完全採自主方案')
):q25_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充頻率、保留份數、RPO、RTO、備份路徑與還原驗證')):q25_custom]
```

### 26. DEC-P140｜程式碼覆蓋率門檻

> [!tip] 建議
> Domain＋Application 行覆蓋率至少 70%；前端核心 Composable／Store 至少 60%。覆蓋率只作底線，高風險情境測試仍是必要閘門，不以提高數字取代有效案例。

```meta-bind
INPUT[select(
  option(backend70_frontend60_risk_gate, '後端70%／前端核心60%＋高風險必測（建議）'),
  option(global60, '前後端全域 60%'),
  option(no_coverage_threshold, '不設覆蓋率，只檢查案例'),
  option(custom_only, '完全採自主方案')
):q26_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Line／Branch、排除檔、下降容許與 CI 閘門')):q26_custom]
```

## D. 權限、檔案與健康檢查

### 27. DEC-P141｜示範超商門市維護角色

> [!tip] 建議
> `OrderManager` 與 `SuperAdmin` 可新增、編輯及停用門市；CatalogManager 只讀，因門市屬履約設定而非商品分類。所有異動保存 RowVersion 與 AuditLog。

```meta-bind
INPUT[select(
  option(order_manager_store_write, 'OrderManager／SuperAdmin 可寫；CatalogManager 唯讀（建議）'),
  option(catalog_and_order_write, 'CatalogManager 與 OrderManager 都可寫'),
  option(superadmin_only_store_write, '只有 SuperAdmin 可寫'),
  option(custom_only, '完全採自主方案')
):q27_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各角色 View／Create／Edit／Disable 與稽核要求')):q27_custom]
```

### 28. DEC-P142｜案件附件保存與清理

> [!tip] 建議
> 客服、檢舉、退貨附件在案件結束後保存 180 天；上傳暫存與孤兒檔 24 小時清除。每日排程刪除，失敗最多重試 2 次並記錄，法律／爭議保留旗標可暫停刪除。

```meta-bind
INPUT[select(
  option(closed180_orphan24h_legal_hold, '結束後180天／孤兒24h／支援保留旗標（建議）'),
  option(closed365_orphan7d, '結束後365天／孤兒7天'),
  option(delete_with_case_immediately, '案件刪除時附件立即刪除'),
  option(custom_only, '完全採自主方案')
):q28_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各領域期限、孤兒定義、排程、重試、保留旗標與刪除紀錄')):q28_custom]
```

### 29. DEC-P143｜商品圖片儲存策略

> [!tip] 建議
> 第一版使用本機檔案系統＋`IImageStorage`；原圖存於專案外資料目錄，產生 WebP 縮圖與不可猜檔名，只公開核准圖片的讀取路由／靜態映射，資料庫保存來源、授權與中繼資料。

```meta-bind
INPUT[select(
  option(local_storage_webp_service_abstraction, '本機儲存＋WebP 縮圖＋IImageStorage（建議）'),
  option(wwwroot_direct_files, '直接存放 wwwroot 並以靜態檔案公開'),
  option(database_binary, '圖片二進位內容存 SQL Server'),
  option(custom_only, '完全採自主方案')
):q29_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充路徑、尺寸、格式、品質、公開方式、刪除、備份與授權欄位')):q29_custom]
```

### 30. DEC-P144｜Logging 與 Health Check 方案

Serilog.AspNetCore 可產生結構化 Request Log；ASP.NET Core 10 內建 Health Checks 可分離 Liveness 與 Readiness，並可對 Endpoint 授權。[Serilog 官方專案](https://github.com/serilog/serilog-aspnetcore)、[Microsoft Health Checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)

> [!tip] 建議
> Serilog 結構化 JSON＋每日 Rolling File＋Console；內建 `/health/live` 只檢查程序，`/health/ready` 檢查 SQL Server 與必要本機依賴。公開回應只顯示狀態，詳細資訊限管理員或本機腳本。

```meta-bind
INPUT[select(
  option(serilog_json_live_ready, 'Serilog JSON＋Rolling File＋Live／Ready（建議）'),
  option(builtin_logging_single_health, '只用內建 Logging＋單一 /health'),
  option(serilog_seq_local, 'Serilog＋本機 Seq＋Live／Ready'),
  option(custom_only, '完全採自主方案')
):q30_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Sink、保留天數、敏感欄位、Health 依賴、權限與告警')):q30_custom]
```

## 批次操作

> [!warning]
> 第 1、2、6 題需要具體姓名、時段或數值；只選方向但未填自主輸入時，Codex 收束後仍可能保留補充決策。

`BUTTON[submit-decision-batch-006,restore-draft-006]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-006
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
  - type: updateMetadata
    bindTarget: submitted_at
    evaluate: false
    value: "2026-08-12"
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "✅ 已送出本批 30 項決策；答案已保存，可交由 Codex 收束。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-006
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
