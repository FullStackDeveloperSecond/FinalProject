import { BUILD_CATEGORY_SLOTS } from './types'
import type { components } from '@doselect/web-shared/api'
import type { GuestBuildDraft, GuestBuildDraftItem } from './guestBuildDraft'

export type OwnedBuildPart = components['schemas']['AiProductSearchExistingPart']
export interface CartTransferSource {
  publicId: string
  rowVersion: string
  items: { cartItemPublicId: string, skuPublicId: string, quantity: number }[]
}
export interface BuildImport {
  name: string
  items: GuestBuildDraftItem[]
  ownedParts?: OwnedBuildPart[]
  cartSource?: CartTransferSource
}
const pendingKey = 'doselect.build.pendingImport'
const categoryExists = (code: string) => BUILD_CATEGORY_SLOTS.some(slot => slot.code === code)
const validQuantity = (value: unknown) => Number.isInteger(value) && Number(value) >= 1 && Number(value) <= 8

/** Browser data is only a bounded draft, never authority for ownership, stock or price. */
export function parseBuildImport(value: unknown): BuildImport {
  if (!value || typeof value !== 'object') throw new Error('組裝草稿格式錯誤。')
  const data = value as BuildImport
  if (typeof data.name !== 'string' || data.name.length > 160 || !Array.isArray(data.items)
    || !Array.isArray(data.ownedParts ?? []) || data.items.length + (data.ownedParts?.length ?? 0) > 20
    || (data.ownedParts?.length ?? 0) > 12) throw new Error('組裝清單最多 20 項，自有零件最多 12 項。')
  if (data.items.some(item => !item || typeof item.skuPublicId !== 'string' || !item.skuPublicId
    || typeof item.name !== 'string' || item.name.length > 300 || !categoryExists(item.categoryCode)
    || !validQuantity(item.quantity))) throw new Error('請確認零件分類與數量（每項 1–8 件）。')
  if (data.ownedParts?.some(part => !part || !part.confirmedByUser || !validQuantity(Number(part.quantity))
    || !categoryExists(part.categoryCode ?? '') || !Array.isArray(part.specifications) || part.specifications.length > 12
    || !['catalogSku', 'structuredManual'].includes(part.sourceType))) throw new Error('請確認自有零件分類與規格。')
  if (data.cartSource && (typeof data.cartSource.publicId !== 'string' || typeof data.cartSource.rowVersion !== 'string'
    || !Array.isArray(data.cartSource.items) || data.cartSource.items.length > 20
    || data.cartSource.items.some(item => !item || typeof item.cartItemPublicId !== 'string'
      || typeof item.skuPublicId !== 'string' || !validQuantity(item.quantity)))) throw new Error('購物車匯入資料已失效，請重新選取。')
  return structuredClone(data)
}

export function stageBuildImport(value: BuildImport): void {
  const checked = parseBuildImport(JSON.parse(JSON.stringify(value)))
  window.sessionStorage.setItem(pendingKey, JSON.stringify(checked))
}

export function readPendingBuildImport(): BuildImport | null {
  try {
    const raw = window.sessionStorage.getItem(pendingKey)
    return raw && raw.length <= 64_000 ? parseBuildImport(JSON.parse(raw)) : null
  } catch { return null }
}
export function clearPendingBuildImport(): void { window.sessionStorage.removeItem(pendingKey) }

/** Conflicting singleton categories follow the user's explicit keep/replace choice, not an implicit overwrite. */
export function mergeBuildImport(draft: GuestBuildDraft, incoming: BuildImport, mode: 'keep' | 'replace' | 'reset'): GuestBuildDraft {
  const base = mode === 'reset' ? { name: '', items: [], ownedParts: [] } : draft
  const existingCodes = new Set([...base.items.map(item => item.categoryCode), ...(base.ownedParts ?? []).map(part => part.categoryCode)])
  const incomingCodes = new Set([...incoming.items.map(item => item.categoryCode), ...(incoming.ownedParts ?? []).map(part => part.categoryCode)])
  const singleton = (code: string | null | undefined) => BUILD_CATEGORY_SLOTS.some(slot => slot.code === code && slot.singleton)
  const retainBase = (code: string | null | undefined) => !(mode === 'replace' && singleton(code) && incomingCodes.has(code ?? null))
  const retainIncoming = (code: string | null | undefined) => !(mode === 'keep' && singleton(code) && existingCodes.has(code ?? null))
  const merged: GuestBuildDraft = {
    name: base.name || incoming.name,
    items: base.items.filter(item => retainBase(item.categoryCode)).map(item => ({ ...item })),
    ownedParts: [...(base.ownedParts ?? []).filter(part => retainBase(part.categoryCode)), ...(incoming.ownedParts ?? []).filter(part => retainIncoming(part.categoryCode))],
    cartSource: incoming.cartSource ?? base.cartSource,
  }
  for (const item of incoming.items.filter(item => retainIncoming(item.categoryCode))) {
    const existing = merged.items.find(candidate => candidate.skuPublicId === item.skuPublicId)
    const repeatedSource = incoming.cartSource?.items.find(row => row.skuPublicId === item.skuPublicId
      && base.cartSource?.items.some(previous => previous.cartItemPublicId === row.cartItemPublicId))
    const previousQuantity = repeatedSource ? base.cartSource?.items.find(row => row.cartItemPublicId === repeatedSource.cartItemPublicId)?.quantity ?? 0 : 0
    if (existing) existing.quantity = Math.max(0, existing.quantity - previousQuantity) + item.quantity
    else merged.items.push({ ...item })
  }
  for (const slot of BUILD_CATEGORY_SLOTS.filter(slot => slot.singleton)) {
    const count = merged.items.filter(item => item.categoryCode === slot.code).reduce((sum, item) => sum + item.quantity, 0)
      + (merged.ownedParts ?? []).filter(part => part.categoryCode === slot.code).reduce((sum, part) => sum + Number(part.quantity), 0)
    if (count > 1) throw new Error(`${slot.label} 只能選擇一件，請先調整匯入內容。`)
  }
  // Combine only snapshots of the same cart revision. A stale source must never silently become a new purchase.
  if (base.cartSource && incoming.cartSource && mode !== 'reset') {
    if (base.cartSource.publicId !== incoming.cartSource.publicId || base.cartSource.rowVersion !== incoming.cartSource.rowVersion)
      throw new Error('購物車已變更。請先移除舊的購物車匯入項目，再重新挑選。')
    const sources = new Map(base.cartSource.items.map(item => [item.cartItemPublicId, { ...item }]))
    for (const item of incoming.cartSource.items) {
      sources.set(item.cartItemPublicId, { ...item })
    }
    merged.cartSource = { ...incoming.cartSource, items: [...sources.values()] }
  }
  return parseBuildImport(JSON.parse(JSON.stringify(merged)))
}
