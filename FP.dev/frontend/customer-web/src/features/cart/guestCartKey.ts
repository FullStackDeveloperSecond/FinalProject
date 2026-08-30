const storageKey = 'doselect.guestCartKey'

// Matches CartMergeRequest.GuestCartKey's [StringLength(256, MinimumLength = 32)] contract
// (ShoppingCartContracts.cs) — any stored value outside this range can never succeed against
// the backend.
const MIN_LENGTH = 32
const MAX_LENGTH = 256

function isValidGuestCartKey(value: string): boolean {
  return value.length >= MIN_LENGTH && value.length <= MAX_LENGTH
}

/**
 * Not an auth credential — just a cart-correlation token the guest's browser holds so the
 * backend can find their cart again. localStorage (not a cookie) because it's non-essential
 * and JS needs to read it back out to send as the `X-DoSelect-Guest-Cart-Key` header anyway.
 *
 * A stored value that's present but the wrong length (corrupted write, a leftover value from a
 * different app version, manual tampering) used to be reused as-is forever — every request would
 * keep failing with the same 400 and retrying would resend the identical bad value, with no way
 * out short of the guest manually clearing browser storage (組長 PR #29 review, item 6). Validate
 * on every read and silently replace an invalid value instead.
 */
export function getOrCreateGuestCartKey(): string {
  const existing = window.localStorage.getItem(storageKey)
  if (existing && isValidGuestCartKey(existing)) {
    return existing
  }

  const created = crypto.randomUUID()
  window.localStorage.setItem(storageKey, created)
  return created
}

/** Call once a merge into a member cart succeeds — the guest cart is Converted server-side. */
export function clearGuestCartKey(): void {
  window.localStorage.removeItem(storageKey)
}
