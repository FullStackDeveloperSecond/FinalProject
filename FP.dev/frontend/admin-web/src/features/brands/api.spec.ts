import { describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost, PUT: mockPut },
}))

const { createBrand, listBrands, updateBrand } = await import('./api')

describe('brands api', () => {
  it('lists brands with query params derived from params, omitting empty q', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 } })

    await listBrands({ q: '', isActive: true, pageNumber: 1, pageSize: 20 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/brands', {
      params: { query: { Q: undefined, IsActive: true, PageNumber: 1, PageSize: 20 } },
    })
  })

  it('creates a brand by posting the request body as-is', async () => {
    const created = { publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme' }
    mockPost.mockResolvedValueOnce({ data: created })

    const request = { code: 'ACME', nameZhTw: 'Acme', description: null, websiteUrl: null, sortOrder: 0, isActive: true }
    const result = await createBrand(request)

    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/brands', { body: request })
    expect(result).toBe(created)
  })

  it('updates a brand by publicId, sending the path param and body', async () => {
    const updated = { publicId: 'brand-1', code: 'ACME', nameZhTw: 'Acme Updated' }
    mockPut.mockResolvedValueOnce({ data: updated })

    const request = { nameZhTw: 'Acme Updated', description: null, websiteUrl: null, sortOrder: 0, isActive: true, rowVersion: 'AAA=' }
    const result = await updateBrand('brand-1', request)

    expect(mockPut).toHaveBeenCalledWith('/api/v1/admin/brands/{id}', {
      params: { path: { id: 'brand-1' } },
      body: request,
    })
    expect(result).toBe(updated)
  })
})
