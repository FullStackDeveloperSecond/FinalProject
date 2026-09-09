# Web 安全與身分驗證

## 一張表分清楚

| 名詞 | 核心問題 | DoSelect 做法 |
|---|---|---|
| Authentication | 你是誰？ | Identity 儲存帳號；Cookie scheme 驗 member、admin、guest access、admin challenge |
| Authorization | 你能做什麼？ | Role、claim、policy；資源擁有權另確認 |
| CORS | 瀏覽器能否讓某 origin 的前端讀跨來源回應？ | 明確 origins/methods/headers，並 `AllowCredentials` |
| CSRF | 瀏覽器自動帶 Cookie 時，惡意網站能否代送狀態變更？ | unsafe method 驗 Antiforgery token |
| XSS | 不可信內容能否在可信頁執行 script？ | 輸出編碼、避免不可信 HTML；HttpOnly 不能消除 XSS |
| Credentials | fetch 是否附 Cookie/HTTP auth 等 | client 設 `credentials: 'include'` |

## Cookie 登入

Identity 負責使用者、密碼雜湊、lockout、security stamp 與角色；Cookie authentication 把 principal 放入加密簽章票證。DoSelect 沒有單一 default scheme，因為顧客、管理員與訪客訂單 token 的信任邊界不同。

- `HttpOnly`：JavaScript 不能直接讀認證 Cookie。
- `Secure`：非展示 HTTP 例外時只經 HTTPS。
- `SameSite`：降低跨站攜帶風險，但不能取代 Antiforgery。
- Sliding + absolute expiry：會員可延長閒置時間但仍有絕對上限；管理員更短。
- Security stamp：改密碼、停權或敏感變更後撤銷舊 session。
- Admin TOTP：密碼後再驗第二因子；challenge cookie 短效。

## CSRF、CORS、Antiforgery 串起來

```text
前端 GET antiforgery token
  -> server 建 token 與配對 Cookie
unsafe request
  -> browser 帶認證/antiforgery Cookie
  -> wrapper 放 X-XSRF-TOKEN 與 client 身分
  -> server 驗 Cookie token + header token + 當前身分
```

CORS 不是 CSRF 防護：攻擊頁即使讀不到 response，某些請求仍可能送達。Antiforgery 也不決定合法 SPA 能否讀跨來源回應。Credentials CORS 不能搭 wildcard origin；preflight 也要在 redirect/authorization 前正確處理。

## 高機率題

### 401 與 403？

401 是沒有可接受身分；403 是身分已確認但權限不足。為避免 IDOR 洩漏其他人的資源是否存在，資源層授權可依明確政策回 404。

### Role、claim、policy？

Role 是粗粒度職務；claim 是身分屬性；policy 組合需求與 handler。訂單擁有權是 resource authorization，不能只靠 Member role。

### JWT 一定比 Cookie 好？

不是。同站 SPA 使用 HttpOnly Cookie 可避免 token 暴露給 JS，但需處理 CSRF。JWT 適合特定跨服務或非瀏覽器 client，撤銷、儲存與 XSS 風險需另設計。

### 密碼如何儲存？

使用 Identity password hasher 與 salt，不自行做可逆加密、不記錄密碼。秘密放 User Secrets/CI secrets/環境設定；Gitleaks 是偵測，不是秘密管理本身。

### 限流與 lockout？

限流常按 IP 控大量嘗試；lockout 按帳號控連續失敗。合用降低 password spraying 與單帳號暴力破解，但需考慮 NAT、DoS、觀測與解鎖。

## 情境練習

「已登入且取得 token，POST 仍回 400」：用 correlation/trace 找 error code；檢查 token 的 scheme、`X-DoSelect-Client`、Cookie、`X-XSRF-TOKEN`；再查 origin/preflight/credentials/SameSite/Secure。修正後要驗 CSRF 負例仍被拒絕。

## 專案證據入口

- `FP.dev/src/backend/DoSelect.Api/Security/SecurityServiceCollectionExtensions.cs`
- `FP.dev/src/backend/DoSelect.Api/Security/GlobalAntiforgeryFilter.cs`
- `FP.dev/frontend/shared/src/api/antiforgery.ts`
- `FP.dev/frontend/shared/src/api/client.ts`
- `FP.dev/tests/DoSelect.Api.IntegrationTests/SecurityFoundationTests.cs`
