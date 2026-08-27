import { describe, expect, it, vi } from 'vitest'

const mockPost = vi.fn()
const mockPut = vi.fn()
const mockDelete = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { POST: mockPost, PUT: mockPut, DELETE: mockDelete },
}))

const { createSku, deleteSku, updateSku } = await import('./api')

describe('skus api', () => {
  it('creates a SKU under a product, sending the path param and body', async () => {
    const created = { publicId: 'sku-1', skuCode: 'SKU-1' }
    mockPost.mockResolvedValueOnce({ data: created })

    const request = {
      skuCode: 'SKU-1', nameZhTw: 'SKU', listPrice: 100, unitCost: 50, weightKg: null,
      lengthCm: null, widthCm: null, heightCm: null, status: 'Draft', isDefault: false,
      requiresPrepayment: false, specifications: [],
    }
    const result = await createSku('prod-1', request)

    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/products/{productId}/skus', {
      params: { path: { productId: 'prod-1' } },
      body: request,
    })
    expect(result).toBe(created)
  })

  it('updates a SKU by publicId, sending the RowVersion in the body', async () => {
    const updated = { publicId: 'sku-1', skuCode: 'SKU-1' }
    mockPut.mockResolvedValueOnce({ data: updated })

    const request = {
      nameZhTw: 'SKU（更新）', listPrice: 100, unitCost: 50, weightKg: null, lengthCm: null,
      widthCm: null, heightCm: null, status: 'Draft', isDefault: false, requiresPrepayment: false,
      specifications: [], rowVersion: 'AAA=',
    }
    const result = await updateSku('sku-1', request)

    expect(mockPut).toHaveBeenCalledWith('/api/v1/admin/skus/{id}', {
      params: { path: { id: 'sku-1' } },
      body: request,
    })
    expect(result).toBe(updated)
  })

  it('deletes a SKU by publicId, sending the RowVersion in the body', async () => {
    mockDelete.mockResolvedValueOnce({ data: undefined })

    await deleteSku('sku-1', 'AAA=')

    expect(mockDelete).toHaveBeenCalledWith('/api/v1/admin/skus/{id}', {
      params: { path: { id: 'sku-1' } },
      body: { rowVersion: 'AAA=' },
    })
  })
})
