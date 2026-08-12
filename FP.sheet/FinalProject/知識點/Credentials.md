---
type: knowledge
title: Credentials（Fetch／CORS 認證資料模式）
aliases:
  - Fetch Credentials
  - CORS Credentials
  - credentials include
tags:
  - 知識點
  - 前端
  - Web
  - Fetch
  - CORS
  - Cookie
  - Credentials
created_at: 2026-08-10
related:
  - "[[知識點/CORS]]"
  - "[[知識點/CSRF]]"
  - "[[知識點/Antiforgery]]"
  - "[[知識點/ASP.NET Core Identity]]"
---

# Credentials（Fetch／CORS 認證資料模式）

## 這裡的 Credentials 是什麼

在 Fetch 與 CORS 脈絡中，Credentials 指瀏覽器隨 HTTP 請求處理的認證資料，例如：

- Cookie。
- HTTP Authentication 使用的帳號認證資訊。
- TLS Client Certificate。

`fetch` 的 `credentials` 選項決定瀏覽器是否傳送這些資料，以及是否接受回應中的 `Set-Cookie`。它不會替應用程式登入，也不會自動發明 Bearer Token；若採 Bearer Token，仍需由共用 wrapper 明確加入 `Authorization` Header。

## 三種模式

```ts
fetch(url, { credentials: 'omit' })
fetch(url, { credentials: 'same-origin' })
fetch(url, { credentials: 'include' })
```

| 模式 | 行為 |
|---|---|
| `omit` | 不傳送 Credentials，也忽略回應設定的 Credentials |
| `same-origin` | 只在同 Origin 請求傳送；Fetch 預設值 |
| `include` | 同 Origin 與跨 Origin 都允許傳送 |

本機開發若 Vue 是 `https://localhost:5173`、API 是 `https://localhost:7001`，port 不同即為跨 Origin。使用 Identity Cookie 時，前端通常需要：

```ts
await fetch('https://localhost:7001/api/me', {
  credentials: 'include',
})
```

## 前後端必須配對

跨 Origin Cookie 請求需要兩邊同時同意：

```text
Vue fetch：credentials: 'include'
+ API CORS：AllowCredentials()
+ API CORS：明確 WithOrigins(...)
+ Cookie：Secure／SameSite／Domain／Path 符合部署方式
```

典型 ASP.NET Core 設定：

```csharp
policy
    .WithOrigins("https://localhost:5173")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials();
```

使用 Credentials 時不能把允許來源設為 `*`。Fetch 標準與 ASP.NET Core 都不允許 `Access-Control-Allow-Origin: *` 搭配 Credentials 回應。

## `include` 不保證 Cookie 一定送出

`credentials: 'include'` 只是允許瀏覽器處理 Credentials，Cookie 本身仍受以下規則限制：

- `Secure` Cookie 需要 HTTPS。
- `SameSite` 可能阻止跨 Site 傳送。
- Domain 與 Path 必須涵蓋目標 URL。
- Cookie 可能已過期、被刪除或遭瀏覽器隱私政策阻擋。
- API 回應仍須通過 CORS，JavaScript 才能讀取內容。

「Same Origin」與「Same Site」不是同一概念。CORS 依 scheme、host、port 判斷 Origin；Cookie 的 SameSite 規則依 Site 判斷。兩個不同 port 可以是不同 Origin，但仍可能屬於 Same Site。

## 與 CSRF 的關係

Credentials Cookie 會由瀏覽器自動附加，正是 CSRF 風險的來源之一。因此 Cookie SPA 不能只設定 `include` 與 CORS，還要在改變狀態的請求帶 [[知識點/Antiforgery|Antiforgery Token]]：

```ts
await fetch(url, {
  method: 'POST',
  credentials: 'include',
  headers: {
    'Content-Type': 'application/json',
    'X-XSRF-TOKEN': csrfToken,
  },
  body: JSON.stringify(payload),
})
```

## 共用 Wrapper 規則

- 只對設定檔中可信任的 API Base URL 使用 `include`，不要對任意使用者輸入 URL 傳送 Credentials。
- 將 `credentials`、Antiforgery、Correlation ID 與錯誤轉換集中管理。
- 401 代表 Session 無效或未登入；403 代表已驗證但無權限，前端行為要分開。
- CORS 失敗常在 JavaScript 中呈現一般 Network Error，除錯時同時檢查瀏覽器 Console、Preflight 與 API 日誌。
- 測試同 Origin、允許跨 Origin、未允許 Origin、Cookie 被 SameSite 阻擋及 Session 過期。

> [!warning] 專案決策邊界
> DEC-P50 已確認明確 Origin 白名單＋Credentials＋Antiforgery；實際 Origin、Cookie 屬性與前端 wrapper 契約仍待環境及實作設計，正式邊界見 [[03-架構/API共通規範]]。

## 參考資料

- [WHATWG Fetch Standard：Credentials mode](https://fetch.spec.whatwg.org/#concept-request-credentials-mode)
- [MDN：Request.credentials](https://developer.mozilla.org/en-US/docs/Web/API/Request/credentials)
- [[知識點/CORS]]
- [[知識點/Antiforgery]]
- [[05-規劃/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
