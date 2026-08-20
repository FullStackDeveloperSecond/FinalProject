---
type: knowledge
title: Brevo SMTP
aliases:
  - Brevo SMTP Relay
  - Sendinblue SMTP
tags:
  - 知識點
  - Email
  - SMTP
  - Brevo
  - 基礎設施
created_at: 2026-08-09
related:
  - "[[02-領域需求/01-會員與身分/會員、驗證與通知]]"
  - "[[知識點/07-基礎設施與交付/Hangfire]]"
  - "[[知識點/02-身分授權與Web安全/Secrets管理]]"
  - "[[知識點/04-資料模型與一致性/Transactional Outbox]]"
---

# Brevo SMTP

## 定位

Brevo SMTP Relay 讓應用程式透過標準 SMTP 寄送驗證信、忘記密碼、訂單與通知 Email。使用 SMTP 時需要的是 **SMTP Key**，不是 Brevo API Key。

本專案可在 Application 層定義 `IEmailSender`，由 Infrastructure 層提供 Brevo 實作，避免領域與使用案例直接依賴服務商。

```text
Application use case
→ IEmailSender / Email outbox
→ BrevoSmtpEmailSender
→ Brevo SMTP Relay
```

## 連線設定

Brevo 官方文件列出：

- 加密連線：port `465`，使用 SSL/TLS。
- 其他 relay port：`587` 或 `2525`；是否及如何升級加密須依官方目前設定與 SMTP Client 行為確認。
- 使用 Brevo 後台提供的 SMTP username 與 SMTP key 驗證。

正式環境應優先採明確加密設定，且在啟動時驗證必要設定是否存在。

```json
{
  "Email": {
    "Host": "smtp-relay.brevo.com",
    "Port": 465,
    "SenderAddress": "no-reply@example.com",
    "SenderName": "電腦電商系統"
  }
}
```

SMTP username 與 key 不應放在 `appsettings.json`、Git、前端環境變數或日誌中。開發機使用 .NET User Secrets；部署環境使用平台 Secret Store 或環境秘密。

## 寄件者與網域

- 在 Brevo 驗證寄件者或寄件網域。
- 依 Brevo 後台指示設定 SPF、DKIM 等 DNS 驗證。
- `From` 使用已驗證的自有網域地址。
- `Reply-To` 可指向客服信箱，但不要讓使用者自由控制標頭。
- 交易信與測試信使用不同設定或明確標記，避免測試誤寄真實顧客。

## 應用程式介面

`IEmailSender` 應接收業務語意資料或已渲染訊息，不要讓呼叫端知道 SMTP 細節：

```csharp
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken);
}
```

至少限制收件者、主旨、HTML／純文字內容、追蹤用關聯 ID。密碼重設與驗證連結應使用短效、一次性 token，Email 內不要包含密碼或敏感個資。

## 可靠寄送

不要在資料庫交易尚未提交前就寄信，也不要讓 SMTP 短暫失敗使已成功的訂單交易回滾。較穩定的流程是：

```text
同一資料庫交易：完成業務資料＋寫入 EmailOutbox
→ Hangfire 讀取待寄項目
→ 呼叫 Brevo SMTP
→ 成功後記錄 ProviderMessageId／SentAt
→ 暫時性錯誤有限重試
→ 永久性錯誤進入 Failed 並供人工查看
```

工作可能重試，因此 Outbox 項目需要穩定 ID 與冪等保護。即使應用程式只送一次，SMTP 接受後連線中斷仍可能造成結果不確定；通知內容應能容忍極少數重複寄送。

## 錯誤分類與日誌

- 驗證失敗、寄件者未驗證、無效地址：通常需修正設定或資料，不應無限重試。
- 連線逾時、暫時性 4xx：可採退避重試。
- 服務商接受訊息不等於最終送達；退信與投遞狀態若是需求，需另接 webhook 或查詢 API。
- 日誌保存 Outbox ID、模板、收件網域、嘗試次數及 SMTP 狀態；避免記錄 token、完整正文與 SMTP key。

## 開發與測試

- 開發環境預設注入不對外寄送的 `LocalEmailSender`，將安全預覽寫入受控介面。
- 整合測試使用專用 Brevo 測試帳號與收件地址，不共用正式 key。
- 測試成功、暫時失敗、永久失敗、逾時、取消及重試後成功。
- HTML 模板同時提供純文字版本，並測試連結、編碼及不同裝置顯示。

> [!warning] 專案決策邊界
> 專案已確認第一版 `IEmailSender` 使用 Brevo SMTP；寄件網域、寄件者名稱、寄件位址及展示環境秘密設定仍待完成。

## 參考資料

- [Brevo：SMTP relay integration](https://developers.brevo.com/docs/smtp-integration)
- [[02-領域需求/01-會員與身分/會員、驗證與通知]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-002-第二批核心決策]]
