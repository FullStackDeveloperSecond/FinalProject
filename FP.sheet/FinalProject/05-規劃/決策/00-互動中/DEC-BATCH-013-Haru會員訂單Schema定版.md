---
type: decision-interaction
batch_id: DEC-BATCH-013
title: Haru 會員、訂單與訪客存取 Schema 定版
status: applied
submission_feedback: ✅ 本批 8 項決策已於 2026-08-18 寫回；yinyin 後續已完成交叉覆核，DES-20 已關閉。Entity、Configuration 與 Migration 仍另行實作及審查。
created_at: 2026-08-18
submitted_at: 2026-08-18
applied_at: 2026-08-18
decision_count: 8
decision_range: DEC-P263～DEC-P270
q01_choice: sql_challenge_table
q01_custom: ""
q02_choice: reusable_30m_scoped_cookie
q02_custom: ""
q03_choice: separate_assembly_job_history
q03_custom: ""
q04_choice: balanced_5_60_3
q04_custom: ""
q05_choice: delete_after_30d_daily_job
q05_custom: ""
q06_choice: structured_without_label_snapshot
q06_custom: ""
q07_choice: application_service_sets_lockout_end
q07_custom: ""
q08_choice: owner_schema_des20_dual_review
q08_custom: ""
---

# DEC-BATCH-013｜Haru 會員、訂單與訪客存取 Schema 定版

目前狀態：`VIEW[{status}]`

> [!important]
> 本批同時收束 Haru 第二次上交稿仍待定的核心選擇，以及複驗發現的地址快照、Guest Token、限流、清理與 Lockout 實作缺口。Haru 已授權 alex 直接修改其上交內容；本批送出後，Codex 可依答案同步修正 Haru 正式交付、需求、資料字典、API 契約、狀態機、決策紀錄及追蹤表，不需要再取得 Haru 的個別修改授權。

## 審查來源

- Haru 第二次上交：`C:/Users/alexy/.codex/attachments/1c78dba7-7265-41a0-9145-cc5b0018d018/DoSelect_會員_登入登出_點數_會員等級_資料表設計 最終版2.md`
- 前次驗收報告：`E:/Output/Haru_最終版驗收修改報告.md`
- 正式基準：會員、驗證與通知、角色與權限、資料字典、狀態機、API DTO／Endpoint、DEC-P60 及既有安全規範。

## 已確認缺漏與直接修正範圍

以下內容不再個別詢問是否修正；送出本批後會依相關題目的答案一併處理：

| 缺漏 | 寫回處理 |
|---|---|
| `Orders` 地址快照缺少 `RecipientDistrict` | 依 DEC-P268 選定的地址模型補齊欄位、DTO 與快照規則 |
| `AddressInput` 未包含結構化縣市／行政區 | 依 DEC-P268 同步 API DTO 契約 |
| 共用會員資料字典仍缺 `Label`／`City`／`District` | 同步 MemberAddresses 正式欄位 |
| 購物交易資料字典仍使用舊 `RefundStatus` | 依 DEC-P60 統一為 `OrderRefundStatus` |
| Guest Token 單次使用規格與 30 分鐘 Cookie 操作流程衝突 | 依 DEC-P264 統一需求、資料字典與 API 規則 |
| Haru 文件的 Lockout 寫成可直接設定兩組 `IdentityOptions` | 依 DEC-P269 改成可實作的唯一方案 |
| Haru 尚無正式交付檔及追蹤 ID | 依 DEC-P270 建檔、追蹤及 Review Gate |
| 上交稿的完成核取與待定項目需重算 | 所有答案寫回後重新驗收，不保留失真的核取狀態 |

## 待你決策的 8 項摘要

| ID | 主題 | 建議方案 |
|---|---|---|
| DEC-P263 | Guest 驗證 Challenge 保存方式 | SQL Server 專表 `GuestOrderAccessRequests` |
| DEC-P264 | Guest Access Token 使用語意 | 30 分鐘內可多次操作的限單 HttpOnly Cookie |
| DEC-P265 | AssemblyJob 歷程模型 | 獨立 `AssemblyJobStatusHistories` |
| DEC-P266 | 驗證碼及重寄限流 | 平衡型安全參數 |
| DEC-P267 | Challenge／Token 清理與保存 | 到期後 30 天刪除、每日清理 |
| DEC-P268 | 台灣地址與訂單快照模型 | 結構化 City／District，訂單不保存 Label |
| DEC-P269 | 會員／管理員不同 Lockout 期限 | 共用登入服務依 AccountType 設定 LockoutEnd |
| DEC-P270 | Haru 正式交付與追蹤方式 | 建立正式 Owner Schema＋DES-20＋雙重覆核 |

---

### 1. DEC-P263｜Guest 驗證碼 Challenge 保存方式

API 已要求 `GuestOrderAccessRequest → requestPublicId + code → GuestOrderAccessToken` 兩階段流程。Challenge 還需支援錯誤次數、重寄、撤銷、到期與稽核；目前專案沒有 Redis，也不使用 Docker。

> [!tip] 建議
> 建立 SQL Server `GuestOrderAccessRequests`。它最容易配合 EF Core、交易、清理排程、嘗試次數與 Demo 驗證，也不需要新增基礎設施。只保存訂單 FK、HMAC 驗證碼雜湊與安全中繼資料，不保存明文驗證碼。

**方案選擇**

```meta-bind
INPUT[select(option(sql_challenge_table, 'SQL Server GuestOrderAccessRequests 專表（建議）'), option(signed_stateless_challenge, '簽章／加密的無狀態 Challenge'), option(distributed_cache_challenge, '導入分散式 Cache／Redis 保存 Challenge'), option(custom_only, '完全採自主方案')):q01_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：保存媒介、必要欄位、雜湊方式、撤銷與稽核方式')):q01_custom]
```

### 2. DEC-P264｜`GuestOrderAccessToken` 的使用語意

現有正式文字寫成 Token 單次使用，但 API 又把 Token 放入有效 30 分鐘的 HttpOnly Cookie，並用於查詢訂單、取消、物流、退貨與退款進度。若第一次 API 查詢後立即失效，頁面載入其他資料或執行後續操作都要重新寄信驗證。

> [!tip] 建議
> 一次性限制只套用六位數驗證碼／Challenge；驗證成功後核發的 Guest Access Cookie 可在 30 分鐘內重複使用，但只能操作綁定的單一訂單。取消或送出退貨後不強制失效，仍可查看結果；偵測異常、人工撤銷或到期才失效。

**方案選擇**

```meta-bind
INPUT[select(option(reusable_30m_scoped_cookie, '30 分鐘內可多次操作的限單 Cookie（建議）'), option(single_use_access_token, 'Access Token 每成功操作一次即失效'), option(rotating_one_time_cookie, '每次操作後輪替新的單次 Cookie'), option(custom_only, '完全採自主方案')):q02_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：可使用次數、期限、允許操作、何時撤銷及是否輪替')):q02_custom]
```

### 3. DEC-P265｜AssemblyJob 狀態歷程模型

一張訂單可有多台組裝電腦。`OrderStatusHistories` 目前只有 `OrderId`，無法識別某筆組裝歷程屬於哪個 `AssemblyJob`。

> [!tip] 建議
> 建立獨立 `AssemblyJobStatusHistories`，以 `AssemblyJobId` 關聯，保存 From／To、ReasonCode、Actor、OccurredAtUtc 與 TraceId。訂單層 `Orders.AssemblyStatus` 只保存聚合投影及其投影變更歷程，不把每台工作細節混入訂單主歷程。

**方案選擇**

```meta-bind
INPUT[select(option(separate_assembly_job_history, '獨立 AssemblyJobStatusHistories（建議）'), option(add_job_fk_to_order_history, 'OrderStatusHistories 加 nullable AssemblyJobId'), option(add_group_key_to_order_history, 'OrderStatusHistories 只加 AssemblyGroupKey'), option(custom_only, '完全採自主方案')):q03_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：資料表／欄位、關聯、索引、聚合投影與交易規則')):q03_custom]
```

### 4. DEC-P266｜訪客驗證碼、重寄與請求限流

Haru 已建立 `AttemptCount`、`ResendCount`、`LastSentAtUtc`，但「超過門檻」「冷卻時間」仍沒有數字，無法實作或撰寫驗收測試。

> [!tip] 建議
> 採平衡型：同一 Challenge 最多錯 5 次；60 秒後才能重寄；同一 Challenge 最多寄 3 次；15 分鐘內每 IP 最多建立 10 次、每 Email HMAC 與每訂單最多各 5 次。每個 Scope 都要同時通過，正式回應維持 202，避免帳號／訂單列舉。

**方案選擇**

```meta-bind
INPUT[select(option(balanced_5_60_3, '平衡型：5 次／60 秒／3 封；IP 10、Email 5、訂單 5／15 分鐘（建議）'), option(strict_3_120_2, '嚴格型：3 次／120 秒／2 封；IP 5、Email 3、訂單 3／15 分鐘'), option(relaxed_8_30_5, '寬鬆型：8 次／30 秒／5 封；IP 20、Email 10、訂單 10／15 分鐘'), option(custom_only, '完全採自主方案')):q04_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：錯誤次數、重寄秒數、寄送上限、IP／Email／訂單限流視窗')):q04_custom]
```

### 5. DEC-P267｜Guest Challenge／Access Token 保存與清理

到期資料若永久保留會持續增加安全資料與訂單關聯；若立即刪除，又不利於短期除錯與濫用調查。重大安全事件本來就應保存在共用 `AuditLogs`，不需要靠過期 Token 表永久保存。

> [!tip] 建議
> `GuestOrderAccessRequests` 與 `GuestOrderAccessTokens` 在到期／消耗／撤銷後保留 30 天，每日背景工作分批硬刪；需要長期保存的異常事件另寫 `AuditLogs`。清理工作需具冪等、批次大小與失敗重試。

**方案選擇**

```meta-bind
INPUT[select(option(delete_after_30d_daily_job, '到期後 30 天刪除＋每日清理（建議）'), option(delete_after_7d_daily_job, '到期後 7 天刪除＋每日清理'), option(delete_after_90d_daily_job, '到期後 90 天刪除＋每日清理'), option(custom_only, '完全採自主方案')):q05_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：保存天數、清理頻率、批次大小、AuditLogs 保留與 Legal Hold 例外')):q05_custom]
```

### 6. DEC-P268｜會員地址、Checkout 地址與訂單快照

Haru 已為 `MemberAddresses` 增加 `Label`、`City`、`District`，但 `AddressInput` 沒有 City／District，`Orders` 又遺漏 `RecipientDistrict`；同時 Label 只是會員辨識「住家／公司」的名稱，不影響實際配送。

> [!tip] 建議
> 採台灣結構化地址：`MemberAddresses` 與 Checkout `AddressInput` 都有 PostalCode／City／District／AddressLine1／AddressLine2；Orders 保存對應 Recipient 快照。`Label` 只留在會員地址簿，不複製到訂單，減少無商業必要的歷史資料。

**方案選擇**

```meta-bind
INPUT[select(option(structured_without_label_snapshot, '結構化 City／District，訂單不保存 Label（建議）'), option(structured_with_label_snapshot, '結構化 City／District，訂單也保存 RecipientLabel'), option(flatten_full_address_line, 'Checkout 與訂單只保存完整 AddressLine1'), option(custom_only, '完全採自主方案')):q06_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：地址欄位、必填條件、超商例外、Label 是否快照及欄位長度')):q06_custom]
```

### 7. DEC-P269｜會員與管理員不同 Lockout 期限的實作

正式需求是同樣連續失敗 5 次，但會員鎖 15 分鐘、管理員鎖 30 分鐘。單一 Identity User Store 的 `IdentityOptions.Lockout.DefaultLockoutTimeSpan` 是共用值，不能只靠設定兩組 Policy 自動依 AccountType 切換。

> [!tip] 建議
> 保留 Identity 的 `AccessFailedCount`／`LockoutEnd`，由共用登入 Application Service 處理第五次失敗：讀取 `AccountType`，會員設定 `LockoutEnd=UtcNow+15m`，管理員設定 `UtcNow+30m`。所有登入入口共用此服務並加入兩類帳號的整合測試。

**方案選擇**

```meta-bind
INPUT[select(option(application_service_sets_lockout_end, '共用登入服務依 AccountType 設定 LockoutEnd（建議）'), option(unify_all_to_30m, '會員與管理員都統一鎖定 30 分鐘'), option(separate_identity_stores, '會員／管理員拆成不同 Identity Store'), option(custom_only, '完全採自主方案')):q07_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：失敗計數、鎖定期限、共用服務、Cookie Scheme 與測試方式')):q07_custom]
```

### 8. DEC-P270｜Haru 正式 Schema 交付與追蹤方式

Kafen、Terry、Yinyin 都已有 `03-架構/資料表實作交付` 正式 Owner 文件及 DES-17～DES-19；Haru 目前只有外部附件，正式文件與追蹤項目缺席。

> [!tip] 建議
> 新增 `Haru-會員登入訂單與訪客存取最終Schema.md`，使用下一個未占用的 `DES-20` 追蹤；Haru 主責、yinyin 第一線覆核、alex 最終整合驗收。只有本批全部寫回且兩層覆核完成後才能關閉 DES-20 及產生待審 Migration。

**方案選擇**

```meta-bind
INPUT[select(option(owner_schema_des20_dual_review, '正式 Owner Schema＋DES-20＋yinyin／alex 雙重覆核（建議）'), option(merge_into_common_dictionary_only, '只合併共用資料字典，不保留 Haru Owner 文件'), option(keep_external_attachment, '維持外部附件，不納入正式交付資料夾'), option(custom_only, '完全採自主方案')):q08_choice]
```

**自主輸入（可補充或取代選項）**

```meta-bind
INPUT[textArea(placeholder('自主輸入：正式檔名、追蹤 ID、覆核人、關閉條件與 Migration Gate')):q08_custom]
```

## 批次操作

`BUTTON[submit-decision-batch-013,restore-draft-013]`

送出結果：`VIEW[{submission_feedback}]`

```meta-bind-button
label: 送出本批 8 項決策
style: primary
id: submit-decision-batch-013
hidden: true
actions:
  - type: updateMetadata
    bindTarget: status
    evaluate: false
    value: submitted
  - type: updateMetadata
    bindTarget: submitted_at
    evaluate: false
    value: "2026-08-18"
  - type: updateMetadata
    bindTarget: submission_feedback
    evaluate: false
    value: "✅ 已送出本批 8 項決策；答案已保存，可交由 Codex 直接寫回 Haru 與正式文件。"
```

```meta-bind-button
label: 退回草稿
style: default
id: restore-draft-013
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

## 寫回門檻

- 8 題皆選擇方案，或自主輸入提供完整可實作方案。
- 若自主輸入與選項衝突，以自主輸入的完整方案為準，但 Codex 需先指出衝突。
- 本批送出後，依本次明確授權，可直接修改 Haru 的正式 Owner 文件及所有受影響正式文件，不需要再等待 Haru 授權。
- 寫回時必須更新決策紀錄、未完成項目追蹤表、資料表交付索引與開發日誌。
- 在 DEC-BATCH-013 寫回及 yinyin／alex Review Gate 完成前，不得建立或套用 Haru 工作包的 EF Core Migration。
