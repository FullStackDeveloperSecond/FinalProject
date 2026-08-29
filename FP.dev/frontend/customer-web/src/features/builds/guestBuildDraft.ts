const storageKey = 'doselect.guestBuildDraft'

export interface GuestBuildDraftItem {
  skuPublicId: string
  quantity: number
  /** Non-authoritative display label only — always re-resolved against the API on open (M功能桌面UI與Route規格.md 狀態責任表). */
  name: string
  /** Which of the 8 build-component category slots (see `BUILD_CATEGORY_SLOTS`) this item fills. */
  categoryCode: string
}

export interface GuestBuildDraft {
  name: string
  items: GuestBuildDraftItem[]
}

const emptyDraft: GuestBuildDraft = { name: '', items: [] }

export function loadGuestBuildDraft(): GuestBuildDraft {
  const raw = window.localStorage.getItem(storageKey)
  if (!raw) {
    return { name: emptyDraft.name, items: [] }
  }

  try {
    const parsed = JSON.parse(raw) as GuestBuildDraft
    return { name: parsed.name ?? '', items: Array.isArray(parsed.items) ? parsed.items : [] }
  } catch {
    return { name: emptyDraft.name, items: [] }
  }
}

export function saveGuestBuildDraft(draft: GuestBuildDraft): void {
  window.localStorage.setItem(storageKey, JSON.stringify(draft))
}

/** Call once the draft has been saved as a real BuildList via `POST /build-lists`. */
export function clearGuestBuildDraft(): void {
  window.localStorage.removeItem(storageKey)
}

const pendingResumeKey = 'doselect.guestBuildDraft.pendingSaveResume'

/**
 * 組長 PR #35 review round 2, P1-1: auto-resuming a save purely off "session is authenticated and
 * there's a draft in localStorage" fires for anyone who happens to satisfy both — an already-logged
 * -in member just visiting /builds/new, or a member who switched accounts while an old draft was
 * still sitting around — not only a guest returning from the specific 401 -> /login -> back round
 * trip this auto-resume exists for. Call this right before redirecting to /login so the next
 * `authenticated` transition on *this* page load knows it's the one it was waiting for, and
 * `consumePendingBuildSaveResume` below to check and clear that intent — a one-shot signal, not a
 * standing flag. Uses sessionStorage (not localStorage, unlike the draft itself): this is scoped to
 * one specific round trip within the current tab, not something that should survive a browser
 * restart or leak into an unrelated future visit.
 */
export function markPendingBuildSaveResume(): void {
  window.sessionStorage.setItem(pendingResumeKey, '1')
}

/** Consumes (clears) the pending-resume marker and reports whether it was set. */
export function consumePendingBuildSaveResume(): boolean {
  const wasPending = window.sessionStorage.getItem(pendingResumeKey) === '1'
  window.sessionStorage.removeItem(pendingResumeKey)
  return wasPending
}
