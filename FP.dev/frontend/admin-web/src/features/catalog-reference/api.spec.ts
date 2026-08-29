import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet },
}))

const {
  CategoryTreeTruncatedError,
  loadCategoryOptions,
  resolveProductOptions,
  searchProductOptions,
} = await import('./api')

// 每個測試都自己設 implementation，也各自數自己的請求數。
beforeEach(() => {
  mockGet.mockReset()
})

function category(code: string, name = code) {
  return { code, name, publicId: `pub-${code}` }
}

/** `/api/v1/catalog/filter-options` 一次只回一層：無 `Category` 是頂層。 */
function filterOptions(children: Record<string, ReturnType<typeof category>[]>) {
  return (_path: string, options: { params: { query: { Category?: string } } }) =>
    Promise.resolve({
      data: { categories: children[options.params.query.Category ?? ''] ?? [] },
    })
}

describe('loadCategoryOptions', () => {
  it('walks every level and builds a root-to-leaf path for each node', async () => {
    mockGet.mockImplementation(filterOptions({
      '': [category('pc', '電腦')],
      pc: [category('gpu', '顯示卡')],
    }))

    const options = await loadCategoryOptions()

    expect(options).toEqual([
      { publicId: 'pub-pc', code: 'pc', name: '電腦', path: '電腦' },
      { publicId: 'pub-gpu', code: 'gpu', name: '顯示卡', path: '電腦 / 顯示卡' },
    ])
  })

  it('stops instead of looping forever when a category is its own ancestor', async () => {
    // 資料出錯時 parent 可能成環。沒有去重就會一直展開到撞上請求上限。
    mockGet.mockImplementation(filterOptions({
      '': [category('pc')],
      pc: [category('pc')],
    }))

    const options = await loadCategoryOptions()

    expect(options.map(option => option.code)).toEqual(['pc'])
  })

  it('fails loudly rather than returning a partial tree', async () => {
    // 跟 fetchAllPages 同一個理由：不完整的清單會讓少掉的分類看起來像不存在。
    mockGet.mockImplementation((_path: string, options: { params: { query: { Category?: string } } }) => {
      const parent = options.params.query.Category ?? ''
      return Promise.resolve({ data: { categories: [category(`${parent}x`)] } })
    })

    await expect(loadCategoryOptions()).rejects.toBeInstanceOf(CategoryTreeTruncatedError)
  })
})

describe('searchProductOptions', () => {
  it('omits an empty keyword instead of sending Q=', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], totalPages: 0 } })

    await searchProductOptions({ q: '', pageNumber: 1, pageSize: 10 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/products', {
      params: { query: { Q: undefined, PageNumber: 1, PageSize: 10 } },
    })
  })

  it('maps a product card down to the fields the picker shows', async () => {
    mockGet.mockResolvedValueOnce({
      data: {
        items: [{ productPublicId: 'p1', productCode: 'GPU-01', name: '顯示卡', price: {} }],
        totalPages: 3,
      },
    })

    const result = await searchProductOptions({ q: '顯示卡' })

    expect(result.items).toEqual([{ publicId: 'p1', code: 'GPU-01', name: '顯示卡' }])
    expect(result.totalPages).toBe(3)
  })
})

describe('resolveProductOptions', () => {
  it('keeps the products it could resolve when one of them is gone', async () => {
    // 商品下架後端點回 404。整批失敗會讓一張舊券打不開。
    mockGet.mockImplementation((_path: string, options: { params: { path: { id: string } } }) =>
      options.params.path.id === 'missing'
        ? Promise.reject(new Error('not found'))
        : Promise.resolve({ data: { productCode: 'GPU-01', name: '顯示卡' } }))

    const resolved = await resolveProductOptions(['p1', 'missing'])

    expect(resolved.p1).toEqual({ publicId: 'p1', code: 'GPU-01', name: '顯示卡' })
    expect(resolved.missing).toBeUndefined()
  })

  it('does not fan out one request per entry beyond the cap', async () => {
    mockGet.mockResolvedValue({ data: { productCode: 'GPU-01', name: '顯示卡' } })
    const publicIds = Array.from({ length: 200 }, (_, index) => `p${index}`)

    await resolveProductOptions(publicIds)

    expect(mockGet.mock.calls.length).toBeLessThanOrEqual(50)
  })
})
