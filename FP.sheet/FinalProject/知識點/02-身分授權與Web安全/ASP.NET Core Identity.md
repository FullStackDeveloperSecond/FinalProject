---
type: knowledge
title: ASP.NET Core Identity
aliases:
  - Identity
  - ASP.NET Identity
tags:
  - 知識點
  - 後端
  - 身分驗證
  - 授權
  - ASP.NET Core
  - Identity
created_at: 2026-08-10
related:
  - "[[02-領域需求/01-會員與身分/會員、驗證與通知]]"
  - "[[03-架構/01-系統與環境/系統架構]]"
  - "[[知識點/02-身分授權與Web安全/CSRF]]"
  - "[[知識點/02-身分授權與Web安全/CORS]]"
  - "[[知識點/02-身分授權與Web安全/TOTP]]"
  - "[[知識點/02-身分授權與Web安全/RBAC與Policy授權]]"
---

# ASP.NET Core Identity

## 它解決什麼問題

ASP.NET Core Identity 是 ASP.NET Core 的會員與登入基礎設施，負責管理本機使用者帳號及常見的身分驗證流程，例如：

- 使用者、密碼雜湊與登入。
- Email 確認與忘記密碼 Token。
- 帳號鎖定、登入失敗次數與安全戳記。
- 角色、Claim 及 Policy 授權所需的身分資料。
- TOTP 雙因素驗證與復原碼。
- Cookie 或 Identity 內建 Bearer Token 的驗證流程。

它不是完整的商品會員領域，也不是商業權限規則本身。會員等級、收件地址、訂單、客服案件等資料仍應放在各自的領域模型，只以穩定的 Identity User ID 建立關聯。

## Authentication 與 Authorization

兩個概念要分開：

```text
Authentication（身分驗證）
→ 確認「你是誰」
→ 例如登入 Cookie 能還原 UserId

Authorization（授權）
→ 判斷「你能做什麼」
→ 例如只有 CatalogManager 能修改商品
```

Identity 可以提供登入者、角色與 Claim，但 API 仍需以 `[Authorize]`、Policy 或 `.RequireAuthorization()` 保護端點。只在 Vue 隱藏按鈕不算授權。

## 主要資料元件

採 EF Core Store 時，常見類型包含：

```text
ApplicationUser          使用者帳號
IdentityRole             角色
UserManager              建立、更新、Token、密碼及 Claim
SignInManager            登入、登出及 2FA 流程
IdentityDbContext        Identity 的 EF Core 資料表與關聯
```

密碼只交給 Identity 的 Password Hasher，系統不得保存明文、可逆加密密碼或自行設計雜湊格式。

## Vue SPA 的登入方式

Microsoft 對瀏覽器型應用建議優先使用 Cookie，因為 Cookie 可由瀏覽器處理，且設定 `HttpOnly` 後不暴露給 JavaScript。

```text
Vue 提交帳號、密碼
→ Identity 驗證
→ 後端回傳安全登入 Cookie
→ 瀏覽器後續請求自動帶 Cookie
→ ASP.NET Core 還原 HttpContext.User
```

前端跨 Origin 呼叫 API 時需要：

```ts
fetch(url, {
  credentials: 'include',
})
```

Cookie 應依部署拓撲設定：

- `HttpOnly = true`：避免 JavaScript 直接讀取登入 Cookie。
- `Secure = true`：只經 HTTPS 傳輸。
- 適當的 `SameSite`、Domain、Path 與期限。
- 會員與管理員若分開 Session，使用不同 Cookie 名稱及 Authentication Scheme。

Cookie 會自動隨請求送出，因此所有會改變狀態的瀏覽器 API 必須搭配 [[知識點/02-身分授權與Web安全/CSRF|Antiforgery／CSRF 防護]]。Vue 與 API 不同 Origin 時，另需設定 [[知識點/02-身分授權與Web安全/CORS|CORS]]。

## Identity API Endpoints

ASP.NET Core 可用 `AddIdentityApiEndpoints<TUser>()` 與 `MapIdentityApi<TUser>()` 提供註冊、登入、Refresh、Email 確認、忘記密碼、重設密碼、2FA 與帳號資訊等基本 API。

```csharp
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

app.MapIdentityApi<ApplicationUser>();
```

產生的預設端點只是起點。專案仍要決定：

- 是否開放自行註冊及要求 Email 確認。
- 統一錯誤格式與避免帳號枚舉。
- 登入速率限制、鎖定規則及稽核。
- 管理員是否強制 TOTP。
- Cookie Scheme、Session 期限、滑動續期及全部登出。
- 如何將 Identity User 對應會員與員工領域資料。

Identity 的內建 Token 模式不是 JWT，也不是完整的 OAuth／OpenID Connect 身分服務。若未來要支援第三方 App、對外 API 或聯合登入，需要另外評估標準身分提供者。

## 角色、Claim 與 Policy

角色適合表達穩定的工作職責，Policy 則能組合角色、Claim 與其他條件：

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManageCatalog", policy =>
        policy.RequireRole("CatalogManager", "Administrator"));
});
```

高風險行為仍需在 Application Use Case 檢查資源所有權及目前業務狀態。例如「Customer 角色」不代表可以讀取所有訂單，只能讀取屬於自己的訂單。

## 安全生命週期

- 修改密碼、停權、角色異動或疑似入侵時，更新 Security Stamp 或撤銷相關 Session。
- Email 確認與密碼重設 Token 必須短效、一次性並經 URL 安全編碼。
- 登入、失敗、鎖定、TOTP、復原碼及管理員權限異動要留下稽核紀錄。
- 不在日誌記錄密碼、Cookie、Token、TOTP Shared Key 或復原碼。
- 前後台都需針對 `401`（未登入）與 `403`（已登入但無權限）提供一致行為。

> [!warning] 專案決策邊界
> DEC-P47～DEC-P49 已確認採 Identity＋HttpOnly Cookie、會員與管理員獨立 Cookie Scheme，以及不同 Session 期限；正式規則見 [[03-架構/02-API與前端契約/API共通規範]]。

## 參考資料

- [Microsoft Learn：使用 Identity 保護 SPA Web API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
- [Microsoft Learn：ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [[02-領域需求/01-會員與身分/會員、驗證與通知]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-003-開發前置與核心流程決策]]
