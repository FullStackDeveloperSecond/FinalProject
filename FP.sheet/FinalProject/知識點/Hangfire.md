---
type: knowledge
title: Hangfire
aliases:
  - Hangfire Background Jobs
tags:
  - 知識點
  - 後端
  - 背景工作
  - 排程
  - Hangfire
  - SQL Server
created_at: 2026-08-09
related:
  - "[[03-架構/系統架構]]"
  - "[[知識點/Brevo SMTP]]"
  - "[[知識點/SLA]]"
---

# Hangfire

## 定位

Hangfire 是 .NET 的持久化背景工作框架。應用程式把方法名稱、型別與參數等工作資訊寫入 Storage，Hangfire Server 再從 Storage 取出並執行。

它適合本專案的：

- Email 與站內通知。
- 訂單庫存保留逾時釋放。
- SLA 到期檢查與提醒。
- 定期報表、銷售預測與異常偵測。
- AI 對話或暫存資料清除。

## 工作類型

```text
Fire-and-forget：儘快執行一次
Delayed：指定時間後執行
Recurring：依排程反覆建立工作
Continuation：前一工作成功後接續執行
```

大量批次與進階限流功能可能屬 Hangfire Pro，不應在未確認授權前把付費功能列為必要需求。

## ASP.NET Core 與 SQL Server

概念設定如下，實際套件版本需在實作時確認：

```csharp
builder.Services.AddHangfire(configuration =>
    configuration.UseSqlServerStorage(hangfireConnectionString));

builder.Services.AddHangfireServer();
```

```csharp
app.UseHangfireDashboard("/hangfire", dashboardOptions);
```

Hangfire 的資料表及索引用來保存工作、狀態、佇列、Server 與鎖定資訊。若專案不允許應用程式啟動時自動建立或更新 schema，應設定 `PrepareSchemaIfNecessary = false`，並把官方 SQL 納入受控部署流程。

## 工作參數設計

Hangfire 會序列化參數。不要把大型物件、EF Entity、檔案內容、秘密或短生命週期服務直接當參數；只傳穩定識別碼：

```csharp
BackgroundJob.Enqueue<IEmailJob>(
    job => job.SendOutboxItemAsync(outboxId, CancellationToken.None));
```

執行時再用 `outboxId` 查詢最新資料與狀態。工作方法應是公開、可由 DI 解析，並讓取消權杖與逾時策略能生效。

## 重試與冪等

Hangfire 遇到例外會依設定自動重試；官方預設的 `AutomaticRetry` 行為不應被當成業務保證。每種工作需明確決定次數、間隔及失敗後處理。

背景工作可能因重試、程序中斷或結果確認失敗而再次執行，因此必須設計冪等：

- Email：以 Outbox ID 判斷是否已完成，並保存嘗試紀錄。
- 庫存釋放：只把仍為 Active 且已到期的保留轉為 Released。
- 通知：以事件 ID＋通知類型建立唯一鍵。
- 報表：以期間＋模型版本 upsert，不盲目新增重複資料。

```text
讀取目前狀態
→ 已完成：安全結束
→ 可執行：以交易／併發控制更新
→ 執行外部副作用
→ 記錄結果
```

資料庫交易與 enqueue 不是天然同一件事。需要「業務資料提交後必定建立工作」時，使用 Transactional Outbox，由排程器可靠地把 Outbox 轉成 Hangfire 工作。

## Recurring Job 注意事項

- 使用固定且可讀的 Job ID，重複部署時更新同一排程。
- 明確設定時區；不要默認開發機與正式機時區相同。
- 工作開始後重新查詢符合條件的資料，不能只相信排程建立時的狀態。
- 避免上一次未完成、下一次又重疊執行；以資料庫鎖、狀態或適當的工作控制處理。
- 逾時判斷使用業務時間 `DueAt`，不能使用工作實際執行時間代替。

## Dashboard 安全

Hangfire Dashboard 會顯示方法名稱、序列化參數，且可重試、刪除或觸發工作，屬高權限管理介面。

- 只允許授權的系統管理角色。
- 不因預設只允許本機就省略正式授權設計。
- 不在工作參數放 token、密碼、Email 正文或個資。
- 啟用 HTTPS、稽核管理操作，並避免直接暴露到公開網路。

## 監控與維運

至少監控：

- Enqueued、Scheduled、Processing、Succeeded、Failed 數量。
- 最舊待處理工作的等待時間。
- 各 Queue 處理量與失敗率。
- 重試次數、最後錯誤及執行時間。
- Hangfire Server 心跳與 SQL Server 可用性。

應依工作重要性分 Queue，例如 `critical`、`mail`、`reports`，避免耗時報表阻塞庫存釋放。

> [!note] 專案決策邊界
> 專案已確認 Hangfire＋SQL Server、四個 Queue、4 Workers、3／2／0 類型化重試、SuperAdmin＋TOTP 唯讀 Dashboard、Transactional Outbox、20 筆／5 秒 Dispatcher 與人工稽核重送。只剩套件安裝、Schema、Job／Consumer 程式及整合測試待實作，詳見 [[03-架構/背景工作與Hangfire設計]]。

## 參考資料

- [Hangfire：Getting Started](https://docs.hangfire.io/en/latest/getting-started/)
- [Hangfire：Using SQL Server](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html)
- [Hangfire：Dealing with Exceptions](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)
- [Hangfire：Using Dashboard UI](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html)
- [[05-規劃/決策/00-互動中/DEC-BATCH-002-第二批核心決策]]
