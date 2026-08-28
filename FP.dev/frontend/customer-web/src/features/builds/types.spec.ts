import { describe, expect, it } from 'vitest'
import { MAX_BUILD_ITEM_COUNT, MAX_BUILD_ITEM_QUANTITY, validateBuildItems } from './types'

/**
 * 組長 PR #35 round-3 review, P1-2: mirrors EfCompatibilityCheckService.MergeAndValidateItems's
 * own bounds exactly (1–20 raw items, 1–8 per merged SKU) — this is the single source of truth
 * both NewBuildPage.vue and BuildDetailPage.vue gate their save/add-to-cart buttons on, so its
 * boundaries need direct coverage independent of either page's UI.
 */
describe('validateBuildItems', () => {
  it('rejects an empty item list', () => {
    const result = validateBuildItems([])
    expect(result.isValid).toBe(false)
    expect(result.errors.some((error) => error.includes('至少選擇'))).toBe(true)
  })

  it('accepts a quantity of exactly 1, the lower boundary', () => {
    const result = validateBuildItems([{ skuPublicId: 'sku-1', quantity: 1 }])
    expect(result.isValid).toBe(true)
  })

  it('accepts a quantity of exactly 8, the upper boundary', () => {
    const result = validateBuildItems([{ skuPublicId: 'sku-1', quantity: MAX_BUILD_ITEM_QUANTITY }])
    expect(result.isValid).toBe(true)
  })

  it('rejects a quantity of 9, one past the upper boundary', () => {
    const result = validateBuildItems([{ skuPublicId: 'sku-1', quantity: 9 }])
    expect(result.isValid).toBe(false)
    expect(result.errors.some((error) => error.includes('1–8'))).toBe(true)
  })

  it('rejects a quantity of 0, one under the lower boundary', () => {
    const result = validateBuildItems([{ skuPublicId: 'sku-1', quantity: 0 }])
    expect(result.isValid).toBe(false)
  })

  it('rejects a non-integer quantity', () => {
    const result = validateBuildItems([{ skuPublicId: 'sku-1', quantity: 1.5 }])
    expect(result.isValid).toBe(false)
  })

  it('accepts exactly 20 items, the upper boundary', () => {
    const items = Array.from({ length: MAX_BUILD_ITEM_COUNT }, (_, index) => ({ skuPublicId: `sku-${index}`, quantity: 1 }))
    const result = validateBuildItems(items)
    expect(result.isValid).toBe(true)
  })

  it('rejects 21 items, one past the upper boundary', () => {
    const items = Array.from({ length: MAX_BUILD_ITEM_COUNT + 1 }, (_, index) => ({ skuPublicId: `sku-${index}`, quantity: 1 }))
    const result = validateBuildItems(items)
    expect(result.isValid).toBe(false)
    expect(result.errors.some((error) => error.includes('最多 20 項'))).toBe(true)
  })

  it('reports both the count and quantity errors together when both bounds are violated', () => {
    const items = Array.from({ length: MAX_BUILD_ITEM_COUNT + 1 }, (_, index) => ({ skuPublicId: `sku-${index}`, quantity: 9 }))
    const result = validateBuildItems(items)
    expect(result.isValid).toBe(false)
    expect(result.errors).toHaveLength(2)
  })
})
