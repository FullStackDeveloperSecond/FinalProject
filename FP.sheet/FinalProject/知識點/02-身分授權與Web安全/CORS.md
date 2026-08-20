---
type: knowledge
title: CORS（跨來源資源共享）
aliases:
  - Cross-Origin Resource Sharing
  - 跨來源資源共享
tags:
  - 知識點
  - 安全
  - Web
  - API
  - CORS
created_at: 2026-08-10
related:
  - "[[知識點/02-身分授權與Web安全/ASP.NET Core Identity]]"
  - "[[知識點/02-身分授權與Web安全/CSRF]]"
  - "[[03-架構/01-系統與環境/系統架構]]"
---

# CORS（跨來源資源共享）

## Origin 是什麼

瀏覽器以 **scheme＋host＋port** 判斷 Origin。任一部分不同，就是不同 Origin：

| URL | 與 `https://shop.example.com` 是否同 Origin |
|---|---|
| `https://shop.example.com/products` | 是 |
| `http://shop.example.com` | 否，scheme 不同 |
| `https://api.example.com` | 否，host 不同 |
| `https://shop.example.com:8443` | 否，port 不同 |

開發時 Vue 可能在 `https://localhost:5173`，API 在 `https://localhost:7001`，因 port 不同而屬跨 Origin。

## CORS 解決什麼問題

瀏覽器的 Same-Origin Policy 預設限制前端 JavaScript 讀取其他 Origin 的回應。CORS 讓 API 透過回應 Header 明確告訴瀏覽器：哪些 Origin、HTTP Method、Header 及 Credential 可以跨來源使用。

```text
Vue Origin 發出跨來源 fetch
→ 瀏覽器檢查是否需要 Preflight
→ API 回傳允許的 CORS Headers
→ 瀏覽器決定是否讓 Vue 取得回應
```

CORS 由瀏覽器執行，不是 API Authentication，也不能阻止 curl、Postman 或其他伺服器呼叫 API。API 仍需完整的身分驗證、授權、輸入驗證及速率限制。

## ASP.NET Core 設定

Cookie Authentication 需要允許 Credentials，這時 Origin 必須是精確白名單：

```csharp
const string SpaPolicy = "SpaOrigins";

builder.Services.AddCors(options =>
{
    options.AddPolicy(SpaPolicy, policy =>
    {
        policy
            .WithOrigins(
                "https://localhost:5173",
                "https://admin.localhost:5174")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
```

```csharp
app.UseRouting();
app.UseCors(SpaPolicy);
app.UseAuthentication();
app.UseAuthorization();
```

依 Microsoft 文件，`UseCors` 應位於 `UseRouting` 之後、`UseAuthorization` 之前。實際 Middleware 順序需與專案其他安全元件一併整合測試。

前端也要主動允許 Cookie：

```ts
fetch(apiUrl, {
  credentials: 'include',
})
```

只設定後端 `AllowCredentials` 而前端沒有 `credentials: 'include'`，跨來源 Cookie 仍不會按預期工作。

## Preflight

非簡單跨來源請求送出前，瀏覽器通常先發送 `OPTIONS` Preflight，詢問 API 是否允許實際請求：

```http
OPTIONS /api/orders
Origin: https://localhost:5173
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type,x-xsrf-token,x-correlation-id
```

API 必須允許實際使用的 Method 與 Header，包括 JSON `Content-Type`、Antiforgery Header 及 Correlation ID。Preflight 失敗時，真正的 POST 可能根本沒有送出。

## 安全規則

- Origin 由設定檔分環境管理，不把使用者輸入直接當允許來源。
- `WithOrigins` 的 URL 不加結尾 `/`，否則可能比對失敗。
- 使用 Cookie Credentials 時禁止 `AllowAnyOrigin()`。
- 不反射任意 Request `Origin` 成為 `Access-Control-Allow-Origin`。
- 正式環境只允許 HTTPS 的前台與後台 Origin。
- 僅開放實際需要的 Method、Header 與 exposed header；第一版若使用 `AllowAnyHeader`，仍以精確 Origin 限縮風險。
- 不把「通過 CORS」視為已登入或已授權。

Microsoft 明確指出 `AllowAnyOrigin` 與 `AllowCredentials` 的組合不安全，ASP.NET Core 也會回傳無效的 CORS 結果。

## CORS 與 CSRF 的差異

| 問題 | CORS | CSRF 防護 |
|---|---|---|
| 核心目的 | 控制瀏覽器是否讓前端讀取跨來源回應 | 防止惡意網站冒用 Cookie 身分送出有效操作 |
| 執行者 | 瀏覽器 | 伺服器驗證 Antiforgery Token |
| 是否取代 Authentication | 否 | 否 |
| Cookie SPA 是否通常需要 | 前後端不同 Origin 時需要 | 使用 Cookie 驗證時需要 |

若正式部署由反向代理讓 Vue 與 API 共用同一 Origin，可以不啟用跨來源 CORS；但只要仍使用自動送出的 Cookie，CSRF 威脅依然存在。

## 測試案例

- 白名單 Origin 的 Preflight 與實際請求成功。
- 未列入 Origin 時沒有允許 Header，前端無法讀取回應。
- 允許 Origin 但缺少 Credentials 設定時行為符合預期。
- `X-XSRF-TOKEN`、`X-Correlation-ID` 與 `Content-Type` 能通過 Preflight。
- 401、403 及 Antiforgery 失敗回應仍包含必要 CORS Header，讓 Vue 能正確處理錯誤。
- 正式設定沒有 localhost、HTTP 或萬用 Origin。

> [!warning] 專案決策邊界
> DEC-P50 已確認只允許設定檔列出的前台與後台 Origin、允許 Credentials、使用 Antiforgery Header，並禁止 `AllowAnyOrigin`；正式規則見 [[03-架構/02-API與前端契約/API共通規範]]。

## 參考資料

- [Microsoft Learn：ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
- [[知識點/02-身分授權與Web安全/CSRF]]
- [[知識點/02-身分授權與Web安全/ASP.NET Core Identity]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
