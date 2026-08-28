---
文件狀態: 已確認
最後更新: 2026-08-28
追蹤項目:
  - DES-07
  - DES-08
  - DES-15
  - DES-20
  - AI-13
---

# 資料字典｜會員、客服、AI 與治理

本頁沿用 [[03-架構/03-資料與一致性/資料字典-商品庫存與組裝]] 的 Profile。Identity 主鍵使用 `nvarchar(450)`；領域實體仍使用 `bigint identity`＋UUID v7 PublicId。所有 FK 預設 Restrict。

## Identity 與 Profile

`ApplicationUsers` 擴充 ASP.NET Core Identity `AspNetUsers`：

| 自訂欄位 | 型別 | 規則 |
|---|---|---|
| `PublicId` | uniqueidentifier | UX；UUID v7；API 唯一公開帳號識別 |
| `AccountType` | varchar(16) | `Member/Admin`；建立後不可切換 |
| `AccountStatus` | varchar(24) | `PendingEmailVerification/Active/Suspended/Anonymized/Disabled` |
| `PreferredLocale` | varchar(10) | `zh-TW/ja-JP/ko-KR`，第一版預設 zh-TW |
| `CreatedAtUtc/UpdatedAtUtc` | datetime2(3) | 必填 |
| `AnonymizedAtUtc` | datetime2(3) NULL | 匿名化後不可登入 |
| `RowVersion` | rowversion | 管理狀態併發控制 |

- `NormalizedEmail` 採 Filtered Unique Index（非 Null 唯一）；Email 驗證必須完成後才能正常登入／結帳。
- 手機不作為登入識別；`PhoneNumber` 只可作選填聯絡資料，不建立登入 Lookup 或簡訊驗證資料表。
- TOTP Authenticator Key、Recovery Code 與 Security Stamp 使用 Identity Token Provider／既有 Stores，不另存可讀 TOTP Secret。
- `MemberProfiles(UserId)` 與 `AdminProfiles(UserId)` 都是一對一 PK/FK；AccountType 與 Profile 互斥由建立 Use Case、交易及整合測試保證。

| Table | 欄位 | Constraint／Index |
|---|---|---|
| `MemberProfiles` | `UserId nvarchar(450)` PK/FK、`PublicId uniqueidentifier` UX、`DisplayName nvarchar(100)`、`BirthDate date NULL`、`CreatedAtUtc/UpdatedAtUtc`、`RowVersion` | 不保存第二份 Email／電話；匿名化改 DisplayName 並清除選填個資 |
| `AdminProfiles` | `UserId nvarchar(450)` PK/FK、`PublicId uniqueidentifier` UX、`EmployeeCode nvarchar(64)`、`DisplayName nvarchar(100)`、`IsActive bit`、`CreatedAtUtc/UpdatedAtUtc`、`RowVersion` | EmployeeCode UX；管理員不得使用同帳號前台購買 |
| `MemberAddresses`／MutableEntity | `MemberUserId nvarchar(450)`、`Label nvarchar(50)`、`RecipientName nvarchar(100)`、`Phone nvarchar(32)`、`PostalCode nvarchar(16)`、`City nvarchar(50)`、`District nvarchar(50)`、`AddressLine1 nvarchar(300)`、`AddressLine2 nvarchar(300) NULL`、`IsDefault bit`、`DeletedAtUtc datetime2(3) NULL` | 每會員最多一筆有效 Default Filtered UX；軟刪除；Label 只供地址簿辨識，不進訂單；地址不得覆寫訂單快照 |

角色使用 Identity `AspNetRoles`／`AspNetUserRoles`；角色名稱只表達粗粒度，精確操作仍由程式 Policy 控制。角色異動同交易寫 AuditLog 並更新 Security Stamp。

## 通知、Email 與 AI 同意

| Table／Profile | 額外欄位 | Constraint／Index |
|---|---|---|
| `Notifications`／Entity | `RecipientUserId nvarchar(450)`、`Type varchar(64)`、`Title nvarchar(200)`、`Body nvarchar(1000)`、`ResourceType varchar(64) NULL`、`ResourcePublicId uniqueidentifier NULL`、`ReadAtUtc datetime2(3) NULL`、`ExpiresAtUtc datetime2(3) NULL` | `IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc` |
| `EmailDeliveries`／MutableEntity | `RecipientUserId nvarchar(450) NULL`、`RecipientEmailNormalized nvarchar(320)`、`TemplateCode nvarchar(64)`、`TemplateVersion int`、`Status varchar(24)`、`ProviderMessageId nvarchar(128) NULL`、`AttemptCount int`、`NextAttemptAtUtc/SentAtUtc/FailedAtUtc datetime2(3) NULL`、`LastErrorCode nvarchar(64) NULL` | Idempotency Scope UX；不保存密碼重設 Token 明文；`IX_EmailDeliveries_Status_NextAttemptAtUtc` |
| `AiConsentRecords`／Entity | `MemberUserId nvarchar(450)`、`PolicyVersion int`、`Locale varchar(10)`、`Status varchar(16)`、`GrantedAtUtc datetime2(3)`、`WithdrawnAtUtc datetime2(3) NULL`、`Source varchar(32)` | append-only；Member Restrict FK；PolicyVersion／Locale／Grant-Withdraw 狀態 Check；`IX_AiConsentRecords_MemberUserId_CreatedAtUtc`；最新有效同意由 CreatedAtUtc＋Id 判定 |

## 評價與收藏

| Table／Profile | 額外欄位 | Constraint／Index |
|---|---|---|
| `ProductReviews`／MutableEntity | `MemberUserId nvarchar(450)`、`OrderItemId bigint`、`ProductId bigint`、`Rating tinyint`、`Title nvarchar(80)`、`Content nvarchar(1000)`、`Status varchar(24)`、`ReviewedByAdminUserId nvarchar(450) NULL`、`ReviewedAtUtc datetime2(3) NULL`、`RejectionReason nvarchar(500) NULL` | OrderItem UX；Rating 1～5；Status=`Draft/PendingReview/Approved/Rejected/Hidden`；只有 Approved 公開；Approved 編輯建立 Revision 並回 PendingReview；Rejected 可修正後重送 PendingReview |
| `ReviewImages`／MutableEntity | `ProductReviewId bigint`＋私有待審圖片中繼資料 | 每評價最多 3 張、每張 5 MB；核准前不可公開 |
| `Favorites` | `MemberUserId nvarchar(450)`、`ProductId bigint`、`CreatedAtUtc datetime2(3)` | 複合 PK；新增／刪除冪等；商品下架仍保留，會員匿名化時刪除 |

評價必須以 OrderItem→Order 完成狀態及 Member 所有權驗證；資料庫唯一性避免同一購買明細重複評價。

## 客服案件

### SupportTickets

使用 MutableEntity，欄位：`MemberUserId nvarchar(450)`、`OrderId bigint NULL`、`Category varchar(32)`、`Subject nvarchar(200)`、`Status varchar(32)`、`Priority varchar(16)`、`AssigneeAdminUserId nvarchar(450) NULL`、`FirstResponseDueAtUtc/ResolutionDueAtUtc datetime2(3)`、`FirstHumanResponseAtUtc/ResolvedAtUtc/ClosedAtUtc datetime2(3) NULL`、`WaitingForCustomerStartedAtUtc datetime2(3) NULL`、`PausedSeconds int default 0`、`LastActivityAtUtc datetime2(3)`、`ReopenCount int default 0`、`RowVersion`。

- Category 使用七類正式分類；Priority=`Urgent/High/Normal/Low`；狀態依完整狀態機。
- `IX_SupportTickets_Status_AssigneeAdminUserId_LastActivityAtUtc`、`IX_SupportTickets_Status_FirstResponseDueAtUtc`、`IX_SupportTickets_Status_ResolutionDueAtUtc`。
- 同一時間只能由一位客服承接；自領採 RowVersion／條件更新。

| Table／Profile | 額外欄位 | Constraint／Index |
|---|---|---|
| `SupportMessages`／Entity | `SupportTicketId bigint`、`SenderType varchar(16)`、`SenderUserId nvarchar(450) NULL`、`Body nvarchar(4000)`、`IsInternal bit`、`Language varchar(10)`、`SentAtUtc datetime2(3)`、`AiGenerated bit` | Internal 只允許 Admin；`IX_SupportMessages_TicketId_SentAtUtc` |
| `SupportAttachments`／MutableEntity | `SupportTicketId bigint`、`SupportMessageId bigint NULL`＋私有附件中繼資料 | 每案最多 3 個、每個 10 MB；StorageKey UX；SupportMessageId 有值時必須屬同一 SupportTicket，由 Application 交易驗證＋整合測試保證 |
| `SupportAssignmentHistories`／Entity | `SupportTicketId bigint`、`FromAdminUserId/ToAdminUserId nvarchar(450) NULL`、`Action varchar(24)`、`Reason nvarchar(500) NULL`、`ActorUserId nvarchar(450) NULL`、`OccurredAtUtc datetime2(3)` | append-only；ActorUserId 為 Nullable Identity FK；`IX_AssignmentHistories_TicketId_OccurredAtUtc` |
| `SupportSlaEvents`／Entity | `SupportTicketId bigint`、`EventType varchar(32)`、`DueAtUtc datetime2(3) NULL`、`OccurredAtUtc datetime2(3)`、`DurationSeconds int NULL`、`MetadataJson nvarchar(2000) NULL` | append-only；Metadata 有 Schema Version、無個資 |
| `SupportSummaries`／MutableEntity | `SupportTicketId bigint`、`SourceLastMessageId bigint`、`Summary nvarchar(1500)`、`Model nvarchar(100)`、`PromptVersion nvarchar(64)`、`Status varchar(24)`、`GeneratedAtUtc datetime2(3)` | Ticket 每個 SourceLastMessage 唯一；AI 失敗不阻擋案件 |

## 檢舉案件

`ReportCases` 使用 MutableEntity：`ReporterUserId nvarchar(450)`、`TargetType varchar(32)`、`TargetPublicId uniqueidentifier`、`ReasonCode varchar(64)`、`Description nvarchar(1000)`、`Status varchar(24)`、`Priority varchar(16)`、`AssigneeAdminUserId nvarchar(450) NULL`、`ResolutionCode varchar(64) NULL`、`ResolvedAtUtc datetime2(3) NULL`、`LastActivityAtUtc datetime2(3)`。

- TargetType 白名單=`Product/ProductReview/SupportMessage/OtherPublicContent`；不得保存任意內部 Table 名稱。
- `IX_ReportCases_Status_AssigneeAdminUserId_LastActivityAtUtc`、`IX_ReportCases_TargetType_TargetPublicId_Status`。
- Status 固定為 `Open/Assigned/InReview/Actioned/Rejected/Closed`；補件使用 Action／History，不新增狀態；成立以 `Actioned + ResolutionCode` 表達。
- 重複檢舉由同 Reporter＋TargetType＋TargetPublicId＋ReasonCode＋未結案 Status 查詢阻擋；關閉後可建立新案件。
- 一般案件進專用佇列供 CustomerService 自領；個資、安全、詐欺、法律與其他高風險案件須由 CustomerServiceSupervisor 指派或覆核。

`ReportAttachments` 使用 MutableEntity，包含 `ReportCaseId` 及私有附件中繼資料；每案 3 個、每個 10 MB。檢舉與客服不共用可寫案件 Entity。工作台來源投影只能對應正式 12 欄，RowVersion 與 AvailableActions 由來源詳情取得。

## AI 對話與搜尋

| Table／Profile | 額外欄位 | Constraint／Index |
|---|---|---|
| `AiConversations`／MutableEntity | `MemberUserId nvarchar(450)`、`SupportTicketId bigint NULL`、`Purpose varchar(24)`、`Locale varchar(10)`、`Status varchar(16)`、`ConsentPolicyVersion int`、`LastActivityAtUtc datetime2(3)`、`ExpiresAtUtc datetime2(3)` | 客服 AI 必須有 Member 與有效同意；`IX_AiConversations_MemberUserId_LastActivityAtUtc` |
| `AiInteractions`／Entity | `AiConversationId bigint NULL`、`SearchPublicId uniqueidentifier NULL`、`Sequence int`、`UserContentProtected nvarchar(4000)`、`AssistantContent nvarchar(4000) NULL`、`IntentJson nvarchar(8000) NULL`、`Model nvarchar(100)`、`PromptVersion/SchemaVersion nvarchar(64)`、`InputTokens/OutputTokens int`、`EstimatedCostUsd decimal(12,6)`、`Status varchar(24)`、`FallbackReason varchar(64) NULL`、`LatencyMs int` | Conversation＋Sequence UX；Purpose 決定 Search／Conversation 恰一；內容按保存政策清理 |
| `AiToolInvocations`／Entity | `AiInteractionId bigint`、`ToolName nvarchar(64)`、`ToolContractVersion nvarchar(64)`、`ArgumentsHash binary(32)`、`ResultCode varchar(32)`、`CitationCount int`、`LatencyMs int`、`Attempt int` | 不保存未遮蔽完整參數；`IX_AiToolInvocations_InteractionId` |
| `AiCitations`／Entity | `AiInteractionId bigint`、`SourceType varchar(32)`、`SourcePublicId uniqueidentifier NULL`、`SourceVersion nvarchar(64) NULL`、`Label nvarchar(200)`、`Url nvarchar(2048) NULL`、`SortOrder int` | `IX_AiCitations_InteractionId_SortOrder` |
| `AiUsageLedger`／Entity | `MemberUserId nvarchar(450) NULL`、`AnonymousSessionKeyHash binary(32) NULL`、`Feature varchar(32)`、`RequestPublicId uniqueidentifier`、`InputTokens/OutputTokens int`、`EstimatedCostUsd decimal(12,6)`、`Succeeded bit`、`OccurredAtUtc datetime2(3)` | RequestPublicId UX；Member／Session 恰一 Check；Member Restrict FK；append-only；AI-13 的 `Succeeded=true` 表示額度預留成功，模型失敗不退款；實際模型 Token／成本由後續 Interaction／Adapter 階段保存 |
| `AiSearchFunnelEvents`／Entity | `EventName varchar(64)`、`SearchPublicId uniqueidentifier`、`SessionKeyHash binary(32)`、`MemberUserId nvarchar(450) NULL`、`SkuPublicId uniqueidentifier NULL`、`OrderPublicId uniqueidentifier NULL`、`Position int NULL`、`AttributedAmount decimal(18,2) NULL`、`Locale varchar(10)`、`OccurredAtUtc datetime2(3)`、`EventIdempotencyKey nvarchar(128)` | Idempotency UX；不保存原始搜尋全文；`IX_AiSearchFunnel_SearchPublicId_OccurredAtUtc` |

客服對話保存 180 天後刪除內容或匿名化識別，Audit／用量摘要依各自政策保留；其他顧客的對話不可作為 RAG 知識來源。

## Import、Outbox、冪等與稽核

| Table／Profile | 額外欄位 | Constraint／Index |
|---|---|---|
| `ImportBatches`／MutableEntity | `ImportType varchar(24)`、`TemplateVersion int`、`Status varchar(24)`、`CreatedByAdminUserId nvarchar(450)`、`SourceFileHash1/2/3 binary(32) NULL`、`SourceFileNameDisplay1/2/3 nvarchar(255) NULL`、`RowCount int`、`New/Updated/Unchanged/ErrorCount int`、`ResultSummaryJson nvarchar(4000) NULL`、`NormalizedContentVersion int`、`CorrelationId uniqueidentifier`、`ExpiresAtUtc/ConfirmedAtUtc datetime2(3) NULL` | Product Import 三組來源對應 Products／Skus／Specifications；Inventory Adjustment 只使用第 1 組，第 2／3 組 Null；同建立者＋Type 最多一個未結束 Filtered UX；`IX_ImportBatches_Status_ExpiresAtUtc`；不得另存舊單一 ContentHash Schema |
| `ImportRows` | `Id bigint identity`、`ImportBatchId bigint`、`Dataset varchar(32)`、`SourceRowNumber int`、`ImportKey nvarchar(64)`、`Action varchar(16)`、`NormalizedPayloadJson nvarchar(max)`、`RawJson nvarchar(max) NULL`、`RowHash binary(32)`、`ErrorCodes nvarchar(2000) NULL` | 正式名稱固定為 `Dataset`／`RawJson`，不得改用 `DatasetType`／`RawPayloadJson`；Batch Cascade 白名單；Application 限兩個 JSON 各 32 KB；`UX_ImportRows_Batch_Dataset_Row`；批次內 ImportKey 唯一 |
| `OutboxMessages`／Entity | `EventId uniqueidentifier`、`EventType nvarchar(128)`、`SchemaVersion int`、`AggregateType nvarchar(64)`、`AggregatePublicId uniqueidentifier`、`PayloadJson nvarchar(8000)`、`OccurredAtUtc/AvailableAtUtc datetime2(3)`、`ProcessedAtUtc datetime2(3) NULL`、`AttemptCount int`、`Status varchar(24)`、`LastErrorCode nvarchar(64) NULL` | EventId UX；`IX_OutboxMessages_Status_AvailableAtUtc`；Payload 最小化 |
| `IdempotencyRecords`／MutableEntity | `Scope nvarchar(128)`、`KeyHash binary(32)`、`RequestHash binary(32)`、`Status varchar(16)`、`ResponseStatus int NULL`、`ResponseBody nvarchar(8000) NULL`、`ExpiresAtUtc datetime2(3)` | `UX_IdempotencyRecords_Scope_KeyHash`；相同 Key 不同 RequestHash 回 409 |
| `AuditLogs`／Entity | `ActorUserPublicId uniqueidentifier NULL`、`ActorRoleSnapshot nvarchar(500) NULL`、`Action nvarchar(128)`、`EntityType nvarchar(64)`、`EntityPublicId uniqueidentifier NULL`、`Outcome varchar(24)`、`BeforeJson/AfterJson nvarchar(16000) NULL`、`Reason nvarchar(1000) NULL`、`IpAddress varchar(45) NULL`、`TraceId nvarchar(64)`、`OccurredAtUtc datetime2(3)`、`RetentionUntilUtc datetime2(3) NULL`、`LegalHold bit` | append-only；`IX_AuditLogs_EntityType_EntityPublicId_OccurredAtUtc`、`IX_AuditLogs_Actor_OccurredAtUtc` |

Audit 差異使用欄位白名單並遮蔽個資；不得保存密碼、Token、Cookie、TOTP Seed、Recovery Code 或附件內容。Hangfire 自有 Schema 不併入領域資料字典，Job 業務結果以 Outbox／Audit／領域狀態追蹤。

Owner／Assignee 等現行作業欄位使用一般 Identity FK；append-only History 的 `ActorUserId` 可為 Null 並使用 Identity FK。存在交易相依時，Identity 採停權、軟刪除或匿名化，不實體刪除。中央 `AuditLogs` 保存不可變 Actor PublicId、角色、操作、理由與結果；各 History 不重複保存完整 Audit Snapshot。

## 正規化審核摘要

| 資料 | 分類 | 同步／延遲 | 重建與防漂移 |
|---|---|---|---|
| Identity／Profiles | none | 同交易；零延遲 | AccountType＋一對一整合測試、Security Stamp |
| Support current state | current state＋append history | 同交易 | Assignment／SLA Event 核對 |
| AI Tool／Citation | transaction_snapshot | AI 回應提交時 | Contract Version、Request／Result 測試 |
| AI Funnel | analytics_event | 最多 Outbox＋Job 延遲 60 秒 | Event Idempotency、漏斗分母核對 |
| Outbox | integration source | 同交易寫入，5 秒派送 | EventId、重試、Dead Letter／核對工作 |
| Audit | immutable audit | 重要操作同交易或可靠 Outbox | Append-only 權限與定期抽查 |

統一案件工作台仍使用三領域 `UNION ALL` View，不建立第四個可寫案件資料表。
