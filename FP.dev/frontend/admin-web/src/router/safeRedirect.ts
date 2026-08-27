const DEFAULT_REDIRECT = '/'

/**
 * ⚠ alex review 第三輪 P2#4：`redirect` 這個 query 參數的「來源」（router guard 用
 * `to.fullPath`、401 handler 用目前路由的 `fullPath`）本身已經是安全的站內路徑，不是使用者
 * 輸入；但它的「消費點」——登入／MFA 完成後讀出這個值決定要導去哪裡——攻擊者可以透過一個
 * 惡意連結（例如 `/login?redirect=https://evil.com` 或 `/login?redirect=//evil.com`）
 * 直接控制這個查詢參數的值，讓受害者完成登入後被導去釣魚站。這裡在唯一的消費點做集中驗證，
 * 只接受同源、以單一 `/` 開頭的站內路徑，其餘一律退回預設值。
 */
export function resolveSafeRedirect(candidate: unknown, fallback: string = DEFAULT_REDIRECT): string {
  if (
    typeof candidate !== 'string' ||
    candidate.length === 0 ||
    !candidate.startsWith('/') ||
    candidate.startsWith('//') ||
    candidate.startsWith('/\\')
  ) {
    return fallback
  }

  try {
    const base = 'http://doselect-admin.invalid'
    if (new URL(candidate, base).origin !== base) {
      return fallback
    }
  } catch {
    return fallback
  }

  return candidate
}
