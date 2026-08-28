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
