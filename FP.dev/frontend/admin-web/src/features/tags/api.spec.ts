import { describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost, PUT: mockPut },
}))

const { createTag, listTags, updateTag } = await import('./api')

describe('tags api', () => {
  it('lists tags with query params derived from params, omitting empty q', async () => {
    mockGet.mockResolvedValueOnce({ data: { items: [], pageNumber: 1, pageSize: 20, totalCount: 0 } })

    await listTags({ q: '', isActive: true, pageNumber: 1, pageSize: 20 })

    expect(mockGet).toHaveBeenCalledWith('/api/v1/admin/tags', {
      params: { query: { Q: undefined, IsActive: true, PageNumber: 1, PageSize: 20 } },
    })
  })

  it('creates a tag by posting the request body as-is', async () => {
    const created = { publicId: 'tag-1', code: 'NEW', nameZhTw: '新品' }
    mockPost.mockResolvedValueOnce({ data: created })

    const request = { code: 'NEW', nameZhTw: '新品', sortOrder: 0, isActive: true }
    const result = await createTag(request)

    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/tags', { body: request })
    expect(result).toBe(created)
  })

  it('updates a tag by publicId, sending the path param and body', async () => {
    const updated = { publicId: 'tag-1', code: 'NEW', nameZhTw: '新品（更新）' }
    mockPut.mockResolvedValueOnce({ data: updated })

    const request = { nameZhTw: '新品（更新）', sortOrder: 0, isActive: true, rowVersion: 'AAA=' }
    const result = await updateTag('tag-1', request)

    expect(mockPut).toHaveBeenCalledWith('/api/v1/admin/tags/{id}', {
      params: { path: { id: 'tag-1' } },
      body: request,
    })
    expect(result).toBe(updated)
  })
})
