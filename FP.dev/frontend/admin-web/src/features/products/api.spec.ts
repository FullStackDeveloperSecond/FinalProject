import { describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost, PUT: mockPut },
}))

const { createProduct, getAdminProduct, listAdminProducts, updateProduct } = await import('./api')

describe('admin products api', () => {
  it('lists products with array filters and query params derived from params', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 } })

    await listAdminProducts({
      q: '', brandCodes: ['ACME'], categoryCodes: ['GPU'], statuses: ['Draft'],
      stockState: '', sort: '', pageNumber: 1, pageSize: 20,
    })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/products', {
      params: {
        query: {
          Q: undefined,
          BrandCodes: ['ACME'],
          CategoryCodes: ['GPU'],
          Statuses: ['Draft'],
          StockState: undefined,
          Sort: undefined,
          PageNumber: 1,
          PageSize: 20,
        },
      },
    })
  })

  it('gets a product by publicId', async () => {
    const detail = { publicId: 'prod-1', productCode: 'PROD-1' }
    mockGet.mockResolvedValueOnce({ data: detail })

    const result = await getAdminProduct('prod-1')

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/products/{id}', {
      params: { path: { id: 'prod-1' } },
    })
    expect(result).toBe(detail)
  })

  it('creates a product by posting the request body as-is', async () => {
    const created = { publicId: 'prod-1', productCode: 'PROD-1' }
    mockPost.mockResolvedValueOnce({ data: created })

    const request = {
      productCode: 'PROD-1', nameZhTw: '商品', brandPublicId: 'brand-1', categoryPublicId: 'cat-1',
      descriptionZhTw: null, warrantyMonths: null, tagPublicIds: [], status: 'Draft',
    }
    const result = await createProduct(request)

    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/products', { body: request })
    expect(result).toBe(created)
  })

  it('updates a product by publicId, sending the path param and body', async () => {
    const updated = { publicId: 'prod-1', productCode: 'PROD-1' }
    mockPut.mockResolvedValueOnce({ data: updated })

    const request = {
      nameZhTw: '商品（更新）', brandPublicId: 'brand-1', categoryPublicId: 'cat-1',
      descriptionZhTw: null, warrantyMonths: null, tagPublicIds: [], status: 'Draft', rowVersion: 'AAA=',
    }
    const result = await updateProduct('prod-1', request)

    expect(mockPut).toHaveBeenCalledWith('/api/v1/admin/products/{id}', {
      params: { path: { id: 'prod-1' } },
      body: request,
    })
    expect(result).toBe(updated)
  })
})
