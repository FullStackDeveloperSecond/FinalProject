import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost },
}))

const {
  loadCategoryOptions,
  maximumBatchSize,
  resolveProductOptions,
  searchProductOptions,
} = await import('./api')

// 每個測試都自己設 implementation，也各自數自己的請求數。
beforeEach(() => {
  mockGet.mockReset()
  mockPost.mockReset()
})

function product(publicId: string, overrides: Record<string, unknown> = {}) {
  return {
    publicId,
    code: `SKU-${publicId}`,
    name: `商品 ${publicId}`,
    status: 'published',
    isSelectable: true,
    ...overrides,
  }
}

describe('loadCategoryOptions', () => {
  it('takes the whole tree in one request', async () => {
    // 先前是對分類樹的每個節點各打一次公開端點（上限 100 次）。
    mockGet.mockResolvedValueOnce({ data: [{ publicId: 'c1', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡', isActive: true }] })

    const options = await loadCategoryOptions()

    expect(mockGet).toHaveBeenCalledTimes(1)
    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/coupons/catalog-options/categories')
    expect(options[0].path).toBe('電腦 / 顯示卡')
  })
})

describe('searchProductOptions', () => {
  it('omits an empty keyword instead of sending Q=', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], hasMore: false } })

    await searchProductOptions({ q: '', pageNumber: 1, pageSize: 10 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/coupons/catalog-options/products', {
      params: { query: { Q: undefined, PageNumber: 1, PageSize: 10 } },
    })
  })

  it('passes the page number through so the caller can reach the second page', async () => {
    // hasMore 沒有翻頁的方法就只是一個沒有出口的狀態。
    mockGet.mockResolvedValueOnce({ data: { items: [product('p2')], hasMore: false } })

    const result = await searchProductOptions({ q: '顯示卡', pageNumber: 2, pageSize: 10 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/coupons/catalog-options/products', {
      params: { query: { Q: '顯示卡', PageNumber: 2, PageSize: 10 } },
    })
    expect(result.hasMore).toBe(false)
    expect(result.items).toHaveLength(1)
  })
})

describe('resolveProductOptions', () => {
  it('resolves any number of ids in a single request', async () => {
    // 先前是逐筆查商品明細，兩個 picker 各最多 50 次。
    mockPost.mockResolvedValueOnce({ data: [product('p1'), product('p2')] })

    const resolved = await resolveProductOptions(['p1', 'p2'])

    expect(mockPost).toHaveBeenCalledTimes(1)
    expect(mockPost).toHaveBeenCalledWith(
      '/api/v1/admin/coupons/catalog-options/products/resolve',
      { body: { publicIds: ['p1', 'p2'] } },
    )
    expect(Object.keys(resolved)).toEqual(['p1', 'p2'])
  })

  it('keeps a discontinued product so an existing rule does not vanish', async () => {
    // 已經寫在券上的參考不能因為挑選器查不到就消失。
    mockPost.mockResolvedValueOnce({
      data: [product('gone', { status: 'discontinued', isSelectable: false })],
    })

    const resolved = await resolveProductOptions(['gone'])

    expect(resolved.gone.isSelectable).toBe(false)
    expect(resolved.gone.status).toBe('discontinued')
  })

  it('does not call the API for an empty selection', async () => {
    const resolved = await resolveProductOptions([])

    expect(resolved).toEqual({})
    expect(mockPost).not.toHaveBeenCalled()
  })

  it('de-duplicates and stays within the server batch limit', async () => {
    mockPost.mockResolvedValueOnce({ data: [] })
    const publicIds = Array.from({ length: maximumBatchSize + 10 }, (_, index) => `p${index}`)

    await resolveProductOptions([...publicIds, publicIds[0]])

    const sent = mockPost.mock.calls[0][1].body.publicIds
    expect(sent).toHaveLength(maximumBatchSize)
    expect(new Set(sent).size).toBe(maximumBatchSize)
  })
})
