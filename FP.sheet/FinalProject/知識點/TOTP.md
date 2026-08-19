---
type: knowledge
title: TOTP
aliases:
  - Time-based One-Time Password
  - 驗證器動態密碼
  - 兩因素驗證
tags:
  - 知識點
  - 身分驗證
  - 2FA
  - 安全
created_at: 2026-08-13
related:
  - "[[02-領域需求/會員、驗證與通知]]"
  - "[[03-架構/API共通規範]]"
  - "[[知識點/ASP.NET Core Identity]]"
---

# TOTP

## 它是什麼

TOTP（Time-based One-Time Password）以帳號與驗證器共同持有的 Secret，加上目前時間區段計算短效數字碼。它通常作為密碼之外的第二因素；驗證器不需每次連網。

```text
Shared Secret＋目前時間步長
→ HMAC 計算
→ 截取成 6 位數短碼
→ 短時間後失效
```

伺服器可允許很小的前後時間窗吸收裝置時鐘誤差，但時間窗越寬，攻擊者可猜測的有效碼越多。

## 完整生命週期

安全實作不只登入頁輸入六位數，還包含：

- 綁定時重新驗證高可信身分並要求輸入一次有效碼。
- QR Code／Secret 只在必要畫面短暫顯示，不進 Log、Audit Diff 或截圖文件。
- Recovery Codes 單次使用、雜湊保存並可重新產生。
- 失敗嘗試限流與鎖定，避免暴力猜碼。
- 重綁或解除後撤銷既有管理 Session，留下稽核。
- 時鐘同步異常需可診斷，但不可在錯誤回應暴露 Secret。

TOTP 能降低密碼外洩風險，但無法完全抵抗即時釣魚或被控制的裝置；高權限操作仍需最小權限、短 Session 與稽核。

> [!note] 專案決策邊界
> 管理員強制 TOTP，完成後才建立管理 Cookie；Session 絕對期限 2 小時且不滑動。Authenticator Key 與 Recovery Code 使用 Identity 既有 Provider／Store，不另存可讀 Secret。

## 參考資料

- [RFC 6238：TOTP](https://www.rfc-editor.org/rfc/rfc6238)
- [Microsoft Learn：Enable QR code generation for TOTP authenticator apps](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-enable-qrcodes)
- [[02-領域需求/會員購物與售後驗收規格]]
