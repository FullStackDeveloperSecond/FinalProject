---
type: knowledge
title: Antiforgery（ASP.NET Core 防偽機制）
aliases:
  - Anti-forgery Token
  - Request Verification Token
  - XSRF Token
tags:
  - 知識點
  - 後端
  - 安全
  - ASP.NET Core
  - Antiforgery
  - CSRF
created_at: 2026-08-10
related:
  - "[[知識點/CSRF]]"
  - "[[知識點/Credentials]]"
  - "[[知識點/CORS]]"
  - "[[知識點/ASP.NET Core Identity]]"
---

# Antiforgery（ASP.NET Core 防偽機制）

## 與 CSRF 的差別

[[知識點/CSRF|CSRF]] 是攻擊類型；Antiforgery 是 ASP.NET Core 用來防禦這類攻擊的具體機制。

```text
CSRF：問題與威脅模型
Antiforgery：伺服器發 Token、前端送回、伺服器驗證
```

它主要用於瀏覽器以 Cookie 自動驗證身分的情境。完全不依賴瀏覽器 Cookie、改用呼叫端主動加入 Bearer Token 的 API，CSRF 威脅模型不同，但仍需防範 XSS、Token 外洩及重放。

## Token Pair

ASP.NET Core Antiforgery 會使用相互關聯的 Cookie Token 與 Request Token：

```text
Cookie Token
→ 瀏覽器自動隨目標網站請求送出

Request Token
→ 合法 Vue 程式取得
→ 主動放入 X-XSRF-TOKEN Header

伺服器驗證兩者是否有效且匹配目前身分
```

登入 Cookie 必須維持 `HttpOnly`。為了讓 Vue 能加入 Header，Request Token 可以透過受控 Endpoint 或 JavaScript 可讀 Cookie 提供；這不代表應讓 JavaScript 讀取登入 Cookie。

## ASP.NET Core 設定概念

```csharp
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});
```

Token Endpoint 可使用 `IAntiforgery.GetAndStoreTokens` 產生並保存 Token pair，再把 Request Token 提供給前端：

```csharp
app.MapGet("/antiforgery/token", (
    IAntiforgery antiforgery,
    HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);

    context.Response.Cookies.Append(
        "XSRF-TOKEN",
        tokens.RequestToken!,
        new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
        });

    return Results.NoContent();
});
```

這是機制示意，不是完整專案程式。Token Endpoint 是否匿名、Cookie SameSite、Path、CORS 及登入前後更新流程，必須依實際部署拓撲定案。

## 驗證範圍

所有會改變狀態的瀏覽器端點原則上驗證：

```text
POST、PUT、PATCH、DELETE → 驗證
GET、HEAD、OPTIONS、TRACE → 不應改變資料
```

登入、登出、變更密碼、Email、TOTP、角色與管理員操作不可因為「尚未登入」或「只是登出」就自動排除。

`AddControllers()` 本身不會替 JSON Web API 自動完成整套 Antiforgery 保護。專案必須明確註冊服務，並以全域 Filter、Endpoint Filter、Middleware 或端點 Metadata 套用驗證；不能只產生 Token 卻忘記在寫入端點驗證。

## Vue Wrapper 流程

```text
App 啟動／登入頁載入
→ 取得 Request Token
→ 共用 wrapper 保存或讀取 Token
→ unsafe method 自動加入 X-XSRF-TOKEN
→ fetch 同時使用 credentials: 'include'
→ 身分改變後重新取得 Token
```

```ts
function isUnsafeMethod(method: string) {
  return ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method.toUpperCase())
}
```

前端不得把 Antiforgery 失敗當成一般網路失敗無限重送。可重新取得 Token 一次；若仍失敗，停止副作用請求並顯示重新整理或重新登入提示。

## 它不能防什麼

- 不能取代 Authentication 或 Authorization。
- 不能防止合法登入者執行未授權操作。
- 不能阻止 XSS；惡意同源 JavaScript 可能取得 Request Token 並發送請求。
- 不能取代輸入驗證、CORS、HTTPS、SameSite Cookie 或速率限制。
- 不能讓有副作用的 `GET` 變安全，必須先修正 HTTP Method。

## 測試清單

- Cookie＋正確 Token：成功。
- Cookie＋缺少、錯誤或其他 Session Token：拒絕。
- 正確 Origin 但缺少 Token：仍拒絕。
- 登入前取得 Token並完成登入；登入後舊 Token 行為符合設計。
- 登出、切換會員／管理員 Session、密碼變更後重新取得 Token。
- Preflight 允許 `X-XSRF-TOKEN` Header。
- API 回傳穩定錯誤格式與 Trace ID，不洩漏 Token 值。

> [!note] 專案採用方式
> 全域保護 Cookie 認證的非安全方法；前端由 `GET /api/v1/security/antiforgery-token` 取得 Request Token，以 `X-XSRF-TOKEN` Header 傳送，失敗回 400＋`antiforgery_validation_failed`。精確 ASP.NET Core 註冊程式屬實作，正式邊界見 [[03-架構/API共通規範]]。

## 參考資料

- [Microsoft Learn：防範 ASP.NET Core CSRF／XSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [[知識點/CSRF]]
- [[知識點/Credentials]]
- [[05-規劃/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
