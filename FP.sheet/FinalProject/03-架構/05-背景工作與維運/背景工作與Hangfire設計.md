---
文件狀態: 已確認
最後更新: 2026-08-13
追蹤項目:
  - TECH-05
  - TECH-09
---

# 背景工作與 Hangfire 設計

## Queue

| Queue | 工作 | 失敗影響 |
|---|---|---|
| `critical` | 未付款訂單逾時、庫存保留釋放、客服 SLA 提醒／逾時、狀態推進、每日庫存核對 | 可能影響庫存或核心流程，最高優先監看 |
| `notifications` | Email 驗證、密碼重設、訂單／付款／出貨／退款 Email 與站內通知 | 不得回滾已成功的商業交易 |
| `maintenance` | Outbox Dispatcher、Outbox／冪等紀錄清理、AI 紀錄、匯入 Staging、案件附件、暫存與孤兒檔清理、展示資料檢查 | 可延後但必須追蹤 |
| `ai` | S 功能客服摘要及其他允許的非同步 AI 工作 | AI 不可用時不影響人工客服與電商核心 |

使用 Hangfire.SqlServer 時不假定設定陣列等同絕對優先順序；核心安全仍由工作冪等、資料庫狀態前置條件及監控保證。

展示單機第一版只啟動一個 Hangfire Server，固定使用 4 個 Worker 處理四個 Queue。不得依未知展示電腦的 CPU 核心數自動放大；只有完成 SQL 連線、Queue 等待時間與 AI 並行量測後才能調整。

## 排程

時區固定 `Asia/Taipei`；工作實際寫入仍保存 UTC。每個 Recurring Job 使用固定 ID 與分散鎖，不允許上次尚未結束時重疊執行。

| Job ID | Queue | 排程 | 範圍／保存期限 |
|---|---|---|---|
| `orders-expire-unpaid` | critical | 每 1 分鐘 | 取消逾期未付款訂單並釋放保留 |
| `support-sla-scan` | critical | 每 5 分鐘 | 提醒即將到期／逾期案件，不直接改 Priority |
| `inventory-reconcile` | critical | 每日 02:00 | Movement／Reservation／Balance 核對，差異建 Case |
| `accounts-remove-unverified` | maintenance | 每日 02:15 | 清除滿 7 天、未驗證且無保留關聯帳號 |
| `imports-cleanup` | maintenance | 每日 02:30 | ImportRow／Raw 24h；Batch 摘要 90 天 |
| `idempotency-cleanup` | maintenance | 每日 02:40 | 清除過期且非 Processing／調查保留紀錄 |
| `ai-records-cleanup` | maintenance | 每日 03:00 | 原始互動 90 天、已結客服對話 180 天、統計 365 天 |
| `private-files-cleanup` | maintenance | 每日 03:20 | 已結案件附件 180 天；暫存／孤兒 24h |
| `product-images-cleanup` | maintenance | 每日 03:40 | 無引用舊圖 30 天；暫存 24h |
| `outbox-cleanup` | maintenance | 每日 04:00 | 成功訊息 30 天；未成功不按期限刪除 |
| `audit-cleanup` | maintenance | 每日 04:20 | 一般 Audit 365 天；Retention／Hold 排除 |
| `demo-data-validate` | maintenance | 每日 05:00＋Demo 前手動 | 只讀驗證，不修改 Seed |

Outbox Dispatcher 是常駐 5 秒輪詢，不使用分鐘 Cron。Email 由 Outbox Consumer 觸發，不以排程掃描所有訂單。

## 重試

| 工作類型 | 自動重試 | 最終失敗 |
|---|---:|---|
| Email 或暫時性外部錯誤 | 3 次，增加退避 | Failed＋告警；保留人工重送入口 |
| 清理工作 | 2 次 | Failed＋告警；不得把資料標成已刪除 |
| 商業狀態衝突／驗證失敗 | 0 次 | 記錄穩定結果，不重複執行 |
| 程式錯誤或未知錯誤 | 依工作明確設定，不套用無限重試 | Failed＋Trace ID |

不得沿用 Hangfire 預設十次重試作為本專案的隱含政策；每個 Job 必須明確標記類型。

## 冪等與交易邊界

- Job 參數只放穩定資源 ID、工作版本及 Correlation ID，不放完整個資或大型 DTO。
- Job 執行時重新讀取目前狀態；狀態已完成、取消或不再符合條件時安全結束。
- Email／通知使用 Outbox 或唯一工作鍵避免重複寄送。
- Outbox Dispatcher 只在訊息消費成功後寫入 `ProcessedAtUtc`；每 5 秒輪詢、每批最多 20 筆，同 Aggregate 保序，不同 Aggregate 可並行。
- 庫存釋放與狀態推進使用資料庫交易及 RowVersion／條件式更新。
- 每日庫存核對在 Asia/Taipei 02:00 比較 Movement／Reservation 與 Balance；任何差異建立 Critical `InventoryReconciliationCase`、通知 InventoryManager，不自動靜默改值。
- 檔案刪除只有在實體刪除成功後才寫入完成紀錄；失敗不偽裝成功。

## Dashboard 與人工操作

- `/hangfire` 只允許已完成 TOTP 的 `SuperAdmin`。
- Dashboard 採唯讀模式，不開放直接 Retry、Delete、Requeue 或 Trigger。
- 人工重試透過系統管理 API 執行，必須填寫原因、重新檢查工作狀態並寫入 AuditLog。
- Dashboard／API 不顯示密碼、Token、完整地址、完整 Email、OpenAI 原始敏感內容或附件路徑。

## 必要監控

- 各 Queue 等待數、最舊等待時間、成功／失敗／重試數。
- `critical` Queue 有失敗工作時立即顯示於健康／管理摘要。
- 通知、清理與 AI 失敗需可依 Job ID、資源 ID、Correlation ID 查詢。
- Demo 前檢查不得存在未說明的 `critical` Failed Job。

## 實作狀態與待辦

- 已完成 SQL Server Outbox Claim、5 秒／20 筆 Dispatcher、同 Aggregate 保序、Email／站內通知 Consumer、Consumer 冪等與 SQL Provider-backed 測試。
- 已完成台北時區 03:20 私有附件、03:40 商品圖片／24 小時暫存、04:00 Outbox 成功紀錄清理；清理 Job 固定批次上限、分散鎖並失敗重試 2 次。失敗 Outbox 不自動刪除。
- Outbox 人工重送已以獨立 `POST .../actions/retry` 與 `Outbox.Retry`（MFA SuperAdmin）完成；只重排 Failed 並寫中央 Audit。Dashboard 維持 SuperAdmin＋TOTP 唯讀，不能拿 Dashboard 直接重送替代。
- InventoryReconciliationCase Schema、狀態及修正邊界已定於資料字典；管理 API、通知與整合測試仍由庫存功能交付，不在共用排程內假造。
- Idempotency、Audit、案件與其他領域清理仍須依各自正式條件接上已建立的排程基礎；Metrics、告警及 Demo 前實際驗證仍待完成。
