---
type: decision-interaction
batch_id: DEC-BATCH-008
title: 正規化與資料實作邊界
status: applied
submission_feedback: ✅ 本批 30 項決策已整理並寫回正式文件。
created_at: 2026-08-12
decision_count: 30
decision_range: DEC-P175～DEC-P204
q01_choice: third_normal_form_controlled_exceptions
q01_custom: ""
q02_choice: typed_value_columns_oneof
q02_custom: ""
q03_choice: explicit_join_entities
q03_custom: ""
q04_choice: member_address_order_snapshot
q04_custom: ""
q05_choice: owned_columns_on_order_entities
q05_custom: ""
q06_choice: coupon_allocation_snapshots
q06_custom: ""
q07_choice: version_fk_and_exact_snapshot
q07_custom: ""
q08_choice: movement_source_balance_transactional_daily_check
q08_custom: ""
q09_choice: sql_union_view_keyless
q09_custom: ""
q10_choice: derive_summary_in_query
q10_custom: ""
q11_choice: measure_then_approve_projection
q11_custom: ""
q12_choice: snapshot_rebuildable_with_asof
q12_custom: ""
q13_choice: relational_core_json_auxiliary
q13_custom: ""
q14_choice: public_id_external_resource_list
q14_custom: ""
q15_choice: app_uuid_v7_nonclustered
q15_custom: ""
q16_choice: profile_types_mutually_exclusive
q16_custom: ""
q17_choice: normalized_columns_filtered_unique
q17_custom: ""
q18_choice: explicit_owned_allowlist
q18_custom: ""
q19_choice: structured_audit_allowlisted_diff
q19_custom: ""
q20_choice: one_outbox_table_typed_payload
q20_custom: ""
q21_choice: scoped_idempotency_record
q21_custom: ""
q22_choice: persistent_staging_24h
q22_custom: ""
q23_choice: inventory_batch_atomic
q23_custom: ""
q24_choice: null_token_iso_decimal_dot
q24_custom: ""
q25_choice: midpoint_away_from_zero
q25_custom: ""
q26_choice: trim_nfkc_email_invariant
q26_custom: ""
q27_choice: codes_case_insensitive_unique
q27_custom: ""
q28_choice: ix_table_columns_conventions
q28_custom: ""
q29_choice: verified_single_sender_now
q29_custom: ""
q30_choice: project_data_root_daily7_weekly4
q30_custom: ""
submitted_at: 2026-08-12
applied_at: 2026-08-12
---

# DEC-BATCH-008｜正規化與資料實作邊界

目前狀態：`VIEW[{status}]`

> [!important] 使用方式
> 每題確認一個選項，或選「完全採自主方案」並填寫自主輸入。送出只保存答案，不會直接修改正式文件。

## A. 正規化與關聯模型

### 1. DEC-P175｜交易模型正規化目標

> [!tip] 建議
> 以 3NF 為交易寫入基線，只允許文件已列出的交易快照、衍生餘額與唯讀投影例外。

```meta-bind
INPUT[select(option(third_normal_form_controlled_exceptions, '3NF＋受控例外（建議）'), option(bcnf_where_possible, '核心資料盡量 BCNF'), option(pragmatic_no_target, '不指定正規化目標'), option(custom_only, '完全採自主方案')):q01_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充正規化層級、例外及審核方式')):q01_custom]
```

### 2. DEC-P176｜動態規格值實體模型

> [!tip] 建議
> 單一 `SkuSpecificationValue` 表依定義型別使用 String／Decimal／Boolean／Option 等互斥欄位，搭配 Check Constraint；核心可篩選值不放任意 JSON。

```meta-bind
INPUT[select(option(typed_value_columns_oneof, '單表型別欄位＋One-of限制（建議）'), option(separate_table_per_type, '每種型別獨立資料表'), option(json_value_document, '規格值主要保存 JSON'), option(custom_only, '完全採自主方案')):q02_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充型別欄位、單位、Option、索引與驗證')):q02_custom]
```

### 3. DEC-P177｜標籤與多對多關聯

> [!tip] 建議
> ProductTag、AdminRoleAssignment 等使用明確 Join Entity，禁止逗號字串；需要排序或時間時保存為 Join Entity 屬性。

```meta-bind
INPUT[select(option(explicit_join_entities, '明確 Join Entity（建議）'), option(skip_navigation_simple_only, '簡單關聯用 EF Skip Navigation'), option(string_or_json_lists, '以字串／JSON保存多值'), option(custom_only, '完全採自主方案')):q03_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充哪些關聯需要具名 Join Entity 與附加欄位')):q03_custom]
```

### 4. DEC-P178｜會員地址與訂單地址

> [!tip] 建議
> 會員地址正規化為可編輯 Address；訂單成立時複製成不可變 OrderAddressSnapshot，不回指會員地址顯示歷史。

```meta-bind
INPUT[select(option(member_address_order_snapshot, '會員地址＋訂單快照（建議）'), option(order_references_member_address, '訂單只引用會員地址'), option(address_snapshot_table_shared, '共用版本化地址快照表'), option(custom_only, '完全採自主方案')):q04_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充訪客地址、匿名化、欄位與保存期限')):q04_custom]
```

### 5. DEC-P179｜訂單商品快照實體形態

> [!tip] 建議
> 將商品名、SKU Code、規格摘要、單價、成本放在 Order／OrderItem 的明確 Owned 欄位，不另建可共用的目前商品副本。

```meta-bind
INPUT[select(option(owned_columns_on_order_entities, 'Order／OrderItem明確快照欄位（建議）'), option(separate_order_snapshot_tables, '另建訂單快照資料表'), option(reference_current_product_only, '只引用目前商品'), option(custom_only, '完全採自主方案')):q05_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充快照欄位、規格格式、版本與不可變規則')):q05_custom]
```

### 6. DEC-P180｜優惠與折扣快照

> [!tip] 建議
> OrderCoupon 保存優惠碼、規則版本與訂單級結果；OrderItem 保存分攤金額，退款只依快照計算。

```meta-bind
INPUT[select(option(coupon_allocation_snapshots, '訂單優惠＋明細分攤快照（建議）'), option(coupon_version_reference_only, '只保存優惠券版本 FK'), option(recalculate_current_coupon, '退款時重算目前優惠規則'), option(custom_only, '完全採自主方案')):q06_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充優惠碼、規則、分攤、尾差與退款欄位')):q06_custom]
```

### 7. DEC-P181｜物流限制與門市快照

> [!tip] 建議
> 訂單同時保存 Provider Version FK 與成立時實際尺寸／重量／門市顯示值，兼顧追溯與歷史顯示。

```meta-bind
INPUT[select(option(version_fk_and_exact_snapshot, '版本FK＋精確值快照（建議）'), option(version_fk_only, '只保存版本 FK'), option(exact_values_only, '只保存數值，不留版本 FK'), option(custom_only, '完全採自主方案')):q07_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充包裹限制、門市、運費及 Provider 快照欄位')):q07_custom]
```

### 8. DEC-P182｜庫存真實來源與核對

> [!tip] 建議
> Movement／Reservation 是稽核來源，Balance 同交易更新；每天核對一次，差異只建立調整案件，不自動靜默修正。

```meta-bind
INPUT[select(option(movement_source_balance_transactional_daily_check, '同交易餘額＋每日核對（建議）'), option(balance_only, 'Balance 是唯一來源，不保留完整異動'), option(eventual_balance_rebuild, 'Balance 由異動最終一致重建'), option(custom_only, '完全採自主方案')):q08_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充核對頻率、容許差異、告警與修正權限')):q08_custom]
```

## B. 投影、摘要與報表

### 9. DEC-P183｜統一案件工作台實作

> [!tip] 建議
> 使用 SQL `UNION ALL` View 搭配 EF Core Keyless Entity 作唯讀投影；寫入仍回各領域 Use Case。

```meta-bind
INPUT[select(option(sql_union_view_keyless, 'SQL View＋Keyless Entity（建議）'), option(application_three_queries_merge, 'Application三次查詢後合併'), option(materialized_workbench_table, '持久化工作台投影表'), option(custom_only, '完全採自主方案')):q09_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充分頁、授權、索引、更新延遲與重建')):q09_custom]
```

### 10. DEC-P184｜訂單主要摘要狀態

> [!tip] 建議
> 第一版查詢時由正式狀態推導，不保存摘要欄位；只有量測不足才評估持久化投影。

```meta-bind
INPUT[select(option(derive_summary_in_query, '查詢時推導（建議）'), option(computed_database_column, '資料庫 Computed Column'), option(persisted_summary_column, '保存摘要欄位並同步'), option(custom_only, '完全採自主方案')):q10_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充推導位置、篩選、索引及一致性')):q10_custom]
```

### 11. DEC-P185｜報表反正規化啟動條件

> [!tip] 建議
> 先以正式交易表與索引量測；任一 M 報表 P95 超過 3 秒且查詢優化後仍失敗，才建立個別設計決策。

```meta-bind
INPUT[select(option(measure_then_approve_projection, '量測失敗後個別核准（建議）'), option(preaggregate_all_reports, '七個報表一開始全預彙總'), option(no_denormalized_reports, '永不建立預彙總'), option(custom_only, '完全採自主方案')):q11_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充量測資料、門檻、核准人與例外')):q11_custom]
```

### 12. DEC-P186｜報表 Snapshot 一致性

> [!tip] 建議
> 若日後建立 Snapshot，保存 `AsOfUtc`、來源版本與重建狀態；可由交易表完整重建，畫面清楚標示資料時間。

```meta-bind
INPUT[select(option(snapshot_rebuildable_with_asof, '可重建＋AsOf標示（建議）'), option(cache_without_persistence, '只使用短期記憶體快取'), option(snapshot_no_source_version, '保存彙總但不記來源版本'), option(custom_only, '完全採自主方案')):q12_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充更新頻率、延遲、失敗、重建與顯示')):q12_custom]
```

### 13. DEC-P187｜JSON 使用邊界

> [!tip] 建議
> 核心可查詢、需 FK／Constraint 的欄位關聯化；JSON 只保存版本化輔助內容與外部事件非關鍵摘要。

```meta-bind
INPUT[select(option(relational_core_json_auxiliary, '核心關聯化、JSON限輔助（建議）'), option(json_for_dynamic_specs, '動態規格與AI多用JSON'), option(no_json_columns, '資料庫完全不使用JSON'), option(custom_only, '完全採自主方案')):q13_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充允許 JSON 的表、Schema Version、長度與個資')):q13_custom]
```

## C. 識別、Identity 與完整性

### 14. DEC-P188｜PublicId 完整範圍

> [!tip] 建議
> Member、Admin、Product、SKU、Order、Payment、Shipment、Return、Refund、SupportTicket、ReportCase、BuildList、分享連結、圖片及附件都使用 PublicId。

```meta-bind
INPUT[select(option(public_id_external_resource_list, '所有外部資源使用PublicId（建議）'), option(sensitive_resources_only, '只對會員／訂單／案件使用'), option(order_and_member_only, '只對會員與訂單使用'), option(custom_only, '完全採自主方案')):q14_choice]
```
```meta-bind
INPUT[textArea(placeholder('列出需要／不需要 PublicId 的 Entity')):q14_custom]
```

### 15. DEC-P189｜PublicId Guid 產生策略

> [!tip] 建議
> Application 建立 UUID v7，SQL 使用非叢集唯一索引；內部 bigint identity 維持叢集主鍵。

```meta-bind
INPUT[select(option(app_uuid_v7_nonclustered, 'Application UUIDv7＋非叢集唯一索引（建議）'), option(random_uuid_v4, '隨機 UUIDv4'), option(sql_newsequentialid, 'SQL NEWSEQUENTIALID'), option(custom_only, '完全採自主方案')):q15_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充 Guid 版本、產生層、索引與資訊洩漏考量')):q15_custom]
```

### 16. DEC-P190｜同一 Identity 是否可同時是會員與管理員

> [!tip] 建議
> `MemberProfile` 與 `AdminProfile` 互斥；管理員若需前台購買，使用另一個會員帳號，降低 Cookie／權限混淆。

```meta-bind
INPUT[select(option(profile_types_mutually_exclusive, 'Profile互斥、分開帳號（建議）'), option(user_can_have_both_profiles, '同一User可同時有兩種Profile'), option(admin_profile_only_no_member_store, '管理員另建非Identity資料表'), option(custom_only, '完全採自主方案')):q16_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充建立流程、Email唯一性、角色與帳號切換')):q16_custom]
```

### 17. DEC-P191｜正規化欄位與有效資料唯一性

> [!tip] 建議
> Email、Code 等保存正規化欄位並建立唯一索引；只限制有效資料時使用 Filtered Unique Index，軟刪除資料仍保留歷史。

```meta-bind
INPUT[select(option(normalized_columns_filtered_unique, '正規化欄＋Filtered Unique（建議）'), option(application_validation_only, '只由Application檢查唯一'), option(unique_including_deleted, '刪除資料仍永久占用唯一值'), option(custom_only, '完全採自主方案')):q17_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充 Email、Code、SKU、門市、優惠券等唯一規則')):q17_custom]
```

### 18. DEC-P192｜可 Cascade 的 Owned Detail 清單

> [!tip] 建議
> 只允許沒有獨立生命週期且不需稽核的 Value／Owned Detail，逐項白名單；其他一律 Restrict。

```meta-bind
INPUT[select(option(explicit_owned_allowlist, '逐項 Owned 白名單（建議）'), option(all_required_children_cascade, '所有 Required Child Cascade'), option(no_cascade_anywhere, '完全禁止 Cascade'), option(custom_only, '完全採自主方案')):q18_choice]
```
```meta-bind
INPUT[textArea(placeholder('列出可 Cascade／必須 Restrict 的關聯')):q18_custom]
```

## D. 稽核、Outbox、匯入與格式

### 19. DEC-P193｜AuditLog 前後值格式

> [!tip] 建議
> 保存白名單欄位的結構化差異 JSON，加上資源、動作、結果與版本；個資只記「已變更」或遮蔽值。

```meta-bind
INPUT[select(option(structured_audit_allowlisted_diff, '白名單結構化Diff（建議）'), option(full_entity_json, '保存完整Entity前後JSON'), option(message_only, '只保存文字訊息'), option(custom_only, '完全採自主方案')):q19_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充欄位白名單、遮蔽、保存期限與查詢權限')):q19_custom]
```

### 20. DEC-P194｜Outbox 資料模型

> [!tip] 建議
> 使用單一 OutboxMessages 表，保存 Type、Payload Version、最小 JSON、Occurred／Processed、Retry 與 Correlation；事件內容不可含 Secret。

```meta-bind
INPUT[select(option(one_outbox_table_typed_payload, '單一版本化Outbox表（建議）'), option(outbox_table_per_module, '每個模組獨立Outbox表'), option(no_outbox_hangfire_direct, '交易後直接建立Hangfire工作'), option(custom_only, '完全採自主方案')):q20_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充 Payload、保存期限、鎖定、重試及清理')):q20_custom]
```

### 21. DEC-P195｜IdempotencyRecord 實體鍵

> [!tip] 建議
> 以 Actor Scope＋Operation＋Key 唯一，保存 Request Hash、Response 摘要、狀態與 24 小時到期；大型 Body 不完整保存。

```meta-bind
INPUT[select(option(scoped_idempotency_record, 'Scope＋Operation＋Key（建議）'), option(key_global_unique, '全系統Key唯一'), option(endpoint_memory_cache, '只用記憶體快取'), option(custom_only, '完全採自主方案')):q21_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充Scope、Hash、Response、期限、清理與併發')):q21_custom]
```

### 22. DEC-P196｜匯入預覽暫存方式

> [!tip] 建議
> 使用持久化 ImportBatch／ImportRow Staging，保存正規化後預覽與錯誤 24 小時；確認時驗證擁有者、版本與 Hash 後原子提交。

```meta-bind
INPUT[select(option(persistent_staging_24h, '持久化Staging 24小時（建議）'), option(memory_staging, '只存記憶體'), option(reupload_on_commit, '確認時要求重新上傳並重驗'), option(custom_only, '完全採自主方案')):q22_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充保存期限、檔案、中繼資料、清理與權限')):q22_custom]
```

### 23. DEC-P197｜獨立庫存匯入交易粒度

> [!tip] 建議
> 每一批 Inventory Import 全批原子成功或回滾；每列產生 Adjustment Movement，預覽與提交分離。

```meta-bind
INPUT[select(option(inventory_batch_atomic, '全批原子＋逐列Movement（建議）'), option(row_independent_results, '逐列獨立成功失敗'), option(no_inventory_csv, '不實作庫存CSV匯入'), option(custom_only, '完全採自主方案')):q23_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充調整原因、權限、上限、負庫存與錯誤結果')):q23_custom]
```

### 24. DEC-P198｜CSV Null、日期與小數表示

> [!tip] 建議
> Null 使用固定 `\N`，空字串保留空白欄位；日期 ISO 8601、Decimal 使用 `.`，禁止依 Excel／Windows 語系猜測。

```meta-bind
INPUT[select(option(null_token_iso_decimal_dot, '\\N＋ISO 8601＋小數點（建議）'), option(empty_means_null, '空欄一律代表Null'), option(locale_auto_detection, '自動偵測本機語系'), option(custom_only, '完全採自主方案')):q24_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充 Null Token、空白、Boolean、日期、時區與小數')):q24_custom]
```

### 25. DEC-P199｜金額四捨五入模式

> [!tip] 建議
> 每個分攤步驟使用 MidpointRounding.AwayFromZero 至 2 位，最後一筆吸收尾差；不得由前端自行重算。

```meta-bind
INPUT[select(option(midpoint_away_from_zero, 'AwayFromZero＋最後一筆尾差（建議）'), option(to_even_bankers, 'ToEven銀行家捨入'), option(integer_twd_calculation, '先轉整數元再計算'), option(custom_only, '完全採自主方案')):q25_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充折扣、稅、退款、比例及每步捨入時點')):q25_custom]
```

### 26. DEC-P200｜一般字串與 Email 正規化

> [!tip] 建議
> 一般輸入 Trim＋Unicode NFKC；Email 交由 Identity 的 NormalizedEmail／Invariant 規則，不自行改寫 local-part 內容。

```meta-bind
INPUT[select(option(trim_nfkc_email_invariant, 'Trim＋NFKC；Email依Identity（建議）'), option(trim_only, '只Trim不做Unicode正規化'), option(lowercase_all_strings, '所有字串一律小寫'), option(custom_only, '完全採自主方案')):q26_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充名稱、搜尋文字、Email、電話與顯示原值')):q26_custom]
```

### 27. DEC-P201｜Code／SKU／優惠碼大小寫

> [!tip] 建議
> 系統 Code 採不分大小寫唯一，輸入正規化為大寫保存；顯示名稱仍保留原語言與大小寫。

```meta-bind
INPUT[select(option(codes_case_insensitive_unique, 'Code不分大小寫＋大寫保存（建議）'), option(codes_case_sensitive, 'Code區分大小寫'), option(per_code_type_rules, '各Code類型個別規則'), option(custom_only, '完全採自主方案')):q27_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充 SKU、優惠券、門市、訂單編號與 Collation')):q27_custom]
```

### 28. DEC-P202｜Index／Constraint 命名與 FK 索引

> [!tip] 建議
> 採 `IX_{Table}_{Columns}`、`UX_`、`FK_`、`CK_`；所有 FK 先建索引，再依實際查詢調整複合順序。

```meta-bind
INPUT[select(option(ix_table_columns_conventions, '固定命名＋FK預設索引（建議）'), option(ef_default_names, '完全使用EF預設名稱'), option(manual_only_hot_queries, '只替熱查詢建立索引'), option(custom_only, '完全採自主方案')):q28_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充命名、欄位順序、Include、Filtered與審核規則')):q28_custom]
```

## E. 尚未收束的外部與本機設定

### 29. DEC-P203｜Brevo 寄件身分衝突處理

> [!tip] 建議
> 40 天專題先用 Brevo 已驗證單一寄件者 `alexyang920528@gmail.com`；日後取得自有網域再另行切換，不假裝擁有 gmail.com DNS。

```meta-bind
INPUT[select(option(verified_single_sender_now, '先用已驗證單一寄件者（建議）'), option(provide_owned_domain, '提供並驗證自有網域'), option(local_email_only, '不做真實Email寄送'), option(custom_only, '完全採自主方案')):q29_choice]
```
```meta-bind
INPUT[textArea(placeholder('提供寄件名稱、實際位址或可控制DNS的網域；不要填Key')):q29_custom]
```

### 30. DEC-P204｜本機資料根目錄與備份保留

> [!tip] 建議
> 使用專案外單一資料根目錄，依環境分子目錄；保留每日 7 份、每週 4 份，SQL 與檔案使用同一 Backup Set ID 核對。

```meta-bind
INPUT[select(option(project_data_root_daily7_weekly4, '專案外根目錄＋日7週4（建議）'), option(daily14_only, '只保留每日14份'), option(manual_demo_backups_only, '只在Migration／Demo前手動備份'), option(custom_only, '完全採自主方案')):q30_choice]
```
```meta-bind
INPUT[textArea(placeholder('補充資料路徑、磁碟、SQL／圖片／附件、保留與清理')):q30_custom]
```

## 批次操作

`BUTTON[submit-decision-batch-008,restore-draft-008]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 30 項決策
style: primary
id: submit-decision-batch-008
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
id: restore-draft-008
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
