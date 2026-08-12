---
文件狀態: 部分已確認
最後更新: 2026-08-11
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
- 金額使用十進位數值與新台幣，不以浮點數計算。
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

## 分頁

列表端點統一接受：

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

## HTTP Method 與 Status Code

| 情境 | Method／Status |
|---|---|
| 查詢單筆或列表成功 | `GET 200` |
| 建立資源成功 | `POST 201`，並回傳資源位置或識別 |
| 更新或命令成功且無回應內容 | `PUT/PATCH/POST 204` |
| 真正非同步受理 | `202`；必須提供可查詢工作狀態的識別 |
| 格式、欄位、分頁或商業輸入驗證失敗 | `400` |
| 未登入或登入失效 | `401` |
| 已登入但無權限或不具資源範圍 | `403` |
| 資源不存在，或依安全策略不可揭露 | `404` |
| 狀態、併發、冪等 Payload 或唯一性衝突 | `409` |

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

穩定錯誤碼完整目錄與各端點 HTTP Status 對照仍待建立。

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

## 尚待完成

- 完整端點清單、HTTP Method 與 Status Code 矩陣。
- 穩定錯誤碼目錄。
- Idempotency 紀錄清理工作的執行細節。
