---
文件狀態: 部分已確認
最後更新: 2026-08-12
追蹤項目:
  - DES-08
  - TECH-09
  - TECH-11
---

# 資料一致性、Outbox 與冪等設計

## 目的

商業交易、通知、背景工作與稽核必須在失敗、重試或重複請求下維持一致。第一版採 SQL Server 單一資料庫交易＋Outbox，不導入外部 Message Broker。

## 交易邊界

同一 Application Use Case 的核心資料、狀態歷程、庫存異動、必要 AuditLog 與 Outbox Message 在同一 SQL Server 交易中提交。交易成功後才由背景 Dispatcher 發送通知或建立後續 Hangfire 工作。

```text
Application Use Case
→ 驗證狀態／授權／冪等
→ 更新 Aggregate 與歷程
→ 寫入 AuditLog
→ 寫入 OutboxMessages
→ Commit
→ Outbox Dispatcher 建立背景工作／通知
```

外部 Email、OpenAI、模擬付款或物流呼叫不得放在長時間資料庫交易內。

## OutboxMessages

使用單一版本化 Outbox 表；不同模組以 `Type` 與 `PayloadVersion` 區分。

| 欄位概念 | 規則 |
|---|---|
| `Id` | `bigint identity` 內部主鍵 |
| `PublicId` | UUID v7，供管理查詢與追蹤 |
| `Type` | 穩定事件類型，不使用 .NET 完整型別名稱作永久契約 |
| `PayloadVersion` | 每個 Type 的正整數版本 |
| `PayloadJson` | 最小必要資料；不得含 Secret、Cookie、Token 或不必要個資 |
| `OccurredAtUtc` | 事件發生 UTC `datetime2(3)` |
| `AvailableAtUtc` | 最早可處理時間 |
| `ProcessedAtUtc` | 成功處理時間；未完成為 Null |
| `AttemptCount` | 已嘗試次數 |
| `LastErrorCode` | 穩定安全錯誤碼，不保存完整敏感例外 |
| `CorrelationId` | 串接 HTTP、交易、Hangfire 與 Log |
| `RowVersion` | Dispatcher 競爭與人工操作的樂觀鎖 |

- Dispatcher 必須支援多次執行，不因重複掃描產生重複商業副作用。
- 消費端使用事件 PublicId／業務唯一鍵去重；不能只把 `ProcessedAtUtc` 當作唯一防線。
- 處理失敗依工作類型套用既有 3／2／0 重試政策；最終失敗保留查詢與具稽核的人工重送入口。
- Payload Schema 修改需新增版本；舊版本在保存期內仍可被處理或明確拒絕並告警。

Dispatcher 每 5 秒輪詢，每批最多鎖定 20 筆；同一 Aggregate 依 `OccurredAtUtc, Id` 處理，不同 Aggregate 可並行。成功訊息保存 30 天後分批清理；未成功訊息不得因期限自動刪除，必須告警並人工結案。

## IdempotencyRecord

建立訂單、退款及其他高風險命令以 `ActorScope + Operation + Key` 唯一。

| 欄位概念 | 規則 |
|---|---|
| `ActorScope` | 會員 PublicId、訪客 Session／Guest Scope 或管理員 PublicId 的不可逆範圍識別 |
| `Operation` | 穩定命令名稱，例如 `order.create`、`refund.execute` |
| `Key` | Client 提供的 Idempotency-Key |
| `RequestHash` | 規範化 Request 的安全雜湊，不保存完整敏感 Body |
| `Status` | `Processing`、`Succeeded`、`Failed` |
| `ResponseStatusCode` | 原始 HTTP 結果 |
| `ResponseSummary` | 可安全重播的最小結果或資源 PublicId |
| `ExpiresAtUtc` | 建立後 24 小時 |
| `RowVersion` | 同 Key 競爭控制 |

- 同 Scope、Operation、Key 與同 Request Hash 重送時回傳原結果。
- 同 Key 搭配不同 Hash 回傳 409，不執行第二次命令。
- 同 Key 同時抵達時只有一筆能建立 Processing 紀錄；其他請求回 `409 Conflict`、穩定錯誤碼及 `Retry-After: 3`。
- ResponseSummary 最多 32 KB，使用版本化 JSON，只保存 Status、允許的 Headers 與可安全回放 Body；超過時保存結果資源 PublicId 並重新讀取。
- 逾期清理不得刪除仍在 Processing 或被調查保留的紀錄。

## AuditLog

AuditLog 保存結構化白名單差異，不序列化完整 Entity。

| 欄位概念 | 規則 |
|---|---|
| Actor | User／Admin PublicId、角色與 Actor Type |
| Action | 穩定動作名稱 |
| Resource | Resource Type＋PublicId；內部 Id 僅供受保護查詢 |
| Result | 成功、拒絕、衝突或失敗安全碼 |
| ChangedFieldsJson | 白名單欄位、Before／After 遮蔽值與 Schema Version |
| Reason | 高風險人工操作必填理由 |
| Correlation | Correlation ID、Trace ID、必要 Job PublicId |
| Network | 一般遮蔽 IP；完整 IP 只限明確安全目的與授權 |
| Time | UTC `datetime2(3)` |

- 密碼、Token、Cookie、API Key、完整地址、完整 Email 與付款資料不得進入差異 JSON。
- 個資欄位只記「已變更」、遮蔽值或不可逆摘要，除非另有明確核准的稽核目的。
- AuditLog 不接受一般管理員修改或刪除；一般紀錄保存 365 天後由維護工作分批清除。
- `SecurityAdmin` 與 `PrivacyAdmin` 依職責查詢；只有 `SuperAdmin` 可匯出。查詢與匯出本身也寫入 AuditLog。
- 第一版不實作 Legal Hold UI；資料模型預留 `RetentionUntilUtc` 與 `HoldReason`，被保留的紀錄不得由一般清理工作刪除。

## 背景 Dispatcher 冪等

- Job 參數只保存 Outbox PublicId、事件版本及 Correlation ID。
- 執行時重新查目前 Outbox／業務狀態，不依賴排程當下的大型 DTO。
- Email 使用 Notification／Template／Recipient Purpose 的唯一工作鍵防重複寄送。
- 清理工作只在實體刪除成功後標記完成。
- 人工重送建立新的稽核操作，但不得改寫原始事件內容。

第一版 Outbox Type 至少包含 `notification.email.requested.v1`、`notification.in_app.requested.v1`、`inventory.reconciliation_mismatch.detected.v1`。Payload 只保存 Template／Purpose、資源 PublicId、語系、必要參數與 Correlation，不序列化完整 Entity 或完整個資；Recipient 由受保護資料在 Consumer 執行時重新取得。

| Event Type | Payload 白名單 | Consumer |
|---|---|---|
| `notification.email.requested.v1` | `notificationPublicId`、`templateKey`、`recipientPurpose`、`resourceType`、`resourcePublicId`、`locale`、`parameterSetVersion` | Email Notification Consumer 重新讀取收件資料、轉譯 Template、經 Brevo SMTP 寄送 |
| `notification.in_app.requested.v1` | `notificationPublicId`、`memberPublicId`、`messageKey`、`resourceType`、`resourcePublicId`、`locale`、`parameterSetVersion` | In-app Consumer 以唯一 Notification PublicId 建立站內通知 |
| `inventory.reconciliation_mismatch.detected.v1` | `casePublicId`、`skuPublicId`、`expectedOnHand`、`actualOnHand`、`detectedAtUtc` | Inventory Consumer 建立告警與管理摘要，不直接修正庫存 |

Email Consumer 成功需同時保存 Provider Message ID 與寄送結果；暫時失敗依通知重試政策，永久失敗寫安全錯誤碼並保留人工重送入口。Template 參數缺失視為程式／契約錯誤，不以空字串寄出。

## 驗收案例

1. 商業交易 Commit 後服務中止，重新啟動仍能處理尚未完成的 Outbox。
2. 同一 Outbox 被兩個 Worker 競爭，只產生一次可見通知副作用。
3. 同 Idempotency-Key 重送建立訂單，回傳同一 Order PublicId 且庫存只保留一次。
4. 同 Key 不同 Payload 回傳 409。
5. Audit Diff 不包含完整 Email、地址、Token 或密碼。
6. Outbox 與 Audit 寫入失敗時，核心交易整體回滾，不出現已改狀態卻無可追蹤事件。

## AuditLog 匯出白名單

SuperAdmin 匯出只包含時間、Actor Type／PublicId、角色快照、Action、Resource Type／PublicId、Result、Reason、Changed Field Names、Correlation／Trace ID 與遮蔽 IP。不得匯出完整 Before／After 個資、Internal Id、Token、Cookie、付款資料或原始例外。CSV 使用 UTF-8 BOM；每次匯出建立新的 AuditLog。

## Actor Scope

- 登入會員／管理員：`user:{UserPublicId}`。
- 匿名購物車：`guest-cart:{CartPublicId}`；只有後端驗證 Cookie 後可取得，不接受前端任意傳入 Scope。
- 已驗證訪客訂單：`guest-order:{OrderPublicId}:{AccessGrantPublicId}`；Grant 到期或撤銷後 Scope 失效。
- 資料庫只保存 Scope 的 SHA-256＋伺服器 Pepper Hash，不保存原始 Cookie、Token、Email、IP 或 User-Agent。
- 公開且沒有可驗證 Actor 的操作不得使用可造成商業副作用的 Idempotency Record；只適用一般 Rate Limit。

## 待實作

- EF Core Entity、Configuration、Migration、Dispatcher 鎖定、Email Consumer 與併發整合測試。
