---
type: decision-interaction
batch_id: DEC-BATCH-007
title: 資料契約與開發環境定版
status: applied
submission_feedback: ✅ 本批 30 項決策已整理並寫回；DEC-P173 的寄件網域衝突已保留追蹤。
created_at: 2026-08-12
submitted_at: 2026-08-12
applied_at: 2026-08-12
decision_count: 30
decision_range: DEC-P145～DEC-P174
q01_choice: store45_105_5_home150_20
q01_custom: ""
q02_choice: one_named_backup_each
q02_custom: 我就是唯一備援
q03_choice: wed90_sun150
q03_custom: 時間一當下彈性調整
q04_choice: sunday_all_wednesday_affected_stop_merge
q04_custom: ""
q05_choice: multipart_shared_version
q05_custom: ""
q06_choice: utf8bom_comma_fixed_header
q06_custom: ""
q07_choice: separate_inventory_import
q07_custom: ""
q08_choice: bigint_internal_guid_public
q08_custom: ""
q09_choice: csharp_singular_sql_plural_pascal
q09_custom: ""
q10_choice: datetime2_3_utc_offset_external
q10_custom: ""
q11_choice: money18_2_rate9_6
q11_custom: ""
q12_choice: string_baseline_320_32_100_64_200_2048_4000
q12_custom: ""
q13_choice: restrict_default_owned_cascade
q13_custom: ""
q14_choice: single_identity_separate_profiles_schemes
q14_custom: ""
q15_choice: webp_320_800_1600_q80
q15_custom: ""
q16_choice: hashed_media_route_immutable
q16_custom: ""
q17_choice: log14d_100mb_warn2gb
q17_custom: ""
q18_choice: public_id_masked_ip_full_only_audit
q18_custom: ""
q19_choice: four_workers_single_server
q19_custom: ""
q20_choice: intent_three_types
q20_custom: ""
q21_choice: spec_array_whitelist_ops
q21_custom: ""
q22_choice: tool_result_union_safe_codes
q22_custom: ""
q23_choice: zh120_add_ja30_ko30_later
q23_custom: ""
q24_choice: terry_search_kafen_support_alex_review
q24_custom: ""
q25_choice: pr_stub_manual_real_eval
q25_custom: ""
q26_choice: ai_p95_5_10_cost_001_003
q26_custom: ""
q27_choice: taipei_midnight_iphash_browser30d
q27_custom: ""
q28_choice: two_members_one_browser_cost_only
q28_custom: ""
q29_choice: authenticated_domain
q29_custom: |-
  alex
  alexyang920528@gmail.com
q30_choice: node24_npm_lock
q30_custom: ""
---

# DEC-BATCH-007｜資料契約與開發環境定版

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題選擇一個方案，或選「完全採自主方案」並填寫自主輸入。自主輸入可以補充或取代選項。送出只保存答案，不會直接修改正式文件。

## A. 物流、分工與匯入

### 1. DEC-P145｜Provider Profile 精確安全上下限

中華郵政目前公開的一般國內包裹基線為三邊和不超過 150 cm、20 kg；既有專題超商基線為單邊 45 cm、三邊和 105 cm、5 kg。[官方國內包裹說明](https://www.post.gov.tw/post/internet/Postal/index.jsp?ID=2030101)

> [!tip] 建議
> 超商可設定範圍：單邊 1～45 cm、三邊和 3～105 cm、重量 0.1～5 kg；宅配：單邊 1～150 cm、三邊和 3～150 cm、重量 0.1～20 kg。

```meta-bind
INPUT[select(option(store45_105_5_home150_20, '超商45／105／5；宅配150／20（建議）'), option(store_fixed_home150_20, '超商完全固定；宅配150／20內可調'), option(custom_only, '完全採自主方案')):q01_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充各 Profile 單邊、三邊和、重量最小／最大值')):q01_custom]
```

### 2. DEC-P146｜各核心模組具名備援

> [!tip] 建議
> 每個核心主責指定一位不同領域備援；alex 不作所有模組唯一備援。請在自主輸入列出會員、客服檢舉、優惠金流、商品購物、共用整合的備援人。

```meta-bind
INPUT[select(option(one_named_backup_each, '每個領域一位具名備援（建議）'), option(two_shared_backups, '兩位跨領域成員共同備援'), option(leader_backup_all, '組長作所有領域備援'), option(custom_only, '完全採自主方案')):q02_choice]
```

```meta-bind
INPUT[textArea(placeholder('必填：各領域備援人，以及測試／整合責任')):q02_custom]
```

### 3. DEC-P147｜兩次整合會議時長

> [!tip] 建議
> 星期三 90 分鐘、星期日 150 分鐘；逾時議題轉成具名工作，不讓會議無限延長。

```meta-bind
INPUT[select(option(wed90_sun150, '星期三90分鐘／星期日150分鐘（建議）'), option(both120, '兩次都120分鐘'), option(wed60_sun180, '星期三60分鐘／星期日180分鐘'), option(custom_only, '完全採自主方案')):q03_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充時長、休息、逾時議題與結束條件')):q03_custom]
```

### 4. DEC-P148｜整合出席與失敗處理

> [!tip] 建議
> 星期日五人必到；星期三至少組長與受影響模組主責／備援。dev 壞掉時停止後續合併，由造成失敗者與備援當日修復或回復該次變更。

```meta-bind
INPUT[select(option(sunday_all_wednesday_affected_stop_merge, '週日全員、週三受影響者；失敗即停合併（建議）'), option(all_members_both, '兩次都五人全員必到'), option(owner_only_async_fix, '只需模組主責，失敗非同步修正'), option(custom_only, '完全採自主方案')):q04_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充必到者、請假、修復期限、回復與通知規則')):q04_custom]
```

### 5. DEC-P149｜三個 CSV 的 templateVersion 承載方式

> [!tip] 建議
> `templateVersion` 作為整次 Multipart Request 的共同欄位；三個 CSV 不各自重複版本，後端以同一版本解析並原子驗證。

```meta-bind
INPUT[select(option(multipart_shared_version, 'Multipart 共用 templateVersion（建議）'), option(each_csv_metadata_row, '每個 CSV 第一列保存版本'), option(filename_version, '版本放在三個檔名'), option(custom_only, '完全採自主方案')):q05_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充欄位名稱、版本格式、不一致與缺少版本處理')):q05_custom]
```

### 6. DEC-P150｜CSV 編碼與語法

> [!tip] 建議
> UTF-8 with BOM、逗號分隔、雙引號跳脫、固定英文 Header；空字串與 Null 不視為相同，匯入前先驗證 Header 完整一致。

```meta-bind
INPUT[select(option(utf8bom_comma_fixed_header, 'UTF-8 BOM＋逗號＋固定英文 Header（建議）'), option(utf8_no_bom_comma, 'UTF-8 無 BOM＋逗號'), option(excel_locale_detection, '自動偵測編碼與分隔符'), option(custom_only, '完全採自主方案')):q06_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充換行、引號、Null、空字串、日期與小數格式')):q06_custom]
```

### 7. DEC-P151｜商品匯入是否包含初始庫存

> [!tip] 建議
> 商品匯入不直接改庫存；初始庫存使用獨立 Inventory Import／Adjustment，必須有原因與 InventoryMovement，避免商品維護繞過庫存稽核。

```meta-bind
INPUT[select(option(separate_inventory_import, '商品與庫存分開匯入（建議）'), option(sku_contains_opening_stock, 'SKU 資料集包含初始庫存'), option(no_inventory_bulk_import, '第一版庫存只能後台逐筆調整'), option(custom_only, '完全採自主方案')):q07_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充庫存欄位、原因、權限、預覽與 InventoryMovement')):q07_custom]
```

## B. SQL Server 與資料字典

### 8. DEC-P152｜主鍵與公開識別策略

> [!tip] 建議
> 資料庫內部使用 `bigint identity`；需要對外不可猜識別的會員、訂單、案件、分享連結等另有 `uniqueidentifier PublicId`，API 不暴露連號主鍵。

```meta-bind
INPUT[select(option(bigint_internal_guid_public, 'bigint 內部鍵＋必要資源 Guid PublicId（建議）'), option(guid_all_primary_keys, '全部使用 Guid 主鍵'), option(bigint_everywhere, '全部使用 bigint 並直接作 API ID'), option(custom_only, '完全採自主方案')):q08_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充哪些 Entity 需要 PublicId、Guid 產生與唯一索引')):q08_custom]
```

### 9. DEC-P153｜Entity、資料表與欄位命名

> [!tip] 建議
> C# Entity／Property 使用單數 PascalCase；SQL Table 使用複數 PascalCase，Column 使用 PascalCase；FK 採 `{Entity}Id`，Join Table 採兩個 Entity 名稱。

```meta-bind
INPUT[select(option(csharp_singular_sql_plural_pascal, 'C#單數、SQL表複數、全部 PascalCase（建議）'), option(singular_all_pascal, 'Entity 與 SQL Table 全部單數 PascalCase'), option(sql_snake_case, 'C# PascalCase、SQL snake_case'), option(custom_only, '完全採自主方案')):q09_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充表、欄位、FK、索引、Constraint 與 Join Table 命名')):q09_custom]
```

### 10. DEC-P154｜UTC 日期時間型別與精度

SQL Server `datetime2` 可指定 0～7 位小數秒。[Microsoft datetime2](https://learn.microsoft.com/sql/t-sql/data-types/datetime2-transact-sql)

> [!tip] 建議
> 持久化 UTC 時間統一使用 `datetime2(3)`，C# 使用 UTC `DateTime`；只有必須保存原始時區偏移的外部事件另用 `datetimeoffset(3)`。

```meta-bind
INPUT[select(option(datetime2_3_utc_offset_external, 'datetime2(3) UTC；外部必要時 datetimeoffset(3)（建議）'), option(datetimeoffset_all, '所有時間使用 datetimeoffset(3)'), option(datetime2_7_utc, '所有時間使用 datetime2(7) UTC'), option(custom_only, '完全採自主方案')):q10_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充建立／更新／生效／到期與外部事件的型別規則')):q10_custom]
```

### 11. DEC-P155｜金額與比例精度

> [!tip] 建議
> 新台幣金額用 `decimal(18,2)`；百分比／折扣率用 `decimal(9,6)`；數量用 `int`，所有分攤最後一筆吸收尾差。

```meta-bind
INPUT[select(option(money18_2_rate9_6, '金額18,2／比例9,6／數量int（建議）'), option(money19_4_rate9_6, '金額19,4／比例9,6'), option(integer_twd, '新台幣全部用整數，不保留小數'), option(custom_only, '完全採自主方案')):q11_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充成本、稅、折扣、退款、分攤與四捨五入規則')):q11_custom]
```

### 12. DEC-P156｜常用字串長度基線

> [!tip] 建議
> Email 320、電話 32、姓名 100、Code 64、標題 200、URL 2048、一般說明 4000；超長內容另用明確欄位，不全面使用 nvarchar(max)。

```meta-bind
INPUT[select(option(string_baseline_320_32_100_64_200_2048_4000, '採建議字串長度基線（建議）'), option(shorter_conservative, '採較短限制：Email254、標題100、說明2000'), option(nvarchar_max_text, '除 Code 外文字多數用 nvarchar(max)'), option(custom_only, '完全採自主方案')):q12_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各欄位長度、Unicode、正規化與索引限制')):q12_custom]
```

### 13. DEC-P157｜刪除與 Cascade 基線

> [!tip] 建議
> FK 預設 Restrict；只對 Aggregate 內無獨立生命週期明細使用 Cascade。商品／會員等主資料採停用或匿名化，訂單／付款／庫存／稽核不得 Cascade 刪除。

```meta-bind
INPUT[select(option(restrict_default_owned_cascade, '預設 Restrict，只有 Owned Detail Cascade（建議）'), option(cascade_by_ef_convention, '依 EF Core Required 關聯慣例 Cascade'), option(soft_delete_everything, '所有資料一律 Soft Delete'), option(custom_only, '完全採自主方案')):q13_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充可 Cascade 關聯、Soft Delete、匿名化與歷史保留')):q13_custom]
```

### 14. DEC-P158｜Identity 與業務 Profile 對應

> [!tip] 建議
> 使用單一 ASP.NET Core Identity User Store；MemberProfile 與 AdminProfile 分開，會員／管理員 Cookie Scheme 與授權 Policy 分離，管理員以角色及 2FA 限制。

```meta-bind
INPUT[select(option(single_identity_separate_profiles_schemes, '單一 Identity Store＋分離 Profile／Cookie（建議）'), option(separate_member_admin_identity, '會員與管理員兩套 Identity Store'), option(identity_user_is_member_only, 'Identity User 直接同時承載全部業務欄位'), option(custom_only, '完全採自主方案')):q14_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 User/Profile 一對一、角色、Cookie Scheme 與管理員建立流程')):q14_custom]
```

## C. 圖片、Log 與背景工作

### 15. DEC-P159｜商品圖片縮圖尺寸與品質

> [!tip] 建議
> 產生 320、800、1600 px 三種長邊版本，保持比例、不放大原圖，WebP Quality 80；原圖只供管理與重新處理。

```meta-bind
INPUT[select(option(webp_320_800_1600_q80, '320／800／1600px，WebP Q80（建議）'), option(webp_400_1200_q85, '400／1200px，WebP Q85'), option(single_webp_1200, '只產生1200px單一版本'), option(custom_only, '完全採自主方案')):q15_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充尺寸、品質、裁切、透明背景、最大原圖與 EXIF')):q15_custom]
```

### 16. DEC-P160｜公開商品圖片路由與快取

> [!tip] 建議
> 使用 `/media/products/{publicId}/{variant}/{contentHash}.webp`，檔名含內容雜湊並設定一年 Immutable Cache；換圖產生新 URL。

```meta-bind
INPUT[select(option(hashed_media_route_immutable, '雜湊 URL＋一年 Immutable Cache（建議）'), option(api_stream_short_cache, '由 API 授權串流＋短期快取'), option(wwwroot_original_filename, 'wwwroot＋原始檔名'), option(custom_only, '完全採自主方案')):q16_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充路由、Cache-Control、404、未發布圖與 CDN 相容性')):q16_custom]
```

### 17. DEC-P161｜Log 保存與檔案上限

> [!tip] 建議
> Rolling Log 保存 14 天，單檔 100 MB 後切檔，總量警告 2 GB；Demo 前不得用人工刪除掩蓋錯誤。

```meta-bind
INPUT[select(option(log14d_100mb_warn2gb, '14天／100MB／2GB警告（建議）'), option(log30d_50mb_warn5gb, '30天／50MB／5GB警告'), option(log7d_100mb, '7天／100MB'), option(custom_only, '完全採自主方案')):q17_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充保存天數、單檔、總量、壓縮與清理失敗')):q17_custom]
```

### 18. DEC-P162｜IP 與使用者識別在 Log 的處理

> [!tip] 建議
> 一般 Log 保存 User PublicId／Admin PublicId、角色與遮蔽 IP；完整 IP 只在必要 AuditLog 中依授權保存，不記錄姓名、Email 或地址。

```meta-bind
INPUT[select(option(public_id_masked_ip_full_only_audit, '一般Log用PublicId＋遮蔽IP；完整IP限稽核（建議）'), option(full_ip_all_logs, '所有安全與HTTP Log保存完整IP'), option(no_user_or_ip_logs, '一般Log完全不保存使用者與IP'), option(custom_only, '完全採自主方案')):q18_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充遮蔽方式、AuditLog 保存、查詢權限與期限')):q18_custom]
```

### 19. DEC-P163｜Hangfire Worker 數

> [!tip] 建議
> 展示單機第一版固定 4 個 Worker，所有 Queue 共用；量測後才能調整，避免未知硬體下以 CPU 倍數開太多連線。

```meta-bind
INPUT[select(option(four_workers_single_server, '單一 Server 固定4 Workers（建議）'), option(two_workers, '固定2 Workers'), option(cpu_count_workers, '依 CPU 核心數動態設定'), option(custom_only, '完全採自主方案')):q19_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Worker 數、Queue 隔離、SQL 連線與 AI 並行限制')):q19_custom]
```

## D. AI 精確契約與治理

### 20. DEC-P164｜SearchIntent 的 intent Enum

> [!tip] 建議
> 固定 `SingleProduct`、`PrebuiltComputer`、`CustomBuild` 三類；不確定時要求澄清，不增加 `Unknown` 後直接搜尋。

```meta-bind
INPUT[select(option(intent_three_types, 'SingleProduct／PrebuiltComputer／CustomBuild（建議）'), option(intent_product_or_build, '只分 Product／Build'), option(intent_with_unknown, '三類加 Unknown 並允許搜尋'), option(custom_only, '完全採自主方案')):q20_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充完整 Enum、判定例子與不確定處理')):q20_custom]
```

### 21. DEC-P165｜requiredSpecs 結構

> [!tip] 建議
> 使用 `{ semanticKey, operator, value, unit }[]`；operator 只允許 `eq`、`gte`、`lte`、`in`，semanticKey 與 unit 由後端白名單驗證。

```meta-bind
INPUT[select(option(spec_array_whitelist_ops, '語意鍵＋白名單運算子陣列（建議）'), option(category_specific_objects, '每個分類使用不同固定物件 Schema'), option(freeform_spec_text, '規格只保留自由文字'), option(custom_only, '完全採自主方案')):q21_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充欄位、operator、value 型別、unit、最大數量與重複條件')):q21_custom]
```

### 22. DEC-P166｜AI 工具錯誤結果格式

> [!tip] 建議
> 工具永遠回傳 Result Union：`ok`、`not_found`、`forbidden`、`state_conflict`、`unavailable`；錯誤只含安全 code，不把 Exception 訊息交給模型。

```meta-bind
INPUT[select(option(tool_result_union_safe_codes, 'Result Union＋安全錯誤碼（建議）'), option(throw_tool_errors, '工具失敗直接丟 Exception 給 Adapter'), option(null_for_all_failures, '所有失敗一律回 null'), option(custom_only, '完全採自主方案')):q22_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充狀態、欄位、可重試、模型可見訊息與記錄')):q22_custom]
```

### 23. DEC-P167｜120 筆 AI 評估集語言分布

> [!tip] 建議
> M 階段 120 筆全部繁中；啟動多語系 S 時另增加日文 30、韓文 30，不稀釋既有繁中安全案例。

```meta-bind
INPUT[select(option(zh120_add_ja30_ko30_later, '先繁中120；S再加日30／韓30（建議）'), option(zh80_ja20_ko20, '現在即繁中80／日20／韓20'), option(zh_only_forever, '評估只做繁中'), option(custom_only, '完全採自主方案')):q23_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各語言數量、翻譯來源、母語審核與啟動時間')):q23_custom]
```

### 24. DEC-P168｜AI 評估標註與覆核責任

> [!tip] 建議
> terry 主標商品搜尋／相容性，kafen 主標客服／越權，alex 作第二審與發布核准；主標者不得單獨核准自己修改的 Prompt。

```meta-bind
INPUT[select(option(terry_search_kafen_support_alex_review, 'terry搜尋、kafen客服、alex覆核（建議）'), option(alex_labels_all, 'alex 負責全部標註與核准'), option(five_member_split, '五人平均分配並互相覆核'), option(custom_only, '完全採自主方案')):q24_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充各分組標註者、第二審、爭議裁決與發布核准')):q24_custom]
```

### 25. DEC-P169｜真實 OpenAI 評估的執行時機

> [!tip] 建議
> 一般 PR 只跑 Stub／安全整合測試；真實 120 筆評估由手動工作流及功能凍結前執行，需明確確認預估成本。

```meta-bind
INPUT[select(option(pr_stub_manual_real_eval, 'PR用Stub；手動／凍結前跑真實評估（建議）'), option(real_eval_every_pr, '每個 PR 都跑真實 OpenAI 評估'), option(local_manual_only, '只允許開發者本機手動執行'), option(custom_only, '完全採自主方案')):q25_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充觸發、成本確認、Secret、結果保存與失敗處理')):q25_custom]
```

### 26. DEC-P170｜AI P95 與單次成本門檻

> [!tip] 建議
> 評估環境 P95：搜尋 ≤5 秒、客服 ≤10 秒；估算平均成本：搜尋 ≤US$0.01／次、客服 ≤US$0.03／則，超出即停止自動升級並檢查 Prompt／上下文。

```meta-bind
INPUT[select(option(ai_p95_5_10_cost_001_003, '搜尋5s/$0.01；客服10s/$0.03（建議）'), option(ai_p95_7_11_cost_002_005, '搜尋7s/$0.02；客服11s/$0.05'), option(no_per_call_cost_gate, '只管總預算，不設單次成本'), option(custom_only, '完全採自主方案')):q26_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 P95、平均／P95 成本、Token、超標與模型升降級')):q26_custom]
```

### 27. DEC-P171｜AI 額度重設與訪客識別

> [!tip] 建議
> 每日額度於 Asia/Taipei 00:00 重設；訪客使用遮蔽 IP Hash＋第一方隨機 Browser ID，Browser ID 保存 30 天，不使用 Fingerprinting。

```meta-bind
INPUT[select(option(taipei_midnight_iphash_browser30d, '台北午夜＋IP Hash＋30天Browser ID（建議）'), option(utc_midnight_ip_only, 'UTC午夜＋只看IP'), option(browser_id_only, '台北午夜＋只看Browser ID'), option(custom_only, '完全採自主方案')):q27_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充時區、Hash Salt、Cookie期限、隱私提示與共用網路處理')):q27_custom]
```

### 28. DEC-P172｜Demo Allowlist 範圍

> [!tip] 建議
> 設定兩個展示會員 PublicId 與一個展示 Browser ID；只繞過 US$90 非 Demo 停用，不繞過登入、同意、權限、個資遮蔽或安全測試。

```meta-bind
INPUT[select(option(two_members_one_browser_cost_only, '兩會員＋一Browser，只繞過成本停用（建議）'), option(member_accounts_only, '只有展示會員帳號'), option(global_demo_mode, '本機 DemoMode 全部流量不受限制'), option(custom_only, '完全採自主方案')):q28_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充帳號／Browser ID、設定位置、可繞過項目與撤銷')):q28_custom]
```

## E. Email 與本機環境

### 29. DEC-P173｜Brevo 寄件者驗證方式

> [!tip] 建議
> 若團隊擁有可設定 DNS 的網域，採網域驗證；若沒有，先用 Brevo 已驗證單一寄件者完成展示。請在自主輸入提供要使用的寄件名稱與 Email／網域。

```meta-bind
INPUT[select(option(authenticated_domain, '驗證自有寄件網域（建議）'), option(verified_single_sender, '使用已驗證單一寄件者'), option(custom_only, '完全採自主方案')):q29_choice]
```

```meta-bind
INPUT[textArea(placeholder('必填：寄件名稱、Email、網域擁有情況；不得填 SMTP Key')):q29_custom]
```

### 30. DEC-P174｜Node 與套件管理器

Node.js 官方目前列 v24 為 LTS、v26 為 Current；正式專案應優先使用受支援 LTS。[Node.js Releases](https://nodejs.org/en/about/previous-releases)

> [!tip] 建議
> 使用 Node.js 24 LTS＋npm＋`package-lock.json`，根目錄 `.nvmrc`／版本檔固定 Major；前後台不得混用不同套件管理器。

```meta-bind
INPUT[select(option(node24_npm_lock, 'Node 24 LTS＋npm＋package-lock（建議）'), option(node24_pnpm_lock, 'Node 24 LTS＋pnpm＋pnpm-lock'), option(node26_current_npm, 'Node 26 Current＋npm'), option(custom_only, '完全採自主方案')):q30_choice]
```

```meta-bind
INPUT[textArea(placeholder('補充 Node 版本、套件管理器、版本檔與升級規則')):q30_custom]
```

## 批次操作

> [!warning]
> 第 2 題與第 29 題需要填入具名備援或實際寄件身分；只選方向仍不足以完成對應追蹤項目。

`BUTTON[submit-decision-batch-007,restore-draft-007]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-007
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
id: restore-draft-007
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
