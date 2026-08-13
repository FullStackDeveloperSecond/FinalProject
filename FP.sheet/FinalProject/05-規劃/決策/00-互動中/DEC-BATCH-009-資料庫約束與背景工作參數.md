---
type: decision-interaction
batch_id: DEC-BATCH-009
title: 資料庫約束與背景工作參數
status: applied
submission_feedback: ✅ 本批 30 項決策已整理並寫回正式文件。
created_at: 2026-08-12
decision_count: 30
decision_range: DEC-P205～DEC-P234
q01_choice: string_decimal_boolean_option_columns
q01_custom: ""
q02_choice: unit_definition_reference
q02_custom: ""
q03_choice: single_option_first_version
q03_custom: ""
q04_choice: explicit_taiwan_address_fields
q04_custom: ""
q05_choice: display_text_plus_versioned_json
q05_custom: ""
q06_choice: lowercase_guid_d
q06_custom: ""
q07_choice: routeable_entities_only
q07_custom: ""
q08_choice: minimal_owned_allowlist
q08_custom: ""
q09_choice: common_workbench_contract
q09_custom: ""
q10_choice: authorization_in_each_branch
q10_custom: ""
q11_choice: cursor_pagination
q11_custom: ""
q12_choice: explicit_import_state_machine
q12_custom: ""
q13_choice: normalized_columns_with_bounded_raw
q13_custom: ""
q14_choice: ten_mb_five_thousand_rows
q14_custom: ""
q15_choice: one_active_per_admin_type
q15_custom: ""
q16_choice: metadata_ninety_rows_twenty_four
q16_custom: ""
q17_choice: failed_rows_twenty_four
q17_custom: ""
q18_choice: target_quantity_with_version_check
q18_custom: ""
q19_choice: controlled_reason_codes_note_required
q19_custom: ""
q20_choice: processed_outbox_thirty_days
q20_custom: ""
q21_choice: batch_twenty_poll_five_seconds
q21_custom: ""
q22_choice: per_aggregate_ordering
q22_custom: ""
q23_choice: audit_one_year
q23_custom: ""
q24_choice: security_privacy_read_export_superadmin
q24_custom: ""
q25_choice: no_legal_hold_first_version
q25_custom: ""
q26_choice: return_409_retry_after
q26_custom: ""
q27_choice: json_summary_thirty_two_kb
q27_custom: ""
q28_choice: e_drive_finalproject_data
q28_custom: ""
q29_choice: warn_twenty_percent_or_twenty_gb
q29_custom: ""
q30_choice: nightly_two_am_case_on_mismatch
q30_custom: ""
submitted_at: 2026-08-12
applied_at: 2026-08-12
---

# DEC-BATCH-009｜資料庫約束與背景工作參數

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題可採建議選項、改選其他方案，或選「完全採自主方案」並填寫自主輸入。自主輸入也可補充既有選項。送出只保存答案，不會直接修改正式文件。

## A. 規格、快照與 PublicId

### 1. DEC-P205｜動態規格值的精確型別欄位

> [!tip] 建議
> `StringValue`、`DecimalValue`、`BooleanValue`、`OptionId` 四擇一，搭配 `CK` One-of；整數也保存 Decimal 並由規格定義限制小數位。

```meta-bind
INPUT[select(option(string_decimal_boolean_option_columns, 'String／Decimal／Boolean／Option四類（建議）'), option(add_integer_and_date_columns, '另加Integer與Date欄位'), option(separate_value_tables, '各型別獨立資料表'), option(custom_only, '完全採自主方案')):q01_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：列出型別欄位、限制與例外')):q01_custom]
```

### 2. DEC-P206｜規格單位的資料模型

> [!tip] 建議
> 建立 `MeasurementUnit` 參照資料，由規格定義引用 Unit Code；值本身不重複保存顯示文字。

```meta-bind
INPUT[select(option(unit_definition_reference, 'Unit參照資料＋Code（建議）'), option(unit_text_on_definition, '規格定義只存單位文字'), option(unit_on_each_value, '每個規格值都存單位'), option(custom_only, '完全採自主方案')):q02_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：單位、換算、多語顯示與維護方式')):q02_custom]
```

### 3. DEC-P207｜選項型規格是否支援複選

> [!tip] 建議
> 第一版每個 SKU 規格只選一個 Option；真正需要多值的標籤使用 Join Entity，不把多選塞進同一欄。

```meta-bind
INPUT[select(option(single_option_first_version, '第一版Option單選（建議）'), option(option_many_to_many, '規格值直接支援複選'), option(per_definition_cardinality, '由規格定義決定單／複選'), option(custom_only, '完全採自主方案')):q03_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：哪些規格需要複選及其查詢方式')):q03_custom]
```

### 4. DEC-P208｜訂單地址快照欄位

> [!tip] 建議
> 保存收件人、電話、Email、國家、郵遞區號、縣市、區域、地址行、超商門市 Code／名稱及配送備註；依配送方式限制必填欄位。

```meta-bind
INPUT[select(option(explicit_taiwan_address_fields, '台灣地址＋超商欄位（建議）'), option(generic_international_address, '一開始採國際通用地址'), option(single_display_address_text, '只保存完整地址文字'), option(custom_only, '完全採自主方案')):q04_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：地址欄位、必填條件與個資遮蔽')):q04_custom]
```

### 5. DEC-P209｜訂單商品規格快照格式

> [!tip] 建議
> 明確欄位保存主要顯示摘要，另存具 Schema Version 的輔助 JSON 供歷史明細重現；退款與金額不得依 JSON 重算。

```meta-bind
INPUT[select(option(display_text_plus_versioned_json, '顯示摘要＋版本化JSON（建議）'), option(snapshot_child_rows, '每個規格建立快照子列'), option(display_text_only, '只保存顯示文字'), option(custom_only, '完全採自主方案')):q05_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：快照格式、Schema版本與多語顯示')):q05_custom]
```

### 6. DEC-P210｜PublicId 的 API 字串格式

> [!tip] 建議
> 統一輸出小寫 Guid `D` 格式（含連字號），輸入可接受大小寫但回應正規化，OpenAPI 標示 `format: uuid`。

```meta-bind
INPUT[select(option(lowercase_guid_d, '小寫Guid D格式（建議）'), option(uppercase_guid_d, '大寫Guid D格式'), option(base32_uuid, 'Base32縮短字串'), option(custom_only, '完全採自主方案')):q06_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：格式、大小寫、URL與OpenAPI規則')):q06_custom]
```

### 7. DEC-P211｜Join Entity 的 PublicId 範圍

> [!tip] 建議
> 只有具獨立 Route、稽核查詢或生命週期的 Join Entity 配置 PublicId；純關聯以兩端內部 FK 複合唯一即可。

```meta-bind
INPUT[select(option(routeable_entities_only, '可獨立操作的Join才配置（建議）'), option(all_join_entities, '所有Join皆配置PublicId'), option(no_join_public_id, '所有Join都不配置'), option(custom_only, '完全採自主方案')):q07_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：列出需獨立識別的Join Entity')):q07_custom]
```

### 8. DEC-P212｜Cascade Delete 第一版白名單

> [!tip] 建議
> 只允許暫態且由父物件完整擁有的 CartItem、BuildListItem、ImportRow；交易、庫存、付款、附件、稽核與通知一律 Restrict 或由清理流程處理。

```meta-bind
INPUT[select(option(minimal_owned_allowlist, '三類暫態明細白名單（建議）'), option(include_order_owned_details, '另納入訂單Owned明細'), option(no_cascade_anywhere, '完全不使用Cascade'), option(custom_only, '完全採自主方案')):q08_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：逐項列出Cascade與Restrict關聯')):q08_custom]
```

## B. 統一案件工作台

### 9. DEC-P213｜工作台 View 的共通欄位

> [!tip] 建議
> 固定 CasePublicId、CaseType、CaseNumber、Title、Status、Priority、RequesterDisplay、AssigneePublicId、CreatedAtUtc、LastActivityAtUtc、SlaDueAtUtc 與 IsOverdue。

```meta-bind
INPUT[select(option(common_workbench_contract, '12個共通欄位（建議）'), option(minimal_eight_columns, '縮減為8個核心欄位'), option(domain_specific_json_extension, '共通欄＋領域JSON擴充'), option(custom_only, '完全採自主方案')):q09_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：新增、刪除或調整工作台欄位')):q09_custom]
```

### 10. DEC-P214｜工作台 View 的授權過濾位置

> [!tip] 建議
> Application 依角色建立 Query，且在每個 UNION 分支加入可見範圍條件；不得先取全量再於記憶體過濾。

```meta-bind
INPUT[select(option(authorization_in_each_branch, '每個UNION分支套授權（建議）'), option(database_row_level_security, '採SQL Row-Level Security'), option(application_after_query, '查出後由Application過濾'), option(custom_only, '完全採自主方案')):q10_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：角色、指派範圍與授權查詢方式')):q10_custom]
```

### 11. DEC-P215｜工作台分頁方式

> [!tip] 建議
> 使用 `LastActivityAtUtc DESC, CasePublicId DESC` 的 Cursor 分頁，避免案件持續更新時 Offset 重複或漏列。

```meta-bind
INPUT[select(option(cursor_pagination, '穩定Cursor分頁（建議）'), option(offset_pagination, 'Page／PageSize分頁'), option(both_modes, '同時支援兩種模式'), option(custom_only, '完全採自主方案')):q11_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：排序鍵、頁數需求與匯出例外')):q11_custom]
```

## C. 匯入 Staging 與庫存調整

### 12. DEC-P216｜ImportBatch 狀態機

> [!tip] 建議
> `Uploaded → Validating → Ready／Invalid → Committing → Committed／Failed → Expired`；Committed 不可重送，Failed 需新建批次。

```meta-bind
INPUT[select(option(explicit_import_state_machine, '完整狀態機（建議）'), option(simple_pending_completed_failed, 'Pending／Completed／Failed'), option(retry_same_failed_batch, 'Failed可在原批次重試'), option(custom_only, '完全採自主方案')):q12_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：狀態、轉移、取消與重試規則')):q12_custom]
```

### 13. DEC-P217｜ImportRow 原始值保存方式

> [!tip] 建議
> 正規化欄位與錯誤結構化保存；原始列另存有長度上限的版本化 JSON，只供預覽與除錯，禁止 Secret 與不必要個資。

```meta-bind
INPUT[select(option(normalized_columns_with_bounded_raw, '正規化欄＋有限Raw JSON（建議）'), option(normalized_columns_only, '只保存正規化欄位'), option(raw_json_only, '只保存原始JSON'), option(custom_only, '完全採自主方案')):q13_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：Raw格式、長度、錯誤結構與敏感欄位')):q13_custom]
```

### 14. DEC-P218｜單一匯入檔上限

> [!tip] 建議
> 第一版限制 10 MB、5,000 列；超過直接拒絕並提示拆檔，足以涵蓋 10,000 筆展示資料的分批維護。

```meta-bind
INPUT[select(option(ten_mb_five_thousand_rows, '10MB／5,000列（建議）'), option(twenty_mb_ten_thousand_rows, '20MB／10,000列'), option(five_mb_one_thousand_rows, '5MB／1,000列'), option(custom_only, '完全採自主方案')):q14_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：檔案大小、列數、欄數與逾時上限')):q14_custom]
```

### 15. DEC-P219｜同一管理員的並行匯入限制

> [!tip] 建議
> 同一管理員每種匯入類型只允許一個未結束批次；商品與庫存可各有一批，避免誤提交與資源耗盡。

```meta-bind
INPUT[select(option(one_active_per_admin_type, '每人每類一個Active（建議）'), option(one_active_total, '每人全系統一個Active'), option(multiple_active_batches, '允許多批並行'), option(custom_only, '完全採自主方案')):q15_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：並行數、管理員／全系統上限與衝突提示')):q15_custom]
```

### 16. DEC-P220｜成功匯入後的 Staging 保留

> [!tip] 建議
> ImportBatch 摘要與結果保存 90 天供稽核；ImportRow／Raw 仍於提交後最多 24 小時清除，正式異動由領域資料與 AuditLog 保存。

```meta-bind
INPUT[select(option(metadata_ninety_rows_twenty_four, '摘要90天、列24小時（建議）'), option(all_twenty_four_hours, '摘要與列皆24小時'), option(all_ninety_days, '摘要與列皆90天'), option(custom_only, '完全採自主方案')):q16_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：成功批次各資料保存期限')):q16_custom]
```

### 17. DEC-P221｜失敗與過期 Staging 保留

> [!tip] 建議
> Invalid、Failed、Expired 的錯誤與列資料保留 24 小時供下載修正，之後清除；批次摘要保留 90 天。

```meta-bind
INPUT[select(option(failed_rows_twenty_four, '錯誤列24小時、摘要90天（建議）'), option(failed_rows_seven_days, '錯誤列7天、摘要90天'), option(delete_immediately, '失敗立即刪除'), option(custom_only, '完全採自主方案')):q17_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：失敗、過期、未提交批次的保留與下載')):q17_custom]
```

### 18. DEC-P222｜庫存匯入輸入語意

> [!tip] 建議
> CSV 輸入盤點後的目標 OnHand；預覽計算 Adjustment Delta，提交時檢查 Balance 版本，版本變動則全批拒絕重做預覽。

```meta-bind
INPUT[select(option(target_quantity_with_version_check, '目標數量＋版本檢查（建議）'), option(delta_quantity, 'CSV直接輸入增減量'), option(support_both_modes, '檔案可選目標或增減模式'), option(custom_only, '完全採自主方案')):q18_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：數量語意、版本衝突與負庫存規則')):q18_custom]
```

### 19. DEC-P223｜庫存調整原因

> [!tip] 建議
> 使用盤點差異、損壞、遺失、退貨入庫、資料修正、其他等受控 Code；選其他時必填備註，所有批次同時記操作人與批次 PublicId。

```meta-bind
INPUT[select(option(controlled_reason_codes_note_required, '受控Code＋其他必填（建議）'), option(free_text_reason, '只使用自由文字'), option(single_import_reason, '所有匯入都只記盤點調整'), option(custom_only, '完全採自主方案')):q19_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：原因Code、必填備註與審核權限')):q19_custom]
```

## D. Outbox、稽核與冪等

### 20. DEC-P224｜已處理 Outbox 保存期限

> [!tip] 建議
> 已成功處理訊息保留 30 天後分批清除；未成功訊息不得因到期自動刪除，需告警與人工結案。

```meta-bind
INPUT[select(option(processed_outbox_thirty_days, '成功30天、失敗不自動刪（建議）'), option(processed_seven_days, '成功保留7天'), option(processed_ninety_days, '成功保留90天'), option(custom_only, '完全採自主方案')):q20_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：成功、失敗、死信與清理期限')):q20_custom]
```

### 21. DEC-P225｜Outbox Dispatcher 參數

> [!tip] 建議
> 展示單機每 5 秒輪詢、每批最多 20 筆；每批提交後再取下一批，避免長交易與通知突發。

```meta-bind
INPUT[select(option(batch_twenty_poll_five_seconds, '20筆／5秒（建議）'), option(batch_fifty_poll_ten_seconds, '50筆／10秒'), option(hangfire_trigger_per_transaction, '每次交易另觸發Dispatcher'), option(custom_only, '完全採自主方案')):q21_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：批次、輪詢、鎖定時間與Worker數')):q21_custom]
```

### 22. DEC-P226｜Outbox 訊息順序

> [!tip] 建議
> 只保證同一 Aggregate 依 OccurredAt／Id 順序；不同訂單或案件可並行，Consumer 仍必須冪等。

```meta-bind
INPUT[select(option(per_aggregate_ordering, '同Aggregate有序（建議）'), option(global_ordering, '全系統嚴格順序'), option(no_ordering_guarantee, '完全不保證順序'), option(custom_only, '完全採自主方案')):q22_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：Aggregate Key、並行與亂序處理')):q22_custom]
```

### 23. DEC-P227｜AuditLog 一般保存期限

> [!tip] 建議
> 畢業專題第一版保存 365 天，超過後由維護工作分批清除；高風險紀錄若需更久另由 Legal Hold 決策處理。

```meta-bind
INPUT[select(option(audit_one_year, '保存365天（建議）'), option(audit_one_hundred_eighty_days, '保存180天'), option(audit_indefinite, '永久保存'), option(custom_only, '完全採自主方案')):q23_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：不同事件保存期限與清除證據')):q23_custom]
```

### 24. DEC-P228｜AuditLog 查看與匯出權限

> [!tip] 建議
> SecurityAdmin、PrivacyAdmin 可依職責查詢；只有 SuperAdmin 可匯出，且每次查詢／匯出本身也寫稽核紀錄。

```meta-bind
INPUT[select(option(security_privacy_read_export_superadmin, '兩角色查詢、SuperAdmin匯出（建議）'), option(superadmin_only, '只有SuperAdmin可查詢與匯出'), option(domain_managers_read_own_scope, '各領域主管可查自己範圍'), option(custom_only, '完全採自主方案')):q24_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：角色、欄位遮蔽、查詢與匯出範圍')):q24_custom]
```

### 25. DEC-P229｜AuditLog Legal Hold

> [!tip] 建議
> 第一版不實作 Legal Hold UI，只預留 `RetentionUntilUtc`／`HoldReason` 設計點；如展示不需要法遵情境，避免增加高風險刪除流程。

```meta-bind
INPUT[select(option(no_legal_hold_first_version, '第一版不實作、保留設計點（建議）'), option(record_level_legal_hold, '實作逐筆Legal Hold'), option(case_level_legal_hold, '依案件批次Hold'), option(custom_only, '完全採自主方案')):q25_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：Hold範圍、解除權限與展示需求')):q25_custom]
```

### 26. DEC-P230｜相同冪等鍵仍在處理中的回應

> [!tip] 建議
> 不長時間等待；回 `409 Conflict`、穩定錯誤碼及短 `Retry-After`。若 Request Hash 不同，一律拒絕重用該 Key。

```meta-bind
INPUT[select(option(return_409_retry_after, '409＋Retry-After（建議）'), option(wait_then_replay, '等待完成後回放結果'), option(return_202_status_url, '202＋狀態查詢URL'), option(custom_only, '完全採自主方案')):q26_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：等待秒數、HTTP狀態與客戶端重試')):q26_custom]
```

### 27. DEC-P231｜Idempotency ResponseSummary

> [!tip] 建議
> 最多 32 KB 版本化 JSON，只保存重播所需 Status、Headers 白名單與 Response Body；超過時保存結果資源 PublicId 並重新讀取。

```meta-bind
INPUT[select(option(json_summary_thirty_two_kb, '32KB版本化JSON（建議）'), option(json_summary_sixty_four_kb, '64KB版本化JSON'), option(resource_reference_only, '只保存結果資源PublicId'), option(custom_only, '完全採自主方案')):q27_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：大小、欄位、壓縮與敏感資料規則')):q27_custom]
```

## E. 本機資料與核對排程

### 28. DEC-P232｜展示機專案外資料根目錄

> [!tip] 建議
> 在展示機使用 `E:\FinalProjectData`，由未提交 Git 的本機設定覆寫；若展示機沒有 E 槽，必須在自主輸入指定另一個絕對路徑。

```meta-bind
INPUT[select(option(e_drive_finalproject_data, 'E:\FinalProjectData（建議）'), option(c_drive_finalproject_data, 'C:\FinalProjectData'), option(local_app_data, '使用者LocalAppData專案目錄'), option(custom_only, '完全採自主方案')):q28_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：展示機的精確絕對路徑與磁碟限制')):q28_custom]
```

### 29. DEC-P233｜備份磁碟空間告警門檻

> [!tip] 建議
> 可用空間低於 20% 或 20 GB 任一條件即告警並禁止自動清除未過期備份；Demo 前健康檢查必須顯示結果。

```meta-bind
INPUT[select(option(warn_twenty_percent_or_twenty_gb, '低於20%或20GB告警（建議）'), option(warn_ten_percent_or_ten_gb, '低於10%或10GB告警'), option(fixed_five_gb, '只以5GB固定門檻'), option(custom_only, '完全採自主方案')):q29_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：門檻、阻擋條件與通知方式')):q29_custom]
```

### 30. DEC-P234｜每日庫存核對時間與差異處理

> [!tip] 建議
> 每日 02:00（Asia/Taipei）核對；任何差異建立 InventoryReconciliationCase、標示 Critical 並通知 InventoryManager，不自動更改 Balance。

```meta-bind
INPUT[select(option(nightly_two_am_case_on_mismatch, '每日02:00＋建立差異案件（建議）'), option(nightly_midnight_case_on_mismatch, '每日00:00＋建立差異案件'), option(on_startup_and_manual_only, '啟動與人工觸發才核對'), option(custom_only, '完全採自主方案')):q30_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：時區、時間、容許差異、案件與修正流程')):q30_custom]
```

## 批次操作

`BUTTON[submit-decision-batch-009,restore-draft-009]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-009
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
id: restore-draft-009
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
