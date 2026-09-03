import { describe, expect, it, vi } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()
const mockPatch = vi.fn()
const mockDelete = vi.fn()

vi.mock('../../api/client', () => ({
  apiClient: { GET: mockGet, POST: mockPost, PUT: mockPut, PATCH: mockPatch, DELETE: mockDelete },
}))

const {
  createProduct,
  deleteProductImage,
  getAdminProduct,
  listAdminProducts,
  publishProductImage,
  updateProduct,
  updateProductImage,
  uploadProductImage,
} = await import('./api')

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
      defaultSku: {
        skuCode: 'SKU-1', nameZhTw: '預設規格', listPrice: 1000, unitCost: 700,
        weightKg: null, lengthCm: null, widthCm: null, heightCm: null,
        status: 'Draft', isDefault: true, requiresPrepayment: false, specifications: [],
      },
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

describe('admin product images api', () => {
  it('uploads the image as multipart form data with only the provided metadata fields', async () => {
    const dto = { publicId: 'img-1' }
    mockPost.mockResolvedValueOnce({ data: dto })
    const file = new File(['png'], 'front.png', { type: 'image/png' })

    const result = await uploadProductImage('p1', { file, altText: 'front', licenseName: 'CC0' })

    expect(result).toBe(dto)
    // 同一個檔案裡前面的測試也用過 mockPost，這裡看的是最後一次呼叫。
    const [path, options] = mockPost.mock.calls.at(-1)!
    expect(path).toBe('/api/v1/admin/products/{productId}/images')
    expect(options.params).toEqual({ path: { productId: 'p1' } })
    const body = options.body as FormData
    expect(body).toBeInstanceOf(FormData)
    expect(body.get('file')).toBe(file)
    expect(body.get('altText')).toBe('front')
    expect(body.get('licenseName')).toBe('CC0')
    expect(body.has('sourceUrl')).toBe(false)
    expect(body.has('licenseUrl')).toBe(false)
  })

  it('patches metadata, publishes and deletes with the RowVersion in the body', async () => {
    mockPatch.mockResolvedValueOnce({ data: { publicId: 'img-1' } })
    mockPost.mockResolvedValueOnce({ data: { publicId: 'img-1', status: 'Published' } })
    mockDelete.mockResolvedValueOnce({})
    const request = {
      altText: 'front', sortOrder: 1, sourceUrl: null, licenseName: null, licenseUrl: null, rowVersion: 'AAA=',
    }

    await updateProductImage('img-1', request)
    await publishProductImage('img-1', 'AAA=')
    await deleteProductImage('img-1', 'AAA=')

    expect(mockPatch).toHaveBeenCalledWith('/api/v1/admin/product-images/{imageId}', {
      params: { path: { imageId: 'img-1' } },
      body: request,
    })
    expect(mockPost).toHaveBeenCalledWith('/api/v1/admin/product-images/{imageId}/actions/publish', {
      params: { path: { imageId: 'img-1' } },
      body: { rowVersion: 'AAA=' },
    })
    expect(mockDelete).toHaveBeenCalledWith('/api/v1/admin/product-images/{imageId}', {
      params: { path: { imageId: 'img-1' } },
      body: { rowVersion: 'AAA=' },
    })
  })

  it('rethrows the ApiError openapi-fetch hands back for a failed upload', async () => {
    const error = new Error('file_format_invalid')
    mockPost.mockResolvedValueOnce({ error })

    await expect(uploadProductImage('p1', { file: new File([''], 'x.txt') })).rejects.toBe(error)
  })
})
