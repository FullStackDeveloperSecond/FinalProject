import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { clearGuestCartKey, getOrCreateGuestCartKey } from './guestCartKey'

const storageKey = 'doselect.guestCartKey'

describe('guestCartKey', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  afterEach(() => {
    window.localStorage.clear()
  })

  it('creates and persists a new key when none exists', () => {
    const key = getOrCreateGuestCartKey()

    expect(key).toBeTruthy()
    expect(window.localStorage.getItem(storageKey)).toBe(key)
  })

  it('reuses an existing valid key', () => {
    const first = getOrCreateGuestCartKey()
    const second = getOrCreateGuestCartKey()

    expect(second).toBe(first)
  })

  /**
   * Regression test (組長 PR #29 review, item 6): a stored value outside
   * CartMergeRequest.GuestCartKey's [32, 256] contract used to be reused forever, so the guest
   * kept resending the same value the backend would always 400 on, with no way out short of
   * manually clearing browser storage.
   */
  it('replaces a corrupted value shorter than the minimum length', () => {
    window.localStorage.setItem(storageKey, 'too-short')

    const key = getOrCreateGuestCartKey()

    expect(key).not.toBe('too-short')
    expect(key.length).toBeGreaterThanOrEqual(32)
    expect(window.localStorage.getItem(storageKey)).toBe(key)
  })

  it('replaces a corrupted value longer than the maximum length', () => {
    window.localStorage.setItem(storageKey, 'x'.repeat(300))

    const key = getOrCreateGuestCartKey()

    expect(key.length).toBeLessThanOrEqual(256)
    expect(window.localStorage.getItem(storageKey)).toBe(key)
  })

  it('replaces an empty stored value', () => {
    window.localStorage.setItem(storageKey, '')

    const key = getOrCreateGuestCartKey()

    expect(key).toBeTruthy()
  })

  it('clearGuestCartKey removes the stored value', () => {
    getOrCreateGuestCartKey()
    clearGuestCartKey()

    expect(window.localStorage.getItem(storageKey)).toBeNull()
  })
})
