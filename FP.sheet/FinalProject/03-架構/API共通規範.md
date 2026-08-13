---
文件狀態: 已確認
最後更新: 2026-08-13
追蹤項目:
  - DES-09
  - DES-11
  - DES-12
---

# API 共通規範

本文件定義前台 Vue、後台 Vue 與 ASP.NET Core Web API 共同遵守的橫切契約。未列出的端點與業務規則仍以各領域文件為準。

## 版本與資料格式

- 第一版 API 使用 `/api/v1` 前綴。
- 日期時間以 UTC 保存與傳輸，採 ISO 8601；畫面再依使用者時區呈現。
- 持久化 UTC 時間使用 `datetime2(3)`；外部事件需要保留原始偏移時才使用 `datetimeoffset(3)`。
- 金額使用 `decimal(18,2)` 與新台幣，比例使用 `decimal(9,6)`，不以浮點數計算。
- 對外資源識別使用 Application 產生的 UUID v7 PublicId，不在路由或 DTO 暴露 `bigint identity` 內部主鍵。
- PublicId 回應固定為小寫 Guid `D` 格式；Request 可接受大小寫，OpenAPI 標示 `format: uuid`。
- 不在成功或錯誤回應中暴露 Stack Trace、連線字串、Token 或內部例外細節。

## 瀏覽器驗證與授權

- 使用 ASP.NET Core Identity 與 `HttpOnly` Cookie，不把登入 Token 放在 `localStorage` 或 `sessionStorage`。
- 會員與管理員使用不同 Cookie Scheme，使登入生命週期與權限邊界可獨立控制。
- 會員工作階段閒置 8 小時，活動時可滑動延長，但最長 7 天。
- 管理員工作階段絕對期限 2 小時，不滑動延長，且必須完成 TOTP 2FA。
- 登出、帳號停權、密碼變更或管理員 TOTP 設定變更時，使既有工作階段失效。
- 授權由 API 依身分、角色、資源所有權與業務條件執行；前端隱藏按鈕不構成授權。

## CORS 與 CSRF

- 只允許設定檔中明列的前台與後台 Origin，不使用萬用 `*`。
- 跨 Origin Cookie 請求必須啟用 Credentials，且 Cookie 屬性依實際 HTTPS 開發／展示環境設定。
- 所有會改變狀態的請求都必須附帶 Anti-forgery Token Header，API 同時驗證 Cookie 與 Token。
- `GET`、`HEAD` 等安全方法不得產生商業資料副作用。
- 前端以 `GET /api/v1/security/antiforgery-token` 取得 Request Token；回應使用 `Cache-Control: no-store`，Token 只保存在記憶體，不寫入 localStorage／sessionStorage。
- 共用 fetch wrapper 對 `POST`、`PUT`、`PATCH`、`DELETE` 加入 `X-XSRF-TOKEN`；登入、登出或切換會員／管理員 Scheme 後重新取得 Token。
- API 對 Cookie 認證的非安全方法套用全域 Antiforgery 驗證；失敗回 400 Problem Details 與 `antiforgery_validation_failed`，不回傳 Token 內容。

## 分頁

一般列表端點統一接受：

```text
pageNumber  預設 1
pageSize    預設 20，最大 100
```

回應格式：

```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

- `pageNumber < 1`、`pageSize < 1` 或 `pageSize > 100` 回傳 HTTP 400，不自動修正或忽略。
- 排序參數採可重複的 `sort=field:asc`／`sort=field:desc`；多個參數依出現順序套用。
- 每個 Endpoint 明列允許排序欄位；未知欄位或方向回傳 400，不直接拼接為 SQL。
- 每個排序最後需補不可變 ID 或 SKU Code 等穩定同值鍵，避免翻頁時項目跳動或重複。

一般列表回應型別固定為 `PageResult<T>{ items, pageNumber, pageSize, totalCount, totalPages }`。商品、分類、品牌、SKU、門市及設定版本等需要頁碼導覽或總筆數的畫面使用此基準。

只有快速變動或大筆逐列資料可使用 `cursor/pageSize` 與 `CursorPage<T>{ items, nextCursor, hasMore }`：

| Cursor 例外 | 穩定排序鍵 |
|---|---|
| 庫存保留列表 | `ExpiresAtUtc DESC, ReservationPublicId DESC` |
| 後台訂單列表 | `CreatedAtUtc DESC, OrderPublicId DESC` |
| 客服 SLA 佇列 | `DueAtUtc ASC, TicketPublicId ASC` |
| 統一案件工作台 | `LastActivityAtUtc DESC, CasePublicId DESC` |
| 匯入預覽列 | `SourceDataset ASC, SourceRowNumber ASC, ImportRowPublicId ASC` |
| 報表明細列 | 各 Report Key 定義的主要指標排序＋穩定 PublicId／Code |

- Cursor 是不透明且綁定篩選、排序與授權範圍；任一條件改變必須從第一頁開始。
- Cursor Endpoint 不承諾 `totalCount/totalPages`，前端使用「載入更多」或連續捲動，不顯示虛假的頁碼。
- 未列於上表的 Endpoint 不得自行改用 Cursor；新增例外需同步修改共通規範、Endpoint 目錄與 DTO 契約。

## HTTP Method 與 Status Code

| 情境 | Method／Status |
|---|---|
| 查詢單筆或列表成功 | `GET 200` |
| 建立資源成功 | `POST 201`，並回傳資源位置或識別 |
| 更新或命令成功且無回應內容 | `PUT/PATCH/POST 204` |
| 真正非同步受理 | `202`；必須提供可查詢工作狀態的識別 |
| 格式、欄位、分頁或商業輸入驗證失敗 | `400` |
| 上傳內容超過允許大小 | `413` |
| 上傳媒體類型、簽章或格式不支援 | `415` |
| 格式可接受但內容因惡意程式或不可處理而拒絕 | `422` |
| 未登入或登入失效 | `401` |
| 已登入但無權限或不具資源範圍 | `403` |
| 資源不存在，或依安全策略不可揭露 | `404` |
| 狀態、併發、冪等 Payload 或唯一性衝突 | `409` |
| 超過用途限流 | `429` |
| 外部依賴或安全掃描服務暫時不可用 | `503` |

- 不使用「所有結果都回 200」的自訂 Envelope 取代 HTTP 語意。
- `204` 回應不得包含 Body；需要回傳更新後資源時使用 `200`。
- 批次操作的 Request 格式錯誤使用整體 400；合法 Request 的逐筆商業成功／失敗由 Response Items 表達。

## 錯誤格式

- 錯誤回應採 RFC Problem Details。
- 每個可處理錯誤包含穩定的應用程式 `code` 與 `traceId`。
- 欄位驗證錯誤使用 `errors` 對應欄位與訊息。
- 對外訊息可安全呈現；完整例外只進受保護 Log。

範例：

```json
{
  "type": "https://example.invalid/problems/validation",
  "title": "Validation failed",
  "status": 400,
  "code": "validation_failed",
  "traceId": "...",
  "errors": {
    "email": ["Email 格式不正確"]
  }
}
```

穩定錯誤碼與 HTTP Status 對照見 [[03-架構/API錯誤碼目錄]]；各端點另於 [[03-架構/API Endpoint目錄]] 指定主要錯誤碼。

### 錯誤碼命名

- `code` 使用小寫 snake_case 的「領域＋原因」，例如 `inventory_insufficient`、`order_state_conflict`。
- 已發布錯誤碼不得改變意義或回收給另一種錯誤；需要替代時新增錯誤碼並保留相容期。
- 錯誤碼不包含語言；中文、日文、韓文顯示文字由前端語系資源或安全的伺服器訊息處理。
- 同一商業錯誤在不同 Endpoint 應使用相同 code 與 HTTP Status。

## 冪等性

- 建立訂單與執行退款必須提供 `Idempotency-Key`。
- 伺服器保存同一身分、操作與 Key 的請求指紋及結果 24 小時。
- 同 Key、同請求重送時回傳原結果，不重複建立訂單、扣庫存或退款。
- 同 Key 搭配不同 Payload 時回傳衝突錯誤。
- 金流或物流回呼依服務商事件識別碼去重；重複事件只能產生一次副作用。
- IdempotencyRecord 以 Actor Scope＋Operation＋Key 建立唯一限制，保存 Request Hash、Response 摘要、處理狀態及 24 小時到期時間；不完整保存大型或敏感 Request Body。
- 相同 Key 尚在處理中時回 `409 Conflict` 與 `Retry-After`；完成結果摘要最多 32 KB，超過時依結果資源 PublicId 重建回應。

## 併發控制

- 一般可編輯資料使用 SQL Server `rowversion` 作為樂觀鎖；版本衝突回傳 HTTP 409。
- 庫存建立保留、消耗與釋放使用資料庫交易及帶條件的原子更新，不只依賴 `rowversion`。
- 多 SKU 訂單必須全部成功才提交，任一 SKU 庫存不足則整筆回滾。
- 失敗回應需包含穩定錯誤碼，讓前端重新取得最新資料並提示使用者。

## 驗證 Token 與防暴力嘗試

| 用途 | 有效期限 |
|---|---:|
| Email 驗證 | 24 小時 |
| 密碼重設 | 1 小時 |
| 訪客訂單一次性驗證碼 | 10 分鐘 |
| 驗證後的 `GuestOrderAccessToken` | 30 分鐘 |

- Token／驗證碼必須單次使用、可撤銷，且不可寫入一般 Log。
- 會員連續登入失敗 5 次鎖定 15 分鐘；管理員連續失敗 5 次鎖定 30 分鐘。
- 成功登入後重設失敗次數；人工解鎖需要授權並保存稽核紀錄。

## 待實作驗證

- 完整端點、Method、DTO 與 Schema 已收斂於 [[03-架構/API Endpoint目錄]] 與 [[03-架構/API DTO與Schema契約]]。
- 穩定錯誤碼目錄已建立；仍需在實際 Controller／Endpoint 套用並以契約測試驗證。
- Idempotency Record 每日清除已到期且非 `Processing`／調查保留的紀錄，排程見 [[03-架構/背景工作與Hangfire設計]]。
