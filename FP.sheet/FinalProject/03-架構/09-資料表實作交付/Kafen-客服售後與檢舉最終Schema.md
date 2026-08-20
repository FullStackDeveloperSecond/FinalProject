---
文件狀態: 已確認
最後更新: 2026-08-19
負責人: kafen
追蹤項目:
  - DES-17
依據決策:
  - DEC-P250
  - DEC-P251
  - DEC-P255
  - DEC-P256
  - DEC-P257
  - DEC-P258
  - DEC-P259
  - DEC-P260
  - AUTO-DEC-005
---

# Kafen｜客服、退貨與檢舉最終 Schema 實作交付

> 版本：2026-08-19｜SQL Server 最終實作交付版
>
> 依據：`FinalProject` 已確認資料字典、狀態機、統一案件工作台及目前 UI。
>
> 範圍：人工客服、SLA、承接／指派、退貨申請與審核、檢舉審核、附件，以及提供其他模組使用的去識別化案件 Query／DTO。
> **AI 模型、Prompt、API 呼叫與 Token／次數限制由 Alex 負責；本模組只保存正式資料字典要求的訊息來源標記與案件摘要，並提供受控 Application Query／Use Case。**
> 權威順序：本文件是 Owner 欄位級實作交付；若與 [[03-架構/03-資料與一致性/資料字典索引]]、三份領域資料字典、[[03-架構/03-資料與一致性/狀態機設計]] 或 API 正式目錄衝突，以正式文件為準。本文件完成不代表核准建立或套用 Migration。

## 0. 全案共用規範

- 領域主鍵：`BIGINT IDENTITY`。
- 對外識別：`PublicId UNIQUEIDENTIFIER NOT NULL UNIQUE`，由 Application 產生 UUID v7；資料庫不使用 `NEWSEQUENTIALID()`。
- Identity 使用者外鍵：`NVARCHAR(450)`，對應 `AspNetUsers.Id`。
- 時間：`DATETIME2(3)` UTC，欄位以 `AtUtc` 結尾；顯示轉為 `Asia/Taipei`。
- 金額：`DECIMAL(18,2)`；不得使用 `FLOAT`。
- 可修改主表使用 `ROWVERSION`；更新時採條件更新，衝突回傳 HTTP 409。
- FK 預設 `Restrict/NoAction`；歷程、訊息、稽核及交易資料不得 Cascade Delete。
- 對外 API 只傳 `PublicId`，不得暴露 `BIGINT Id` 或 Identity `UserId`。
- 狀態使用固定英文值；前端自行轉換繁中標籤。
- 狀態、承辦人、SLA 期限變更必須與歷程新增在同一 SQL Transaction。
- 一般客服限登入會員使用；訪客退貨必須經 `GuestOrderAccessToken` 驗證。
- 統一案件工作台是唯讀 `UNION ALL` View，不建立第四張可寫案件表。

## 1. 本模組負責資料表總覽

| 資料表 | 用途 | 寫入責任 |
|---|---|---|
| `SupportTickets` | 人工客服案件目前狀態 | 客服模組 |
| `SupportMessages` | 顧客公開訊息、客服回覆與內部備註 | 客服模組 |
| `SupportAttachments` | 客服私有附件 | 客服模組／共用檔案服務 |
| `SupportAssignmentHistories` | 自領、指派、轉派與取消承接歷程 | 客服模組 |
| `SupportStatusHistories` | 客服狀態轉換歷程 | 客服模組 |
| `SupportSlaEvents` | SLA 啟動、暫停、恢復、提醒及逾時 | 客服模組／背景工作 |
| `SupportSummaries` | 保存客服案件摘要；Kafen 提供寫入 Use Case，Alex 產生內容 | 客服模組保存／AI 模組產生 |
| `ReturnRequests` | 退貨申請目前狀態 | 售後模組 |
| `ReturnItems` | 退貨訂單明細與數量 | 售後模組 |
| `ReturnInspections` | 每次收貨檢查結果 | 售後模組 |
| `ReturnAttachments` | 退貨私有佐證 | 售後模組／共用檔案服務 |
| `ReturnAssignmentHistories` | 退貨承接、指派、轉派與取消承接歷程 | 售後模組 |
| `ReturnStatusHistories` | 退貨狀態歷程 | 售後模組 |
| `ReturnShipments` | 退貨寄回批次、物流商及追蹤狀態 | 售後模組（Kafen） |
| `ReturnShipmentEvents` | 退貨物流事件歷程 | 售後模組（Kafen） |
| `ReportCases` | 檢舉案件目前狀態與決定 | 檢舉模組（S-03 啟用後） |
| `ReportAttachments` | 檢舉私有佐證 | 檢舉模組／共用檔案服務 |
| `ReportAssignmentHistories` | 檢舉受控領件、指派及轉派歷程 | 檢舉模組 |
| `ReportStatusHistories` | 檢舉受理、補件、判定及結案歷程 | 檢舉模組 |

## 2. `SupportTickets`

| 欄位 | SQL Server 型別 | Null | 限制／說明 |
|---|---|:---:|---|
| `Id` | BIGINT IDENTITY | 否 | PK |
| `PublicId` | UNIQUEIDENTIFIER | 否 | UX；UUID v7 |
| `TicketNumber` | NVARCHAR(32) | 否 | UX；顯示編號 |
| `MemberUserId` | NVARCHAR(450) | 否 | FK → `AspNetUsers.Id`；人工客服限會員 |
| `OrderId` | BIGINT | 是 | 建議 FK → `Orders.Id` |
| `Category` | VARCHAR(32) | 否 | `Order/Payment/Logistics/ProductWarranty/ReturnHelp/Account/Other` |
| `Subject` | NVARCHAR(200) | 否 | 1～200 字 |
| `Status` | VARCHAR(32) | 否 | `Open/Assigned/InProgress/WaitingForCustomer/WaitingForInternal/Resolved/Closed/Cancelled` |
| `Priority` | VARCHAR(16) | 否 | `Low/Normal/High/Urgent` |
| `AssigneeAdminUserId` | NVARCHAR(450) | 是 | FK → `AspNetUsers.Id`；同時只能一位承辦人 |
| `FirstResponseDueAtUtc` | DATETIME2(3) | 否 | 首次人工回覆期限 |
| `ResolutionDueAtUtc` | DATETIME2(3) | 否 | 目標結案期限 |
| `FirstHumanResponseAtUtc` | DATETIME2(3) | 是 | 第一則公開人工回覆 |
| `WaitingForCustomerStartedAtUtc` | DATETIME2(3) | 是 | 暫停起點 |
| `PausedSeconds` | INT | 否 | DEFAULT 0；CHECK ≥ 0；最多扣除 72 小時 |
| `ResolvedAtUtc` | DATETIME2(3) | 是 | 解決時間 |
| `ClosedAtUtc` | DATETIME2(3) | 是 | 正式結案時間 |
| `LastActivityAtUtc` | DATETIME2(3) | 否 | 列表排序與工作台 |
| `ReopenCount` | INT | 否 | DEFAULT 0；CHECK ≥ 0 |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) | 否 | UTC |
| `RowVersion` | ROWVERSION | 否 | 承接、回覆、狀態更新併發控制 |

### Index

- `UX_SupportTickets_PublicId`
- `UX_SupportTickets_TicketNumber`
- `IX_SupportTickets_Status_AssigneeAdminUserId_LastActivityAtUtc`
- `IX_SupportTickets_Status_FirstResponseDueAtUtc`
- `IX_SupportTickets_Status_ResolutionDueAtUtc`
- `IX_SupportTickets_MemberUserId_CreatedAtUtc`

### 承接與回覆不變量

1. 自領使用 `WHERE AssigneeAdminUserId IS NULL AND RowVersion=@expected` 條件更新；零筆受影響回傳 409。
2. 主管指派／轉派及客服取消承接，必須同步新增 `SupportAssignmentHistories`。
3. 新增客服公開回覆或內部備註前，後端重新讀取案件並確認：
   - `AssigneeAdminUserId == CurrentAdminUserId`；
   - 狀態不是 `Closed/Cancelled`；
   - Request 攜帶的 `RowVersion` 仍有效。
4. 若另一帳號已先承接，回傳 `409 support_ticket_assignment_conflict`；不得新增 `SupportMessages`。
5. 前端收到 409 後顯示最新承辦人、保留未送出文字，並要求重新整理或另存草稿。

## 3. `SupportMessages`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id` | BIGINT IDENTITY | 否 | PK |
| `PublicId` | UNIQUEIDENTIFIER | 否 | UX；UUID v7 |
| `SupportTicketId` | BIGINT | 否 | FK → SupportTickets |
| `SenderType` | VARCHAR(16) | 否 | `Member/Admin/System`；不得新增 AI 類型 |
| `SenderUserId` | NVARCHAR(450) | 是 | Member／Admin 訊息必填 |
| `Body` | NVARCHAR(4000) | 否 | 純文字；輸出 HTML Encode |
| `IsInternal` | BIT | 否 | DEFAULT 0；Internal 不得回傳前台 |
| `AiGenerated` | BIT | 否 | DEFAULT 0；只標記內容來源，不把 AI 當成角色或案件擁有者 |
| `ReplyToMessageId` | BIGINT | 是 | FK → SupportMessages |
| `Language` | VARCHAR(10) | 否 | 第一版 `zh-TW` |
| `SentAtUtc` | DATETIME2(3) | 否 | UTC |

- Index：`IX_SupportMessages_SupportTicketId_SentAtUtc_Id`。
- 第一則 `Admin + IsInternal=0` 的訊息，與 `FirstHumanResponseAtUtc` 更新同交易。
- 長對話使用 `(SentAtUtc, Id)` Cursor 分頁，不以陣列索引排序。

## 4. `SupportAssignmentHistories`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id` | BIGINT IDENTITY | 否 | PK |
| `SupportTicketId` | BIGINT | 否 | FK |
| `FromAdminUserId` | NVARCHAR(450) | 是 | 原承辦人 |
| `ToAdminUserId` | NVARCHAR(450) | 是 | 新承辦人 |
| `Action` | VARCHAR(24) | 否 | `Claim/Assign/Reassign/Unassign` |
| `Reason` | NVARCHAR(500) | 是 | 主管轉派、取消承接建議必填 |
| `ActorUserId` | NVARCHAR(450) | 是 | 實際操作帳號；系統／背景工作允許 Null |
| `OccurredAtUtc` | DATETIME2(3) | 否 | append-only |

- Index：`IX_SupportAssignmentHistories_SupportTicketId_OccurredAtUtc_Id`。

## 5. `SupportStatusHistories`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id` | BIGINT IDENTITY | 否 | PK |
| `SupportTicketId` | BIGINT | 否 | FK |
| `FromStatus` | VARCHAR(32) | 是 | 建立時 NULL |
| `ToStatus` | VARCHAR(32) | 否 | 合法狀態機值 |
| `ReasonCode` | VARCHAR(64) | 是 | 取消／重開等原因 |
| `Note` | NVARCHAR(500) | 是 | 補充說明 |
| `ActorUserId` | NVARCHAR(450) | 是 | 系統事件可 NULL |
| `OccurredAtUtc` | DATETIME2(3) | 否 | append-only |

## 6. `SupportSlaEvents`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id` | BIGINT IDENTITY | 否 | PK |
| `SupportTicketId` | BIGINT | 否 | FK |
| `EventType` | VARCHAR(32) | 否 | `Started/Paused/Resumed/Warning80/Overdue100/Reopened/Closed` |
| `TargetType` | VARCHAR(24) | 否 | `FirstResponse/Resolution` |
| `DueAtUtc` | DATETIME2(3) | 是 | 當時期限 |
| `DurationSeconds` | INT | 是 | CHECK ≥ 0 |
| `OccurredAtUtc` | DATETIME2(3) | 否 | append-only |
| `MetadataJson` | NVARCHAR(2000) | 是 | ISJSON；含 SchemaVersion；不得含個資 |

SLA：Low 24h／5d、Normal 8h／3d、High 4h／24h、Urgent 1h／8h；24×7 日曆時間。

## 6.1 `SupportSummaries`

`SupportSummaries` 只保存客服案件摘要結果，不代表 Kafen 實作 AI 模型或 OpenAI API。Alex 負責產生摘要；Kafen 提供經授權的寫入 Application Use Case。

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | 否 | PK／UX；PublicId 使用 UUID v7 |
| `SupportTicketId` | BIGINT | 否 | FK → SupportTickets |
| `SourceLastMessageId` | BIGINT | 否 | FK → SupportMessages；摘要涵蓋到的最後訊息 |
| `Summary` | NVARCHAR(1500) | 否 | 去除不必要個資的摘要內容；輸出時 HTML Encode |
| `Model` | NVARCHAR(100) | 否 | Alex 回傳的模型識別，不作授權判斷 |
| `PromptVersion` | NVARCHAR(64) | 否 | 摘要契約版本 |
| `Status` | VARCHAR(24) | 否 | `Pending/Completed/Failed/Obsolete` |
| `GeneratedAtUtc` | DATETIME2(3) | 是 | 完成產生時間 |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) | 否 | UTC |
| `RowVersion` | ROWVERSION | 否 | Mutable Entity 併發控制 |

- UX：`SupportTicketId + SourceLastMessageId`，同一訊息版本不得重複保存摘要。
- `SourceLastMessageId` 必須屬於同一 `SupportTicketId`；Application Use Case 與測試必須驗證。
- AI 失敗不得阻擋案件建立、人工回覆、承接或結案；失敗只更新摘要狀態。
- 寫入 DTO 只接受摘要、來源最後訊息 PublicId、模型與 Prompt 版本；Alex 不得直接取得 Repository／DbContext。
- 摘要不得包含內部備註、完整姓名、Email、電話、地址、`StorageKey` 或其他顧客資料。

## 7. 私有附件 Profile

`SupportAttachments`、`ReturnAttachments`、`ReportAttachments` 分表，不使用 `OwnerType + OwnerId` 多型關聯。

`SupportAttachments` 必須保留 `SupportTicketId BIGINT NOT NULL`，並增加 `SupportMessageId BIGINT NULL`，讓附件可屬於案件且選擇性連結特定訊息。另需保存 `UploadedByUserId NVARCHAR(450) NOT NULL`。下載只能經授權 API，不可提供公開實體 URL；API 必須處理格式不支援、單檔超過 10 MB、單次超過 3 檔及無權限存取。

| 共用欄位 | 型別 | 規則 |
|---|---|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | PK／UX |
| 領域 FK | BIGINT | 必須建立實體 FK |
| `OriginalFileName` | NVARCHAR(255) | 顯示前消毒 |
| `StorageKey` | NVARCHAR(500) | UX；私有儲存，不提供靜態 URL |
| `Extension/MimeType` | VARCHAR(10)／VARCHAR(100) | PNG、JPG、PDF；驗證簽章 |
| `FileSizeBytes` | BIGINT | 1～10,485,760；每案最多 3 個 |
| `Sha256` | BINARY(32) | SHA-256 原始 32 bytes |
| `ScanStatus` | VARCHAR(20) | `Pending/Clean/Rejected/Failed` |
| `ScannedAtUtc` | DATETIME2(3) NULL | ScanStatus=Clean 才可下載 |
| `RetentionUntilUtc` | DATETIME2(3) NULL | 結案後 180 天 |
| `LegalHold` | BIT | DEFAULT 0 |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) NOT NULL | MutableEntity 生命週期 |
| `DeletedAtUtc` | DATETIME2(3) NULL | 私有附件軟刪除時間 |
| `RowVersion` | ROWVERSION | 併發控制 |

## 8. `ReturnRequests`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | 否 | PK／UX |
| `ReturnNumber` | NVARCHAR(32) | 否 | UX |
| `OrderId` | BIGINT | 否 | 建議 FK → Orders |
| `RequesterUserId` | NVARCHAR(450) | 是 | 會員；訪客由 AccessToken 驗證 |
| `Status` | VARCHAR(24) | 否 | `Requested/UnderReview/Approved/AwaitingShipment/InTransit/Received/Inspecting/AwaitingRefund/Completed/Rejected/Cancelled` |
| `Priority` | VARCHAR(16) | 否 | `Low/Normal/High/Urgent`；DEFAULT `Normal` |
| `ReasonCode` | VARCHAR(64) | 否 | 受控原因 |
| `Description` | NVARCHAR(1000) | 否 | 必填退貨說明；Entity、Request DTO、前端及 Problem Details 欄位錯誤須一致驗證 |
| `AssigneeAdminUserId` | NVARCHAR(450) | 是 | 退貨審核承辦人 |
| `ReviewedByAdminUserId` | NVARCHAR(450) | 是 | 最終審核人 |
| `PolicyVersion` | INT | 否 | 申請時政策版本 |
| `Requested/Approved/Received/ClosedAtUtc` | DATETIME2(3) | 是 | 階段時間 |
| `ReturnShipmentDueAtUtc` | DATETIME2(3) | 是 | 核准後 7 日；一次延長另留 Audit |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) | 否 | UTC |
| `RowVersion` | ROWVERSION | 否 | 併發控制 |

- 退貨建立時固定為 `Normal`；後台只能經具名商業操作調整四級 Priority，並更新 `UpdatedAtUtc`。
- 工作台查詢索引為 `Status + Priority + AssigneeAdminUserId + UpdatedAtUtc`；不得依 View 即時計算或以狀態暗中改級。

### 退貨補件與溝通規則

- 第一版不建立 `ReturnMessages`，也不建立 `ReturnRequests.SupportTicketId`；兩個 Aggregate 的狀態不得互相依賴。
- 補件要求、顧客附件、審核備註、Email／站內通知紀錄都留在 Return 流程；另開 `SupportTicket` 只作一般聯絡，不是退貨狀態轉移的必要條件。
- 退貨詳情可顯示人工聯絡參考資訊，但不可回填客服 FK 或把 `SupportMessages` 當成退貨事件歷程。

### 退貨核准與退款執行責任

- 本模組只負責建立與審核退貨、驗收退貨品項、保存狀態／審核結果，以及提供退款所需的核准資料。
- 本模組不得新增或更新 `Refunds`、`RefundAllocations` 或模擬發票折讓資料；只能唯讀取得退款結果。
- 退貨核准使用正式 `ApproveReturnRequest`；退款模組另以 `ExecuteRefundRequest` 和 `Idempotency-Key` 執行退款。兩者是不同的 Application Use Case。
- 重試退款不得重複建立退款交易；退款狀態、單項分攤、完成時間與折讓結果由退款模組提供。

## 9. `ReturnItems`、`ReturnInspections`、`ReturnAssignmentHistories`、`ReturnStatusHistories`

### ReturnItems

| 欄位 | 型別 | 說明 |
|---|---|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | PK／UX |
| `ReturnRequestId` | BIGINT | FK；一案可一或多個品項 |
| `OrderItemId` | BIGINT | 建議 FK → OrderItems |
| `Quantity` | INT | >0；不得超過可退數量 |
| `RequestedRefund` | DECIMAL(18,2) | ≥0；非最終退款真實來源 |
| `InspectionStatus` | VARCHAR(24) | 檢查狀態 |
| `RestockDisposition` | VARCHAR(24) NULL | `Resellable/Quarantine/Scrap` |

- UX：`ReturnRequestId + OrderItemId`。

### ReturnInspections

`Id`、`PublicId`、`ReturnItemId FK`、`Result VARCHAR(24)`、`ConditionCode VARCHAR(64)`、`Note NVARCHAR(1000) NULL`、`InspectedByAdminUserId NVARCHAR(450)`、`InspectedAtUtc DATETIME2(3)`；每次檢查 append-only。

### ReturnStatusHistories

與 SupportStatusHistories 相同結構，FK 改為 `ReturnRequestId`；所有狀態轉換 append-only。

### ReturnAssignmentHistories

欄位：`Id`、`ReturnRequestId FK`、`FromAdminUserId NVARCHAR(450) NULL`、`ToAdminUserId NVARCHAR(450) NULL`、`Action VARCHAR(24)`、`Reason NVARCHAR(500) NULL`、`ActorUserId NVARCHAR(450) NULL`、`OccurredAtUtc DATETIME2(3)`；append-only。

- `Action`：`Claim/Assign/Reassign/Unassign`。
- 退貨審核使用正式 `Return.Approve` Policy（`OrderManager`／`SuperAdmin`）；不得建立 `AfterSalesAgent` 或泛稱 `Supervisor` 角色。
- `CustomerService` 可處理客服溝通、補件及資料彙整，但不得執行 `ApproveReturnRequest`。
- 退貨核准只能由通過 `Return.Approve` Policy 的 `OrderManager`／`SuperAdmin` 執行；不得以畫面職稱、前端按鈕或自訂額度取代正式 Policy。
- 承接與主表 `AssigneeAdminUserId`、`RowVersion` 更新必須同一 Transaction；衝突回傳 409。

## 10. `ReturnShipments`、`ReturnShipmentEvents`

`ReturnShipments` 是 Kafen 售後領域的獨立 Aggregate，不重用 Terry 的 outbound `Shipments`。Terry 只提供 Carrier／ShippingMethod Lookup；同一退貨申請最多一個未取消且未結束的有效寄回批次。

### ReturnShipments

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | 否 | PK／UX |
| `ReturnRequestId` | BIGINT | 否 | FK → ReturnRequests；最多一筆有效寄回批次 |
| `ShipmentNumber` | NVARCHAR(32) | 否 | UX；退貨物流編號 |
| `Method` | VARCHAR(24) | 否 | `HomePickup/ConvenienceStore/SelfShip` |
| `CarrierCode` | VARCHAR(32) | 是 | 受控物流商代碼 |
| `TrackingNumber` | NVARCHAR(64) | 是 | 物流追蹤碼；與 CarrierCode 建議複合 Index |
| `Status` | VARCHAR(24) | 否 | `Pending/Scheduled/PickedUp/InTransit/Delivered/Failed/Cancelled` |
| `RecipientName` | NVARCHAR(160) | 是 | 宅配取件不可變地址快照 |
| `RecipientPhone` | NVARCHAR(32) | 是 | 宅配取件不可變地址快照；授權查詢 |
| `PostalCode` | NVARCHAR(16) | 是 | 宅配取件不可變地址快照 |
| `AddressLine` | NVARCHAR(500) | 是 | 宅配取件不可變地址快照 |
| `StoreCode/StoreName` | NVARCHAR(160) | 是 | 超商寄回適用；宅配可為 Null |
| `ScheduledPickupAtUtc` | DATETIME2(3) | 是 | 預計取件時間 |
| `ShippedAtUtc` | DATETIME2(3) | 是 | 實際寄出時間 |
| `ReceivedAtUtc` | DATETIME2(3) | 是 | 退貨中心收貨時間 |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) | 否 | UTC |
| `RowVersion` | ROWVERSION | 否 | 物流狀態併發控制 |

### ReturnShipmentEvents

欄位：`Id`、`ReturnShipmentId FK`、`ExternalEventId NVARCHAR(128)`、`Source VARCHAR(32)`、`EventType VARCHAR(64)`、`EventCode VARCHAR(64) NULL`、`Description NVARCHAR(500) NULL`、`OccurredAtUtc DATETIME2(3)`、`ReceivedAtUtc DATETIME2(3)`、`PayloadHash BINARY(32) NULL`、`PayloadSummaryJson NVARCHAR(2000) NULL`；append-only，不保存不必要個資。

- `EventType` 至少支援 `Created/PickupScheduled/PickupFailed/PickedUp/InTransit/Delivered/Cancelled`。
- 必須建立 `UX_ReturnShipmentEvents_Source_ExternalEventId`，以資料庫唯一性抵抗併發重送；不得只採「先查再新增」。
- ReturnShipment 使用 Embedded Snapshot 欄位，不建立不存在的共用 `AddressSnapshots` FK；一案多次寄回屬 V1 以外範圍。

## 11. `ReportCases`

| 欄位 | 型別 | Null | 說明 |
|---|---|:---:|---|
| `Id/PublicId` | BIGINT IDENTITY／UNIQUEIDENTIFIER | 否 | PK／UX |
| `ReportNumber` | NVARCHAR(32) | 否 | UX |
| `ReporterUserId` | NVARCHAR(450) | 否 | 登入會員 |
| `TargetType` | VARCHAR(32) | 否 | `Product/ProductReview/SupportMessage/OtherPublicContent` |
| `TargetPublicId` | UNIQUEIDENTIFIER | 否 | 公開目標 ID；禁止任意 Table 名稱 |
| `ReasonCode` | VARCHAR(64) | 否 | 受控理由 |
| `Description` | NVARCHAR(1000) | 否 | 檢舉說明 |
| `Status` | VARCHAR(24) | 否 | `Open/Assigned/InReview/Actioned/Rejected/Closed` |
| `Priority` | VARCHAR(16) | 否 | Low～Urgent |
| `AssigneeAdminUserId` | NVARCHAR(450) | 是 | 檢舉審核承辦人 |
| `ResolutionCode` | VARCHAR(64) | 是 | 判定／處置代碼 |
| `DecisionNote` | NVARCHAR(1000) | 是 | 判定依據 |
| `ResolvedAtUtc/ClosedAtUtc` | DATETIME2(3) | 是 | 時間 |
| `LastActivityAtUtc` | DATETIME2(3) | 否 | 排序 |
| `CreatedAtUtc/UpdatedAtUtc` | DATETIME2(3) | 否 | UTC |
| `RowVersion` | ROWVERSION | 否 | 併發控制 |

- Index：`IX_ReportCases_Status_AssigneeAdminUserId_LastActivityAtUtc`。
- Index：`IX_ReportCases_TargetType_TargetPublicId_Status`。
- 同 `ReporterUserId + TargetType + TargetPublicId + ReasonCode + 未結案範圍` 視為重複，回傳既有案件；實作採上述四欄正規值計算 SHA-256 `OpenCaseKeyHash BINARY(32) NULL`，建立 `UX_ReportCases_OpenCaseKeyHash WHERE OpenCaseKeyHash IS NOT NULL`。案件關閉時清空 Hash，關閉後才可重建；不得只採「先查再新增」。
- 審核者不能因工作台而取得商品、會員或客服管理權；處置必須呼叫對應領域 Command。

### 檢舉正式狀態機

| 目前狀態 | 動作 | 下一狀態 | 暫定正式角色／Policy | 必填資料 |
|---|---|---|---|---|
| Open | 自領／指派 | Assigned | `CustomerService`／`CustomerServiceSupervisor` 對應 Policy | 承辦人 |
| Assigned | 開始審查 | InReview | 同上 | 審查起始紀錄 |
| InReview | 要求／收到補件 | InReview | 同上 | 以 ActionCode、Reason 與附件／通知歷程表達，不增設狀態 |
| InReview | 採取處置 | Actioned | 同上；高風險需主管覆核 | 判定與處置代碼 |
| InReview | 駁回檢舉 | Rejected | 同上；高風險需主管覆核 | 判定理由 |
| Actioned／Rejected | 結案 | Closed | 同上 | 結案摘要 |

重開以 `Closed → Open` 狀態轉移及歷程表示，不建立 `Reopened` 狀態。高風險案件由 `CustomerServiceSupervisor` 指派／覆核；一般案件可由 `CustomerService` 在專用檢舉佇列自領。

## 12. `ReportAssignmentHistories`、`ReportStatusHistories`

### ReportAssignmentHistories

欄位：`Id`、`ReportCaseId FK`、`FromAdminUserId NVARCHAR(450) NULL`、`ToAdminUserId NVARCHAR(450) NULL`、`Action VARCHAR(24)`、`Reason NVARCHAR(500) NULL`、`ActorUserId NVARCHAR(450) NULL`、`OccurredAtUtc DATETIME2(3)`；append-only。

- 檢舉處理只能使用正式角色 `CustomerService`／`CustomerServiceSupervisor` 對應的後端 Policy；不得建立 `ReportReviewer` 或泛稱 `Supervisor` 角色。
- 一般風險案件可由 `CustomerService` 依正式 Policy 從專用檢舉佇列領取；涉及個資、資安、詐騙、法律或高風險處置者必須由 `CustomerServiceSupervisor` 指派／升級並覆核。
- 承接、轉派與取消承接須以 `RowVersion` 做條件更新，並與歷程新增同一 Transaction。

### ReportStatusHistories

欄位：`Id`、`ReportCaseId FK`、`FromStatus`、`ToStatus`、`ActionCode`、`Reason`、`ActorUserId NVARCHAR(450) NULL`、`OccurredAtUtc`；append-only。

## 13. `vw_CaseWorkbench`（唯讀）

| 欄位 | 說明 |
|---|---|
| `CasePublicId` | 對外案件識別碼 |
| `CaseType` | `Support/Return/Report` |
| `CaseNumber` | 顯示用案件編號 |
| `Title` | 案件標題 |
| `Status` | 來源領域狀態 |
| `Priority` | 優先級 |
| `RequesterDisplay` | 遮罩後的申請人顯示資訊 |
| `AssigneePublicId` | 承辦管理員公開識別碼；未指派時為 NULL |
| `CreatedAtUtc` | 建立時間 |
| `LastActivityAtUtc` | 最後活動時間 |
| `SlaDueAtUtc` | SLA 到期時間；不適用時為 NULL |
| `IsOverdue` | 是否逾期 |

正式查詢契約只輸出上述 12 欄，支援 `CaseType`、`Status`、`Priority`、`AssigneePublicId`、`IsOverdue`、關鍵字及 Cursor；預設排序固定為 `LastActivityAtUtc DESC, CasePublicId DESC`，不使用 Offset／PageNumber。Cursor 同時編碼這兩個排序鍵。`CustomerReplyState`、`RowVersion`、Assignment State 與 AvailableActions 均不屬 V1 工作台投影。

工作台 View、Query DTO、API Response 與前端型別必須使用相同欄名。授權條件套用於每個 `UNION ALL` 分支；開啟或寫入案件仍須回到來源 Aggregate Endpoint，以正式 Policy 再次授權。常用索引至少覆蓋 `LastActivityAtUtc`、狀態、承辦人及常用篩選欄位。

### 13.1 V1 View 欄位來源定版

| 分支 | `Title` | `RequesterDisplay` | `Priority` | `LastActivityAtUtc` | SLA |
|---|---|---|---|---|---|
| Support | `Category` 受控代碼 | `會員` | `SupportTickets.Priority` | `SupportTickets.LastActivityAtUtc` | 首回前使用首回期限；首回後使用結案期限並計入最多 72 小時顧客等待暫停 |
| Return | `ReasonCode` 受控代碼 | `RequesterUserId` 為 Null 時 `訪客`，否則 `會員` | `ReturnRequests.Priority`，建立時 `Normal` | `ReturnRequests.UpdatedAtUtc` | Null／false |
| Report | `ReasonCode` 受控代碼 | `會員` | `ReportCases.Priority` | `ReportCases.LastActivityAtUtc` | Null／false |

`Title` 不直接輸出 Subject／Description，`RequesterDisplay` 不輸出姓名、Email、電話或內部 UserId。三分支的 `AssigneePublicId` 都由 `AdminProfiles.PublicId` 取得，未指派或 Profile 不存在時為 Null。

## 14. 案件指標 Query 與 M-15 責任邊界

`M-15` 一般與進階營運報表由 Terry 主責，不列入 Kafen 的修正或完成條件。Kafen 不負責七項營運報表的 Endpoint、公式、畫面、匯出或驗收，也不建立七組報表主表。

Kafen 只在 Terry 的報表確實需要案件指標時，提供經授權、去識別化的 Application Query／DTO；Terry 不得直接使用 Kafen 的 Repository、DbContext 或 Entity。

| 可提供案件指標 | 來源 | 去識別化輸出範圍 |
|---|---|---|
| 客服案件數與狀態分布 | SupportTickets | 日期區間、Category、Status、Priority、彙總數量 |
| 首次人工回覆 SLA | SupportTickets、SupportSlaEvents | 期間、Priority、達成／逾時數量與比率；不輸出對話內容 |
| 退貨案件處理量 | ReturnRequests | 日期區間、Status、ReasonCode、件數及處理時間彙總 |
| 檢舉案件處理量 | ReportCases | 日期區間、Status、ReasonCode、件數及處理時間彙總 |
| 附件安全狀態 | 三張附件表 | ScanStatus 與數量；禁止輸出 StorageKey、檔名及上傳者 |

- Query DTO 採欄位允許清單，只回傳彙總值，不回傳會員、管理員、案件內文或附件識別資訊。
- 使用 `DATETIME2(3)` UTC 查詢；若呈現日期，由呼叫端依正式契約轉換為 `Asia/Taipei`。
- Kafen 只保證案件指標 Query 契約與必要來源索引；M-15 Report Key、公式、財務認列與 UI 均由 Terry 負責。

## 14.1 AI 去識別化 Application Query／DTO 契約

- AI 模組只能呼叫經後端授權的 Application Query，不得直接使用本模組 Repository 或 DbContext。
- 只允許提供目前登入會員自己的 `CasePublicId`、`CaseNumber`、`Category`、`Status`、遮罩後訂單摘要、去識別化訊息文字、`CreatedAtUtc`、`LastActivityAtUtc` 與允許公開的處理摘要。
- 禁止提供姓名、Email、電話、地址、內部備註、未遮罩附件資訊、`StorageKey`、資料庫 `Id` 與其他顧客對話。
- DTO 採欄位允許清單，不得直接序列化 Entity；客服歷史對話不得成為其他顧客的共通知識來源。
- Kafen 提供 Query 介面、DTO 與客服摘要寫入 Use Case；Prompt、模型呼叫、Token／次數限制及 AI 降級由 Alex 負責。

## 15. 建議需求（其他組員／共用架構配合）

| 建議資料／能力 | Owner | 本模組需求 |
|---|---|---|
| `AspNetUsers`、`AdminProfiles`、Roles／Policies | 共用／會員組 | Member、Admin FK；`CustomerService`／`CustomerServiceSupervisor` 權限 |
| `Orders`、`OrderItems` | 訂單組 | 案件訂單摘要、退貨資格與品項 |
| `Refunds`、`RefundAllocations` | 金流組 | 退貨核准後退款狀態；售後端唯讀查詢 |
| `InventoryMovements` | 庫存組 | Resellable 後回補事件；售後端不直接改庫存 |
| 退貨地址快照契約 | Kafen 售後模組 | 使用 `ReturnShipments` Embedded Snapshot 欄位，不建立共用 `AddressSnapshots` FK |
| `Notifications`、`EmailDeliveries` | 共用組 | SLA、補件、指派與狀態通知 |
| `AuditLogs` | 共用組 | 指派、越權回覆、附件下載、審核決定與個資檢視 |
| 私有檔案服務與掃描 | 共用組 | Defender 掃描、授權下載、清理工作 |
| M-15 營運報表 | Terry | Kafen 僅提供核准的去識別化案件指標 Application Query／DTO；Terry 不得直接讀取本模組 Repository |

## 16. API／Transaction 驗收重點

1. 兩名客服同時自領，只有一名成功；另一名收到 409 與最新承辦摘要。
2. 呼叫者沒有回覆 Policy 時固定回傳 403；已有權限但 RowVersion／承辦狀態已變動時固定回傳 409，資料庫不得新增訊息。
3. 畫面未刷新但案件已被他人承接，送出前驗證失敗並顯示最新承辦人。
4. 主管指派／轉派必須保存 From、To、Actor、Reason、OccurredAtUtc。
5. 已結案或取消案件不得新增一般訊息。
6. `IsInternal=1` 訊息不得出現在會員前台。
7. 正式工作台只驗收 12 個必要欄位與正式篩選；不加入 `CustomerReplyState`、Assignment State、RowVersion 或 AvailableActions。
8. 檢舉成立／不成立與 ReportStatusHistory、Audit 同交易保存。
9. 附件未掃描通過、跨案件或無權限下載均拒絕。
10. 客服、退貨、檢舉的寫入一律導回各領域 Endpoint，不寫入工作台 View。
11. `ReturnRequests` 不建立 `SupportTicketId`；補件、附件、審核備註與通知由 Return 流程保存，另開客服案件不影響退貨狀態。
12. 檢舉操作依 `CustomerService`／`CustomerServiceSupervisor` 的正式後端 Policy；未授權時 API 回傳 403，前端角色文字不得作授權依據。
13. 未通過 `Return.Approve` Policy 的帳號不得核准退貨；前端隱藏按鈕不能取代後端授權。
14. 驗收 Kafen 擁有的獨立 ReturnShipment Aggregate、最多一個有效寄回批次、Embedded 地址快照與 Event 冪等唯一鍵。
15. Alex 寫入 SupportSummary 時只能經 Application Use Case；不得直接使用客服 Repository／DbContext。
16. Terry 查詢案件指標只能經去識別化 Application Query／DTO；不得取得案件內文、個資或 StorageKey。

## 17. 建議實作順序

1. SupportTickets、Messages、Assignment／Status Histories。
2. 承接併發、回覆授權與 409 衝突 UI。
3. SupportAttachments、SLA Events 與背景提醒。
4. ReturnRequests、Items、Inspections、Attachments、Assignment／Status Histories；不建立 SupportTicket FK。
5. ReturnShipments、ShipmentEvents、Embedded 地址快照與單一有效寄回批次限制。
6. `vw_CaseWorkbench`、複合篩選及不同領域 Policy 授權。
7. M 功能穩定後啟用 ReportCases、Attachments、Assignment／Status Histories。
8. SupportSummaries 寫入 Use Case、去識別化 AI Query／DTO 與跨顧客資料隔離測試。
9. 視 Terry 核准需求提供去識別化案件指標 Query／DTO；M-15 Endpoint、公式、畫面與驗收不在本模組範圍。

## 18. 實作與 Migration Gate

- Entity／Fluent Configuration 必須逐欄對照本文件及三份正式資料字典；跨模組只公開 Application Query／DTO，不共享 Repository／DbContext。
- `SupportMessage.ReplyToMessageId`、`SupportAttachment.SupportMessageId` 有值時必須屬同一 `SupportTicketId`，由同交易驗證與整合測試保證。
- 若 `ReturnItems` 保留目前檢驗摘要欄位，新增 `ReturnInspection` 時必須同交易同步最新摘要，並加入防漂移核對測試。
- 本文件完成只關閉 DES-17 的「Schema 文件」缺口；建立 Migration 前仍須完成 Entity、Configuration、跨模組 FK、交易／冪等測試清單及獨立 Migration Review。
