import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { useSearchFilters } from './useSearchFilters'

describe('useSearchFilters', () => {
  it('resets pageNumber to 1 immediately when the keyword changes, before the debounce settles', async () => {
    vi.useFakeTimers()
    try {
      const { filters, listParams, goToPage } = useSearchFilters(20, 300)
      goToPage(2)
      expect(filters.pageNumber).toBe(2)

      filters.q = 'new keyword'
      await nextTick()

      expect(filters.pageNumber).toBe(1)
      expect(listParams.value.q).toBe('')

      vi.advanceTimersByTime(300)
      expect(listParams.value.q).toBe('new keyword')
    } finally {
      vi.useRealTimers()
    }
  })

  /**
   * Regression test (組長 PR #24 review round 7, P3): an admin on a later page who starts
   * typing a new keyword used to query that keyword against the stale page number, which could
   * show "no results" even when page 1 had matches. listParams must never combine a new q with
   * an old pageNumber.
   */
  it('never queries a new keyword against a stale pageNumber', async () => {
    vi.useFakeTimers()
    try {
      const { filters, listParams, goToPage } = useSearchFilters(20, 300)
      goToPage(3)

      filters.q = 'x'
      await nextTick()
      vi.advanceTimersByTime(300)

      expect(listParams.value).toEqual({ q: 'x', pageNumber: 1, pageSize: 20 })
    } finally {
      vi.useRealTimers()
    }
  })

  it('search() applies the current input immediately, bypassing the debounce', async () => {
    vi.useFakeTimers()
    try {
      const { filters, listParams, search } = useSearchFilters(20, 300)
      filters.q = 'brand-a'
      await nextTick()

      search()

      expect(listParams.value.q).toBe('brand-a')
      expect(filters.pageNumber).toBe(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('debounces rapid keystrokes into a single query update', async () => {
    vi.useFakeTimers()
    try {
      const { filters, listParams } = useSearchFilters(20, 300)

      filters.q = 'b'
      await nextTick()
      vi.advanceTimersByTime(100)
      filters.q = 'br'
      await nextTick()
      vi.advanceTimersByTime(100)
      filters.q = 'bra'
      await nextTick()

      expect(listParams.value.q).toBe('')

      vi.advanceTimersByTime(300)

      expect(listParams.value.q).toBe('bra')
    } finally {
      vi.useRealTimers()
    }
  })

  it('restores a keyword and later page without resetting that page on the next tick', async () => {
    const { filters, listParams, restore } = useSearchFilters(20, 300)

    restore('saved keyword', 3)
    await nextTick()

    expect(filters.q).toBe('saved keyword')
    expect(listParams.value).toEqual({ q: 'saved keyword', pageNumber: 3, pageSize: 20 })
  })
})
