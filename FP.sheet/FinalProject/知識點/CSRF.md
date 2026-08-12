---
type: knowledge
title: CSRF（跨站請求偽造）
aliases:
  - Cross-Site Request Forgery
  - XSRF
  - 跨站請求偽造
  - Antiforgery
tags:
  - 知識點
  - 安全
  - Web
  - CSRF
  - Antiforgery
created_at: 2026-08-10
related:
  - "[[知識點/ASP.NET Core Identity]]"
  - "[[知識點/CORS]]"
  - "[[02-領域需求/會員、驗證與通知]]"
---

# CSRF（跨站請求偽造）

## 攻擊原理

CSRF 是 **Cross-Site Request Forgery**。攻擊者誘導已登入使用者開啟惡意網站，再利用瀏覽器會自動帶上目標網站 Cookie 的特性，冒用使用者身分送出請求。

```text
使用者已登入 shop.example.com
→ 瀏覽器保存登入 Cookie
→ 使用者開啟 evil.example
→ 惡意頁面向 shop.example.com/api/address 發送 POST
→ 瀏覽器自動附上 shop.example.com Cookie
→ 若伺服器只檢查 Cookie，可能誤認為使用者本人操作
```

攻擊者通常不需要讀到回應，只要成功觸發改密碼、改地址、下單、退款、登出等副作用就可能造成損害。

## 什麼情況需要防護

使用瀏覽器自動送出的認證資料時，尤其是 Cookie Authentication，所有改變狀態的請求都應防護：

- `POST`
- `PUT`
- `PATCH`
- `DELETE`

登入、登出、修改 Email、密碼、TOTP 與管理員操作同樣需要考慮 CSRF。`GET`、`HEAD` 等安全方法不得設計成會修改資料。

Bearer Token 若只由 JavaScript 主動加入 `Authorization` Header，通常不會被瀏覽器自動附到跨站請求，因此威脅模型不同；但 Token 存在可被 JavaScript 讀取的位置會增加 XSS 竊取風險，不能因此宣稱整體更安全。

## Antiforgery Token

常見防禦是 Synchronizer Token Pattern：伺服器產生無法由攻擊網站猜測的 Token，前端必須在改變狀態的請求中主動送回，後端驗證成功才執行。

ASP.NET Core SPA 可採：

```text
登入 Cookie：HttpOnly，JavaScript 不可讀
Antiforgery Cookie／Token：提供 request token 給合法前端
Request Header：X-XSRF-TOKEN
```

```csharp
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});
```

伺服器可用 `IAntiforgery.GetAndStoreTokens` 發出 Token，Vue 再由共用 fetch wrapper 讀取 request token，放入所有 unsafe method 的 Header：

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

Antiforgery request token 可由前端讀取，不代表登入 Cookie 也應取消 `HttpOnly`。兩者目的不同：前者證明請求來自能取得 Token 的合法前端流程，後者代表登入身分。

## Token 生命週期

- App 初始化或登入頁載入時取得 Token。
- 登入成功、登出、切換帳號或安全身分改變後重新取得。
- Token 缺漏、錯誤或過期時拒絕請求，不可自動降級為免驗證。
- 前端遇到 Antiforgery 失敗可以重新取得一次再提示使用者，不可無限重送有副作用的操作。
- Token Endpoint 的匿名／已登入存取方式要配合「登入本身是否防 CSRF」一起設計。

## CORS 不能取代 CSRF

CORS 主要限制瀏覽器能否讓 JavaScript **讀取**跨來源回應，不保證惡意來源無法**送出**請求。HTML form 等簡單跨站請求可能在沒有成功讀取回應的情況下造成副作用。

因此 Cookie 架構通常需要同時具備：

```text
精確 CORS Origin 白名單
+ Credentials 限制
+ Antiforgery Token
+ 安全 Cookie 屬性
+ HTTPS
```

`SameSite` Cookie、Origin／Referer 檢查與自訂 Header 可作為縱深防禦，但不應在未完整驗證瀏覽器與部署拓撲前，單獨取代 Antiforgery。

## 測試案例

- 正確 Cookie＋正確 Token：成功。
- 有 Cookie、沒有 Token：拒絕。
- 有 Cookie、錯誤 Token：拒絕。
- Token 屬於另一 Session：拒絕。
- 允許 Origin 但沒有 Token：仍拒絕。
- 登入、登出與切換帳號後舊 Token 的行為符合設計。
- `GET` 端點不產生資料變更。

> [!warning] 專案決策邊界
> DEC-P50 已確認明確 Origin 白名單＋Credentials，並要求所有狀態變更請求使用 Antiforgery Header；Header 名稱、Token Endpoint 與驗證套用方式仍待實作設計，正式邊界見 [[03-架構/API共通規範]]。

## 參考資料

- [Microsoft Learn：防範 ASP.NET Core CSRF／XSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [[知識點/ASP.NET Core Identity]]
- [[知識點/CORS]]
- [[05-規劃/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
