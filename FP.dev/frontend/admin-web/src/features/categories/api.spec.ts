import { describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost, PUT: mockPut },
}))

const { createCategory, listCategories, updateCategory } = await import('./api')

describe('categories api', () => {
  it('lists categories with query params derived from params, omitting empty q', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 } })

    await listCategories({ q: '', isActive: true, pageNumber: 1, pageSize: 20 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/categories', {
      params: { query: { Q: undefined, IsActive: true, PageNumber: 1, PageSize: 20 } },
    })
  })

  it('creates a category by posting the request body as-is', async () => {
    const created = { publicId: 'cat-1', code: 'GPU', nameZhTw: '顯示卡' }
    mockPost.mockResolvedValueOnce({ data: created })

    const request = {
      code: 'GPU', nameZhTw: '顯示卡', slug: 'gpu', description: null,
      parentCategoryPublicId: null, sortOrder: 0, isActive: true,
    }
    const result = await createCategory(request)

    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/categories', { body: request })
    expect(result).toBe(created)
  })

  it('updates a category by publicId, sending the path param and body', async () => {
    const updated = { publicId: 'cat-1', code: 'GPU', nameZhTw: '顯示卡（更新）' }
    mockPut.mockResolvedValueOnce({ data: updated })

    const request = {
      nameZhTw: '顯示卡（更新）', slug: 'gpu', description: null,
      parentCategoryPublicId: null, sortOrder: 0, isActive: true, rowVersion: 'AAA=',
    }
    const result = await updateCategory('cat-1', request)

    expect(mockPut).toHaveBeenCalledWith('/api/v1/admin/categories/{id}', {
      params: { path: { id: 'cat-1' } },
      body: request,
    })
    expect(result).toBe(updated)
  })
})
