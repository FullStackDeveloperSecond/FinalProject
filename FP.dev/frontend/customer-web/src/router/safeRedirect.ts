const DEFAULT_REDIRECT = '/'

/**
 * 組長 PR #35 review, item 2: mirrors admin-web's `router/safeRedirect.ts` (same alex review
 * precedent) — a `redirect` query value comes from a URL an attacker fully controls (e.g.
 * `/login?redirect=https://evil.com`), so the one place that reads it back to decide where to
 * send the shopper after login must validate it, not the place that wrote it. Only same-origin,
 * single-leading-slash internal paths are accepted; anything else falls back to `/`.
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
    const base = 'http://doselect-customer.invalid'
    if (new URL(candidate, base).origin !== base) {
      return fallback
    }
  } catch {
    return fallback
  }

  return candidate
}
