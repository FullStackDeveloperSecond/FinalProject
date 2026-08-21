// Hands the email the user just tried to log in with off to the forgot-password page without
// putting it in the URL, browser history, or Referer header (unlike a `?email=...` query param).
// sessionStorage is scoped to the tab and cleared on read, so it never outlives this one handoff.
const STORAGE_KEY = 'doselect.forgot-password.email'

export function setPendingForgotPasswordEmail(email: string): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, email)
  } catch {
    // sessionStorage can be unavailable (privacy mode, disabled storage); the prefill is a
    // convenience, not a requirement, so a failure here is silently ignored.
  }
}

export function consumePendingForgotPasswordEmail(): string {
  try {
    const value = sessionStorage.getItem(STORAGE_KEY) ?? ''
    sessionStorage.removeItem(STORAGE_KEY)
    return value
  } catch {
    return ''
  }
}
