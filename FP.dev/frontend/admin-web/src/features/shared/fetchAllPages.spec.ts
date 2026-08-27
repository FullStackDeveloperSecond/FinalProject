import { describe, expect, it, vi } from 'vitest'
import { fetchAllPages, FetchAllPagesTruncatedError } from './fetchAllPages'

describe('fetchAllPages', () => {
  it('aggregates every item across multiple pages', async () => {
    const fetchPage = vi.fn(async (pageNumber: number, pageSize: number) => ({
      items: pageNumber === 1 ? ['a', 'b'] : ['c'],
      pageNumber,
      pageSize,
      totalCount: 3,
      totalPages: 2,
    }))

    const items = await fetchAllPages(fetchPage)

    expect(items).toEqual(['a', 'b', 'c'])
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('stops once a page comes back empty, even if totalCount implies more', async () => {
    const fetchPage = vi.fn(async (pageNumber: number, pageSize: number) => ({
      items: pageNumber === 1 ? ['a'] : [],
      pageNumber,
      pageSize,
      totalCount: 999,
    }))

    const items = await fetchAllPages(fetchPage)

    expect(items).toEqual(['a'])
  })

  /**
   * Regression test (組長 PR #24 review round 3, item 2): a result set past the 50-page/5,000-item
   * safety cap used to be returned as-is — silently truncated — so a caller resolving an existing
   * association's code back to a publicId (ProductEditPage's tag list) would treat a partial set as
   * complete and drop anything past item 5,000 on save. It must fail closed instead.
   */
  it('throws instead of silently returning a truncated set past the page cap', async () => {
    const fetchPage = vi.fn(async (pageNumber: number, pageSize: number) => ({
      items: Array.from({ length: pageSize }, (_, i) => `item-${pageNumber}-${i}`),
      pageNumber,
      pageSize,
      totalCount: 999_999,
    }))

    await expect(fetchAllPages(fetchPage)).rejects.toBeInstanceOf(FetchAllPagesTruncatedError)
  })
})
