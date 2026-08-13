---
type: decision-interaction
batch_id: DEC-BATCH-010
title: 核心產品與責任決策
status: applied
submission_feedback: ✅ 本批 8 項核心決策已於 2026-08-13 寫回正式文件、決策紀錄與追蹤表。
created_at: 2026-08-12
decision_count: 8
decision_range: DEC-P235～DEC-P242
q01_choice: corebuild
q01_custom: ""
q02_choice: real_catalog_fictional_business_disclaimer
q02_custom: ""
q03_choice: fixed_pair_backup_cross_review
q03_custom: ""
q04_choice: practical_roles_separate_high_risk_approval
q04_custom: ""
q05_choice: manufacturer_then_curated_fallback_block_claim
q05_custom: ""
q06_choice: paid_basis_refund_on_refund_date
q06_custom: ""
q07_choice: catalog_or_structured_manual_confirmed
q07_custom: ""
q08_choice: precision_ninety_recall_eighty_five
q08_custom: ""
submitted_at: 2026-08-12
applied_at: 2026-08-13
---

# DEC-BATCH-010｜核心產品與責任決策

目前狀態：`VIEW[{status}]`

> [!important]
> 本批只保留會影響商業呈現、人員責任、權限安全、相容性可信度、報表口徑或 AI 核心體驗的問題。每題可選方案並補充自主輸入；若選自主方案，請寫出完整規則。

### 1. DEC-P235｜專案與商店正式名稱

> [!tip] 建議
> 使用容易讓 HR 記住「電腦組裝＋AI 導購」的中英名稱；暫建議 `CoreBuild 科築電商`，但品牌名稱屬核心呈現，必須由你確認。

```meta-bind
INPUT[select(option(corebuild, 'CoreBuild 科築電商（建議）'), option(specmate, 'SpecMate 規格夥伴'), option(pc_compass, 'PC Compass 電腦指南'), option(custom_only, '完全採自主名稱')):q01_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：正式中英文名稱、簡稱與標語')):q01_custom]
```

### 2. DEC-P236｜真實品牌與虛構展示資料界線

> [!tip] 建議
> 商品品牌與公開規格可引用真實資料並記錄來源；商店、價格、會員、訂單、評價、庫存與營運結果使用虛構資料，頁尾與簡報標示教學展示、未與品牌合作。

```meta-bind
INPUT[select(option(real_catalog_fictional_business_disclaimer, '真實型錄＋虛構營運＋明確免責（建議）'), option(all_fictional_brands_and_data, '品牌與營運全部虛構'), option(realistic_mixed_minimal_disclaimer, '混合真實資料、只在簡報說明'), option(custom_only, '完全採自主方案')):q02_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：哪些資料可真實、標示位置、免責文字與來源規則')):q02_custom]
```

### 3. DEC-P237｜跨模組備援與測試覆核責任

> [!tip] 建議
> 五位成員與模組主責已在 40 天計畫定案，不再重問。建議固定配對備援與交叉測試：`haru ↔ yinyin`、`kafen ↔ terry`，alex 負責架構、最終整合及第二線備援；功能主責不得自行完成最終驗收。

```meta-bind
INPUT[select(option(fixed_pair_backup_cross_review, '固定配對備援＋交叉測試（建議）'), option(alex_as_only_backup_with_rotating_review, 'alex唯一備援＋輪替測試覆核'), option(no_fixed_backup_assign_per_sprint, '不固定備援、每Sprint指派'), option(custom_only, '完全採自主方案')):q03_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：備援配對、測試覆核人、缺席接手與最終驗收責任')):q03_custom]
```

### 4. DEC-P238｜高風險後台操作的職責分離

> [!tip] 建議
> 因為是五人專題，日常角色允許一人多角；退貨由 OrderManager 核准、退款由 FinanceManager 執行，角色授予只限 SuperAdmin，完整個資由 PrivacyAdmin／SuperAdmin，Audit 查詢加入 SecurityAdmin、匯出只限 SuperAdmin。這些採不同 Policy 與帳號展示，但不強制所有動作都做雙人線上工作流。

```meta-bind
INPUT[select(option(practical_roles_separate_high_risk_approval, '一人多角＋高風險Policy分離（建議）'), option(strict_two_person_approval, '所有高風險操作雙人核准'), option(superadmin_can_do_everything_directly, 'SuperAdmin可直接執行全部操作'), option(custom_only, '完全採自主方案')):q04_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：退貨核准、退款執行、角色授予、個資、Audit、SecurityAdmin與SuperAdmin例外')):q04_custom]
```

### 5. DEC-P239｜功耗資料來源與缺值處理

> [!tip] 建議
> CPU／GPU 優先採原廠公開 TDP／TBP／建議 PSU；缺值才用已記錄來源的人工維護值。核心值缺失時不得宣稱相容，改顯示「資料不足、需確認」，並阻擋一鍵加入完整組裝購物車。

```meta-bind
INPUT[select(option(manufacturer_then_curated_fallback_block_claim, '原廠優先＋人工備援＋缺值不宣稱相容（建議）'), option(manufacturer_only_block_missing, '只接受原廠資料，缺值完全阻擋'), option(estimate_missing_values_with_warning, '缺值由系統估算並警告'), option(custom_only, '完全採自主方案')):q05_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：來源優先序、可接受備援來源、缺值畫面與是否阻擋購買')):q05_custom]
```

### 6. DEC-P240｜營收、COD 與退款的報表歸屬

> [!tip] 建議
> 線上付款在付款成功日認列營收；COD 在送達並完成收款日認列；退款在退款成功日列負值，原銷售月份不回寫。另提供「依原訂單月份」分析維度，避免跨月退款看不懂。

```meta-bind
INPUT[select(option(paid_basis_refund_on_refund_date, '收款日認列、退款日沖減（建議）'), option(order_completed_basis_restate_original_month, '訂單完成認列、退款回寫原月'), option(order_created_gross_basis, '訂單建立即認列毛額'), option(custom_only, '完全採自主方案')):q06_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：信用卡、ATM、超商、COD、取消、退款與跨月歸屬')):q06_custom]
```

### 7. DEC-P241｜AI 搜尋中的既有零件輸入

> [!tip] 建議
> 使用者可選站內 SKU，或手動填結構化規格；自然語言只協助轉成候選值，必須由使用者確認後才進相容性判斷，AI 不可自行猜成某個型號。

```meta-bind
INPUT[select(option(catalog_or_structured_manual_confirmed, '站內SKU或結構化手填，AI結果需確認（建議）'), option(catalog_sku_only, '第一版只接受站內SKU'), option(ai_free_text_matching, 'AI直接把自由文字匹配成零件'), option(custom_only, '完全採自主方案')):q07_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：支援的零件、必要欄位、AI匹配與使用者確認方式')):q07_custom]
```

### 8. DEC-P242｜AI 搜尋補問品質門檻

> [!tip] 建議
> 在已標註評估集上，該補問時有補問的 Recall 至少 85%，提出的補問確實必要之 Precision 至少 90%；涉及預算或相容性必要資訊缺失時不得為追求少補問而直接推薦。

```meta-bind
INPUT[select(option(precision_ninety_recall_eighty_five, 'Precision≥90%、Recall≥85%（建議）'), option(precision_ninety_five_recall_ninety, 'Precision≥95%、Recall≥90%'), option(precision_eighty_five_recall_eighty, 'Precision≥85%、Recall≥80%'), option(custom_only, '完全採自主方案')):q08_choice]
```
```meta-bind
INPUT[textArea(placeholder('自主輸入：Precision、Recall、資料集與必要資訊漏問的發布阻擋規則')):q08_custom]
```

## 批次操作

`BUTTON[submit-decision-batch-010,restore-draft-010]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 8 項核心決策
style: primary
id: submit-decision-batch-010
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
    value: "✅ 已送出本批 8 項核心決策；答案已保存，可交由 Codex 收束。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-010
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
