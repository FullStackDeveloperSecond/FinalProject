---
文件狀態: 已確認
最後更新: 2026-08-12
追蹤項目:
  - TECH-05
  - TECH-09
---

# 背景工作與 Hangfire 設計

## Queue

| Queue | 工作 | 失敗影響 |
|---|---|---|
| `critical` | 未付款訂單逾時、庫存保留釋放、客服 SLA 提醒／逾時、狀態推進 | 可能影響庫存或核心流程，最高優先監看 |
| `notifications` | Email 驗證、密碼重設、訂單／付款／出貨／退款 Email 與站內通知 | 不得回滾已成功的商業交易 |
| `maintenance` | AI 紀錄、案件附件、暫存與孤兒檔清理、展示資料檢查 | 可延後但必須追蹤 |
| `ai` | S 功能客服摘要及其他允許的非同步 AI 工作 | AI 不可用時不影響人工客服與電商核心 |

使用 Hangfire.SqlServer 時不假定設定陣列等同絕對優先順序；核心安全仍由工作冪等、資料庫狀態前置條件及監控保證。

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
- 庫存釋放與狀態推進使用資料庫交易及 RowVersion／條件式更新。
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
