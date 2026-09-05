import { apiClient } from '../../api/client'
import type {
  AdminProductDetailDto,
  AdminProductExportFormat,
  AdminProductImageDto,
  BulkProductAction,
  BulkProductActionRequest,
  BulkProductActionResultDto,
  CreateProductRequest,
  ProductImageUploadInput,
  UpdateProductImageRequest,
  UpdateProductRequest,
} from './types'

export interface AdminProductListParams {
  q?: string
  brandCodes?: string[]
  categoryCodes?: string[]
  statuses?: string[]
  stockState?: string
  sort?: string
  pageNumber?: number
  pageSize?: number
}

export async function listAdminProducts(params: AdminProductListParams) {
  const { data } = await apiClient.GET('/api/v1/admin/products', {
    params: {
      query: {
        Q: params.q || undefined,
        BrandCodes: params.brandCodes,
        CategoryCodes: params.categoryCodes,
        Statuses: params.statuses,
        StockState: params.stockState || undefined,
        Sort: params.sort || undefined,
        PageNumber: params.pageNumber,
        PageSize: params.pageSize,
      },
    },
  })
  return data!
}

export async function getAdminProduct(publicId: string): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.GET('/api/v1/admin/products/{id}', {
    params: { path: { id: publicId } },
  })
  return data!
}

export async function createProduct(request: CreateProductRequest): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.POST('/api/v1/admin/products', {
    body: request,
  })
  return data!
}

export async function updateProduct(
  publicId: string,
  request: UpdateProductRequest,
): Promise<AdminProductDetailDto> {
  const { data } = await apiClient.PUT('/api/v1/admin/products/{id}', {
    params: { path: { id: publicId } },
    body: request,
  })
  return data!
}

/**
 * UC-ADM-PROD-02 批次上架／下架／調價。後端是整批單一交易——不會有「一半成功」的回應要處理，
 * 所以這裡沒有逐筆結果，只有受影響的數量。
 */
export async function applyBulkProductAction(
  action: BulkProductAction,
  request: BulkProductActionRequest,
): Promise<BulkProductActionResultDto> {
  const { data, error } = await apiClient.POST('/api/v1/admin/products/actions/{bulkAction}', {
    params: { path: { bulkAction: action } },
    body: request,
  })
  if (error) throw error
  return data!
}

/**
 * A-04 匯出。Query 與列表完全相同（「匯出沿用目前 Filter」），刻意不帶分頁——匯出的是整組符合
 * 條件的商品，不是目前這一頁。
 */
export async function exportAdminProducts(
  params: AdminProductListParams,
  format: AdminProductExportFormat = 'csv',
): Promise<Blob> {
  const { data, error } = await apiClient.GET('/api/v1/admin/products/export', {
    params: {
      query: {
        Q: params.q || undefined,
        BrandCodes: params.brandCodes,
        CategoryCodes: params.categoryCodes,
        Statuses: params.statuses,
        StockState: params.stockState || undefined,
        Sort: params.sort || undefined,
        Format: format,
      },
    },
    parseAs: 'blob',
  })
  if (error) throw error
  if (!(data instanceof Blob)) {
    throw new Error('The product export response was not a file.')
  }
  return data
}

// ---------------------------------------------------------------- M-03 商品圖片（A-06）

export async function uploadProductImage(
  productPublicId: string,
  input: ProductImageUploadInput,
): Promise<AdminProductImageDto> {
  const formData = new FormData()
  formData.set('file', input.file)
  if (input.altText) formData.set('altText', input.altText)
  if (input.sourceUrl) formData.set('sourceUrl', input.sourceUrl)
  if (input.licenseName) formData.set('licenseName', input.licenseName)
  if (input.licenseUrl) formData.set('licenseUrl', input.licenseUrl)

  const { data, error } = await apiClient.POST('/api/v1/admin/products/{productId}/images', {
    params: { path: { productId: productPublicId } },
    // openapi-fetch 看到 FormData 就不做 JSON 序列化，讓瀏覽器自己帶 multipart boundary（與匯入同一個做法）。
    body: formData as unknown as Record<string, never>,
  })
  if (error) throw error
  return data!
}

export async function updateProductImage(
  imagePublicId: string,
  request: UpdateProductImageRequest,
): Promise<AdminProductImageDto> {
  const { data, error } = await apiClient.PATCH('/api/v1/admin/product-images/{imageId}', {
    params: { path: { imageId: imagePublicId } },
    body: request,
  })
  if (error) throw error
  return data!
}

export async function publishProductImage(imagePublicId: string, rowVersion: string): Promise<AdminProductImageDto> {
  const { data, error } = await apiClient.POST('/api/v1/admin/product-images/{imageId}/actions/publish', {
    params: { path: { imageId: imagePublicId } },
    body: { rowVersion },
  })
  if (error) throw error
  return data!
}

export async function deleteProductImage(imagePublicId: string, rowVersion: string): Promise<void> {
  const { error } = await apiClient.DELETE('/api/v1/admin/product-images/{imageId}', {
    params: { path: { imageId: imagePublicId } },
    body: { rowVersion },
  })
  if (error) throw error
}
